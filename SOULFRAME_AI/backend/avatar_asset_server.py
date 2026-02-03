import json
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Dict, List, Optional
from urllib.parse import urlparse

import requests
from fastapi import FastAPI, HTTPException, Request
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import FileResponse
from pydantic import BaseModel

ROOT_DIR = Path(__file__).resolve().parent
STORE_DIR = ROOT_DIR / "avatar_store"
MODELS_DIR = STORE_DIR / "models"
META_PATH = STORE_DIR / "avatars.json"

ALLOWED_ORIGINS = [
    "http://localhost:8000",
    "http://127.0.0.1:8000",
]

LOCAL_MODELS = [
    {
        "avatar_id": "LOCAL_model1",
        "source": "local",
        "local_file": "model1.glb",
        "display_name": "Local Model 1",
    },
    {
        "avatar_id": "LOCAL_model2",
        "source": "local",
        "local_file": "model2.glb",
        "display_name": "Local Model 2",
    },
]

app = FastAPI(title="Avatar Asset Server")

app.add_middleware(
    CORSMiddleware,
    allow_origins=ALLOWED_ORIGINS,
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat()


def ensure_store() -> None:
    MODELS_DIR.mkdir(parents=True, exist_ok=True)
    if not META_PATH.exists():
        META_PATH.write_text(json.dumps({"avatars": []}, indent=2), encoding="utf-8")


def load_metadata() -> Dict[str, Any]:
    ensure_store()
    try:
        with META_PATH.open("r", encoding="utf-8") as handle:
            return json.load(handle)
    except (json.JSONDecodeError, OSError):
        return {"avatars": []}


def save_metadata(data: Dict[str, Any]) -> None:
    ensure_store()
    temp_path = META_PATH.with_suffix(".json.tmp")
    temp_path.write_text(json.dumps(data, indent=2), encoding="utf-8")
    temp_path.replace(META_PATH)


def is_valid_http_url(url: str) -> bool:
    try:
        parsed = urlparse(url)
    except ValueError:
        return False
    return parsed.scheme in {"http", "https"} and bool(parsed.netloc)


def compute_hash(url: str) -> str:
    import hashlib

    return hashlib.sha256(url.encode("utf-8")).hexdigest()


def get_avatar_by_id(data: Dict[str, Any], avatar_id: str) -> Optional[Dict[str, Any]]:
    for item in data.get("avatars", []):
        if item.get("avatar_id") == avatar_id:
            return item
    return None


def get_avatar_by_hash(data: Dict[str, Any], url_hash: str) -> Optional[Dict[str, Any]]:
    for item in data.get("avatars", []):
        if item.get("url_hash") == url_hash:
            return item
    return None


def next_unique_avatar_id(data: Dict[str, Any], base_id: str) -> str:
    existing = {item.get("avatar_id") for item in data.get("avatars", [])}
    if base_id not in existing:
        return base_id
    suffix = 2
    while f"{base_id}_{suffix}" in existing:
        suffix += 1
    return f"{base_id}_{suffix}"


def download_file(url: str, target_path: Path) -> int:
    temp_path = target_path.with_suffix(target_path.suffix + ".part")
    try:
        with requests.get(url, stream=True, timeout=60) as response:
            response.raise_for_status()
            bytes_written = 0
            with temp_path.open("wb") as handle:
                for chunk in response.iter_content(chunk_size=1024 * 1024):
                    if not chunk:
                        continue
                    handle.write(chunk)
                    bytes_written += len(chunk)
    except requests.RequestException as exc:
        if temp_path.exists():
            temp_path.unlink(missing_ok=True)
        raise HTTPException(status_code=502, detail=f"Download failed: {exc}")

    temp_path.replace(target_path)
    return bytes_written


class ImportRequest(BaseModel):
    avatar_id: Optional[str] = None
    url: str
    gender: Optional[str] = None
    bodyId: Optional[str] = None
    urlType: Optional[str] = None
    display_name: Optional[str] = None


@app.get("/health")
def health() -> Dict[str, Any]:
    data = load_metadata()
    return {
        "status": "ok",
        "store_dir": str(STORE_DIR),
        "models_dir": str(MODELS_DIR),
        "count": len(data.get("avatars", [])),
    }


@app.get("/avatars/list")
def list_avatars(request: Request) -> Dict[str, Any]:
    data = load_metadata()
    base_url = str(request.base_url).rstrip("/")

    items: List[Dict[str, Any]] = []
    items.extend(LOCAL_MODELS)

    for item in data.get("avatars", []):
        cached_url = f"{base_url}/avatars/{item['avatar_id']}/model.glb"
        item_copy = dict(item)
        item_copy["cached_glb_url"] = cached_url
        items.append(item_copy)

    return {"avatars": items}


@app.post("/avatars/import")
def import_avatar(payload: ImportRequest, request: Request) -> Dict[str, Any]:
    if not is_valid_http_url(payload.url):
        raise HTTPException(status_code=400, detail="Invalid url (http/https required)")

    data = load_metadata()
    url_hash = compute_hash(payload.url)
    existing = get_avatar_by_hash(data, url_hash)
    if existing:
        existing_path = Path(existing.get("file_path", ""))
        if existing_path.exists():
            existing["last_access"] = utc_now()
            save_metadata(data)
            return {
                "ok": True,
                "avatar_id": existing["avatar_id"],
                "cached_glb_url": f"{str(request.base_url).rstrip('/')}/avatars/{existing['avatar_id']}/model.glb",
                "bytes": existing.get("bytes", 0),
                "dedup": True,
            }
        data["avatars"] = [
            entry for entry in data.get("avatars", []) if entry.get("avatar_id") != existing.get("avatar_id")
        ]

    avatar_id = payload.avatar_id or f"avaturn_{url_hash[:8]}"
    avatar_id = next_unique_avatar_id(data, avatar_id)

    filename = f"{url_hash}_{avatar_id}.glb"
    target_path = MODELS_DIR / filename

    bytes_written = download_file(payload.url, target_path)

    record = {
        "avatar_id": avatar_id,
        "source": "avaturn",
        "source_url": payload.url,
        "url_hash": url_hash,
        "file_path": str(target_path),
        "bytes": bytes_written,
        "created_at": utc_now(),
        "last_access": utc_now(),
        "gender": payload.gender,
        "bodyId": payload.bodyId,
        "urlType": payload.urlType,
        "display_name": payload.display_name,
    }

    data.setdefault("avatars", []).insert(0, record)
    save_metadata(data)

    return {
        "ok": True,
        "avatar_id": avatar_id,
        "cached_glb_url": f"{str(request.base_url).rstrip('/')}/avatars/{avatar_id}/model.glb",
        "bytes": bytes_written,
        "dedup": False,
    }


@app.get("/avatars/{avatar_id}/model.glb")
def get_avatar_model(avatar_id: str) -> FileResponse:
    data = load_metadata()
    item = get_avatar_by_id(data, avatar_id)
    if not item:
        raise HTTPException(status_code=404, detail="Avatar not found")

    path = Path(item.get("file_path", ""))
    if not path.exists():
        raise HTTPException(status_code=404, detail="File missing")

    item["last_access"] = utc_now()
    save_metadata(data)

    return FileResponse(path, media_type="model/gltf-binary", filename=path.name)


@app.delete("/avatars/{avatar_id}")
def delete_avatar(avatar_id: str) -> Dict[str, Any]:
    data = load_metadata()
    item = get_avatar_by_id(data, avatar_id)
    if not item:
        raise HTTPException(status_code=404, detail="Avatar not found")

    file_path = Path(item.get("file_path", ""))
    data["avatars"] = [entry for entry in data.get("avatars", []) if entry.get("avatar_id") != avatar_id]
    save_metadata(data)

    if file_path.exists():
        file_path.unlink(missing_ok=True)

    return {"ok": True, "avatar_id": avatar_id, "deleted": True}


if __name__ == "__main__":
    import uvicorn

    uvicorn.run("avatar_asset_server:app", host="127.0.0.1", port=8003, reload=False)
