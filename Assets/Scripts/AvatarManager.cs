using System;
using Avaturn.Core.Runtime.Scripts.Avatar;
using Avaturn.Core.Runtime.Scripts.Avatar.Data;
using System.Collections.Generic;
using System.IO;
using System.Collections;
using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

public class AvatarManager : MonoBehaviour
{
    [System.Serializable]
    public class SavedAvatarData
    {
        public List<AvatarData> avatars = new List<AvatarData>();
    }

    [System.Serializable]
    public class AvatarData
    {
        public string avatarId;
        public string url;          // <-- SALVIAMO L’URL VERO (httpURL)
        public string urlType;
        public string bodyId;
        public string gender;

        // opzionale (solo non-webgl)
        public string fileName;
        public bool isWebGL;

        public AvatarData(AvatarInfo info, bool isWebGL)
        {
            avatarId = info.AvatarId;
            url = info.Url;
            urlType = info.UrlType;
            bodyId = info.BodyId;
            gender = info.Gender;
            this.isWebGL = isWebGL;

            fileName = $"avatar_{avatarId}.glb";
        }

        public AvatarInfo ToAvatarInfo()
        {
            // Se abbiamo già un URL valido (http/file) o è un modello locale, usa quello e basta
            if (!string.IsNullOrEmpty(url) &&
                (isWebGL || url.StartsWith("http://") || url.StartsWith("https://") || url.StartsWith("file://") ||
                 bodyId == "local" || (avatarId != null && avatarId.StartsWith("LOCAL_"))))
            {
                return new AvatarInfo(url, urlType, bodyId, gender, avatarId);
            }

            // Fallback 1, se esiste salvataggio
            var path = Path.Combine(Application.persistentDataPath, fileName);
            if (File.Exists(path))
                return new AvatarInfo($"file://{path}", urlType, bodyId, gender, avatarId);

            // Ultimo fallback: prova comunque l'url salvato
            return new AvatarInfo(url, urlType, bodyId, gender, avatarId);
        }
    }

    [Header("Riferimenti Core")]
    public DownloadAvatar downloadAvatar;
    public Transform spawnPoint;

    [Header("UI Reference")]
    public UIManager uiManager;

    [Header("Replace target")]
    public Transform baseModelToReplace; // assegna Avaturn-BaseModel da Inspector
    public bool destroyBaseModel = true;


    [Header("Local test models (StreamingAssets)")]
    [SerializeField] private bool addLocalTestModels = true;
    [SerializeField] private string localModel2FileName = "model2.glb";
    [SerializeField] private string localModel2AvatarId = "LOCAL_model2";
    [SerializeField] private string localModel2Gender = "male";

    [Header("Animation")]
    public RuntimeAnimatorController idleController;
    public bool animatorApplyRootMotion = false;
    public AvaturnIdleLookAndBlink idleLook;


    public SavedAvatarData SavedData => savedData;

    private SavedAvatarData savedData;
    private string savePath;
    private GameObject currentAvatar;

    private Transform _baseParent;
    private Vector3 _baseLocalPos;
    private Quaternion _baseLocalRot;
    private Vector3 _baseLocalScale;
    private bool _basePoseCached;

    public AvaturnULipSyncBinder lipSyncBinder;
    public ULipSyncProfileRouter profileRouter;


    void Awake()
    {
        savePath = Path.Combine(Application.persistentDataPath, "Avatars.json");
        LoadData();

        if (addLocalTestModels)
            EnsureLocalModelInList();

        if (downloadAvatar != null)
            downloadAvatar.SetOnDownloaded(OnAvatarDownloaded);

        if (baseModelToReplace != null)
        {
            _baseParent = baseModelToReplace.parent;
            _baseLocalPos = baseModelToReplace.localPosition;
            _baseLocalRot = baseModelToReplace.localRotation;
            _baseLocalScale = baseModelToReplace.localScale;
            _basePoseCached = true;
            if (idleLook != null)
                idleLook.Setup(baseModelToReplace); // imposta animazione sul modello base

            baseModelToReplace.gameObject.SetActive(true);
            destroyBaseModel = false;
        }
    }


    public void OnAvatarReceived(AvatarInfo avatarInfo)
    {
        Debug.Log($"Avatar ricevuto: {avatarInfo.AvatarId}");

        uiManager?.UpdateDebugText($"Avatar ricevuto ({avatarInfo.Gender}) - elaborazione...");

#if UNITY_WEBGL && !UNITY_EDITOR
        // In WebGL dobbiamo ricevere un http(s) URL per caricarlo con GLTFast/UnityWebRequest
        if (!avatarInfo.Url.StartsWith("http://") && !avatarInfo.Url.StartsWith("https://"))
        {
            Debug.LogError("WEBGL: ricevuto un URL non-http(s). Imposta Export type = HttpURL in Avaturn (o forza avaturnForceExportHttpUrl).");
            uiManager?.UpdateDebugText("Errore: su WebGL serve HttpURL. Controlla Export type su Avaturn.");
            return;
        }

        // Salva metadati (incl. url)
        var meta = new AvatarData(avatarInfo, true);
        if (!AvatarExists(meta.avatarId))
        {
            savedData.avatars.Add(meta);
            SaveData();
        }

        // Carica subito
        LoadAvatarImmediately(avatarInfo);
#else
        // Altre piattaforme: puoi continuare come preferisci (http o file system).
        var meta = new AvatarData(avatarInfo, false);
        if (!AvatarExists(meta.avatarId))
        {
            savedData.avatars.Add(meta);
            SaveData();
        }
        LoadAvatarImmediately(avatarInfo);
#endif

        profileRouter?.ApplyGender(avatarInfo.Gender); // Applica profilo LipSync in base al genere
    }

    void LoadAvatarImmediately(AvatarInfo avatarInfo)
    {
        if (downloadAvatar != null)
        {
            downloadAvatar.SetOnDownloaded(OnAvatarDownloaded);
            downloadAvatar.Download(avatarInfo);
        } else
            Debug.LogError("DownloadAvatar reference mancante in AvatarManager");

        uiManager?.OnAvatarDownloadStarted(avatarInfo);
    }

    public void LoadSavedAvatar(AvatarData avatarData)
    {
        Debug.Log($"Caricamento avatar salvato: {avatarData.avatarId}");
        uiManager?.UpdateDebugText($"Caricamento avatar {avatarData.avatarId}...");

        profileRouter?.ApplyGender(avatarData.gender);

        var info = avatarData.ToAvatarInfo();
        LoadAvatarImmediately(info);
    }

    public void OnAvatarDownloaded(Transform loaderTransform)
    {
        Debug.Log($"Avatar caricato nella scena (loader): {loaderTransform.name} children={loaderTransform.childCount}");

        if (loaderTransform.childCount == 0)
        {
            Debug.LogError("Load OK ma nessun child instanziato sotto il Loader.");
            return;
        }

        Transform parent = _basePoseCached ? _baseParent : spawnPoint;

        var containerGO = new GameObject("CurrentAvatar");
        var container = containerGO.transform;
        container.SetParent(parent, false);

        if (_basePoseCached)
        {
            container.localPosition = _baseLocalPos;
            container.localRotation = _baseLocalRot;
            container.localScale = _baseLocalScale;

            // Nascondi il placeholder
            if (baseModelToReplace != null) baseModelToReplace.gameObject.SetActive(false);
        }
        else
        {
            container.localPosition = Vector3.zero;
            container.localRotation = Quaternion.identity;
            container.localScale = Vector3.one;
        }


        // elimina eventuale avatar precedente
        if (currentAvatar != null) Destroy(currentAvatar);

        // allinea il Loader al container (come fa PrepareAvatar di Avaturn)
        loaderTransform.position = container.position;
        loaderTransform.rotation = container.rotation;

        //loaderTransform.localScale = Vector3.one;

        // sposta i figli dal Loader al container
        while (loaderTransform.childCount > 0)
        {
            var child = loaderTransform.GetChild(0);
            child.SetParent(container, false); // Usa worldPositionStays = false e poi azzera offset sul root
            
            child.localPosition = Vector3.zero;
            child.localRotation = Quaternion.identity;
        }

        currentAvatar = container.gameObject;

        StartCoroutine(SetupAnimatorNextFrame(currentAvatar));

        if (lipSyncBinder != null)
            StartCoroutine(SetupLipSyncNextFrame(container));
        else
            Debug.LogWarning("[LipSync] lipSyncBinder non assegnato in AvatarManager (Inspector).");

        if (idleLook != null)
            StartCoroutine(SetupIdleLookNextFrame(container));

        // Debug utilissimo: ora deve essere vicino allo spawnpoint
        Debug.Log($"[Avatar] container world={container.position} local={container.localPosition} children={container.childCount}");

        uiManager?.OnAvatarDownloaded(container);
    }

    private IEnumerator SetupLipSyncNextFrame(Transform avatarRoot)
    {
        // aspetta 1 frame: su WebGL/GLTFast evita edge-case mentre Unity finalizza renderer/mesh
        yield return null;
        lipSyncBinder.Setup(avatarRoot);
    }

    private IEnumerator SetupAnimatorNextFrame(GameObject avatarGO)
    {
        yield return null;

        if (idleController == null)
        {
            Debug.LogWarning("[Anim] idleController non assegnato (AvatarManager). Rimarra' in T-pose.");
            yield break;
        }

        var animator = avatarGO.GetComponent<Animator>();
        if (animator == null) animator = avatarGO.AddComponent<Animator>();

        animator.applyRootMotion = animatorApplyRootMotion;
        animator.runtimeAnimatorController = idleController;

        // chiave: costruisce l’Avatar humanoid a runtime
        animator.avatar = HumanoidAvatarBuilder.Build(avatarGO);

        // utile in WebGL / camera lontane
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        Debug.Log("[Anim] Animator pronto: Idle in play.");
    }

    private IEnumerator SetupIdleLookNextFrame(Transform avatarRoot)
    {
        yield return null;
        idleLook.Setup(avatarRoot);
    }



    public void RemoveCurrentAvatar()
    {
        // Ferma logiche legate all’avatar runtime
        if (idleLook != null) idleLook.Clear();

        if (lipSyncBinder != null)
            lipSyncBinder.ClearTargets();

        if (currentAvatar != null)
        {
            Destroy(currentAvatar);
            currentAvatar = null;
        }

        // Riattiva il placeholder
        if (baseModelToReplace != null)
        {
            baseModelToReplace.gameObject.SetActive(true);

            // Rifai Setup sul modello base (al frame dopo l'enable)
            if (idleLook != null)
                StartCoroutine(SetupIdleLookOnBaseNextFrame());
        }
    }

    private IEnumerator SetupIdleLookOnBaseNextFrame()
    {
        yield return null;
        if (idleLook != null && baseModelToReplace != null)
            idleLook.Setup(baseModelToReplace);
    }




#if UNITY_WEBGL && !UNITY_EDITOR
[DllImport("__Internal")] private static extern void JS_FileSystem_Sync();
#endif
    void SaveData()
    {
        try
        {
            string json = JsonUtility.ToJson(savedData, true);
            File.WriteAllText(savePath, json);

            #if UNITY_WEBGL && !UNITY_EDITOR
                JS_FileSystem_Sync();
            #endif

            Debug.Log("Dati salvati: " + savedData.avatars.Count + " avatar(s)");
        }
        catch (Exception e)
        {
            Debug.LogError($"Errore nel salvataggio dati: {e.Message}");
        }
    }

    void LoadData()
    {
        if (File.Exists(savePath))
        {
            try
            {
                var json = File.ReadAllText(savePath);
                savedData = JsonUtility.FromJson<SavedAvatarData>(json) ?? new SavedAvatarData();
                Debug.Log($"Dati caricati: {savedData.avatars.Count} avatar(s)");
            }
            catch (Exception e)
            {
                Debug.LogError($"Errore nel caricamento dati: {e.Message}");
                savedData = new SavedAvatarData();
            }
        }
        else
        {
            savedData = new SavedAvatarData();
            Debug.Log("Nessun dato salvato trovato");
        }
    }

    private void EnsureLocalModelInList()
    {
        // Evita duplicati
        if (AvatarExists(localModel2AvatarId))
            return;

        string url;

#if UNITY_WEBGL && !UNITY_EDITOR
    // In WebGL StreamingAssets è già un URL (stessa origin del build)
    url = $"{Application.streamingAssetsPath}/{localModel2FileName}";
#else
        // In editor/standalone è un path su disco: serve file://
        var filePath = Path.Combine(Application.streamingAssetsPath, localModel2FileName);
        url = $"file://{filePath}";
#endif

        var info = new Avaturn.Core.Runtime.Scripts.Avatar.Data.AvatarInfo(
            url,
            "glb",
            "local",
            localModel2Gender,
            localModel2AvatarId
        );

        // isWebGL = true solo in WebGL
        bool isWebGL =
#if UNITY_WEBGL && !UNITY_EDITOR
        true;
#else
            false;
#endif

        var meta = new AvatarData(info, isWebGL);

        // Mettilo in cima alla lista, così lo vedi subito
        savedData.avatars.Insert(0, meta);
        SaveData();

        Debug.Log($"[AvatarManager] Aggiunto avatar locale: {localModel2AvatarId} -> {url}");
    }


    bool AvatarExists(string avatarId)
    {
        foreach (var data in savedData.avatars)
            if (data.avatarId == avatarId) return true;
        return false;
    }

    private string _lastAvatarId;
    private float _lastAvatarTs;
    // chiamato da UIManager / bridge
    public void OnAvatarJsonReceived(string json)
    {
        Debug.Log($"JSON ricevuto: {json}");

        try
        {
            var jsonData = JsonUtility.FromJson<AvatarJsonData>(json);

            if (jsonData == null)
                return;

            if (jsonData.status == "closed")
            {
                uiManager?.UpdateDebugText("Avaturn chiuso");
                return;
            }

            if (jsonData.status == "error")
            {
                uiManager?.UpdateDebugText("Errore Avaturn");
                return;
            }

            if (string.IsNullOrEmpty(jsonData.url))
            {
                Debug.LogWarning("[AvatarManager] JSON senza url (ignorato). status=" + jsonData.status);
                return;
            } else {
                if (_lastAvatarId == jsonData.avatarId && Time.realtimeSinceStartup - _lastAvatarTs < 2f)
                    return;

                _lastAvatarId = jsonData.avatarId;
                _lastAvatarTs = Time.realtimeSinceStartup;
            }

            var avatarInfo = new AvatarInfo(
                jsonData.url,
                string.IsNullOrEmpty(jsonData.urlType) ? "glb" : jsonData.urlType,
                string.IsNullOrEmpty(jsonData.bodyId) ? "default" : jsonData.bodyId,
                string.IsNullOrEmpty(jsonData.gender) ? "unknown" : jsonData.gender,
                string.IsNullOrEmpty(jsonData.avatarId) ? Guid.NewGuid().ToString() : jsonData.avatarId
            );

            OnAvatarReceived(avatarInfo);
        }
        catch (Exception e)
        {
            Debug.LogError($"Errore nel parsing JSON: {e.Message}");
            uiManager?.UpdateDebugText("Errore JSON avatar");
        }
    }

    [System.Serializable]
    private class AvatarJsonData
    {
        public string url;
        public string urlType;
        public string bodyId;
        public string gender;
        public string avatarId;
        public string status;
    }
}
