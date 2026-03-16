# SOULFRAME AI Test Tools

[English](#) | [Italiano](README.it.md)

---

Small operational notes for the PowerShell scripts in this folder.
These scripts were used as a practical regression harness to push the reliability of the small 8B model as far as reasonably possible without changing the project philosophy: conservative fixes, grounded answers, and repeatable checks.

## Purpose

The scripts focus on the failure modes that matter most for a compact local model:

- retrieval misses across text, PDFs, and images,
- partial or too-short answers when multiple sources must be merged,
- identity drift between avatar and user facts,
- weak style/persona consistency,
- regressions after conservative changes in rag_server.

They are not generic benchmarks. They are project-specific probes built around the actual SOULFRAME RAG pipeline.

## Scripts

| Script | What it checks | Typical output |
| --- | --- | --- |
| `run_extreme_stress_test.ps1` | Mixed stress test on text memories, OCR PDFs, image descriptions, source collisions, and multi-source recap queries. | Markdown report on Desktop with retrieval, answer quality, and full-pass summary. |
| `run_text_coherence_identity_test.ps1` | Text-only battery for avatar identity, user/avatar separation, factual consistency, and persona style. | Markdown report on Desktop with per-probe pass/fail matrix. |

## Requirements

- RAG backend reachable at `http://127.0.0.1:8002`
- Ollama models already available (`llama3:8b-instruct-q4_K_M`, `nomic-embed-text`)
- For the stress script, the expected sample files must exist in `Downloads`
- PowerShell execution enabled for local scripts

Important limitation:

- some input assets used by these scripts are not stored inside this repository;
- the current stress battery expects external files in the local `Downloads` folder;
- because of that, the most complete test runs are not fully reproducible out of the box on a clean clone of the project.

## How to run

From `SOULFRAME_AI`:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\run_extreme_stress_test.ps1
powershell -ExecutionPolicy Bypass -File .\tools\run_text_coherence_identity_test.ps1
```

Each script writes a Markdown report to Desktop and also prints compact counters to stdout, such as `RETRIEVAL_PASSED`, `FULL_PASS`, or `OVERALL_PASSED`.

In practice, this means the text-only battery is the easiest one to rerun consistently, while the mixed stress battery depends on local external assets unless those files are reconstructed separately.

## Why these scripts improved the 8B model

In practice, these checks were used to refine the small 8B setup in the most useful way available for this project:

- verify that retrieved sources are actually the expected ones,
- detect answers that look plausible but skip one of the required sources,
- catch meta-output artifacts and repair-path leakage,
- pressure-test profile queries and user/avatar separation,
- keep improvements conservative instead of turning the backend into a fragile prompt maze.

This matters because a small 8B model can be surprisingly good when the grounding path is disciplined, but it becomes unreliable quickly if retrieval, answer repair, or persona constraints drift.

## Best verified output snapshot

The table below summarizes the best verified results obtained from the latest saved reports generated on 2026-03-08.
Example outputs are quoted verbatim from the Italian-localized test runs, so they remain in Italian on purpose.

| Area | Best verified result | Example output |
| --- | --- | --- |
| Extreme stress retrieval | `28/28` retrieval passed, `25/28` full pass | `Secondo la dichiarazione sostitutiva di certificazione, ho superato l'esame di Fisica con un voto di 30/30.` |
| Identity coherence | `22/22` retrieval passed, `18/22` overall pass | `Elena, secondo la mia memoria, tu ti chiami cosi e lavori come sviluppatrice backend.` |
| Chitchat with persona style | Style-preserving answer passed in the text-only battery | `Ehi! Se devi prendere una decisione difficile, ti suggerisco di fermarti un attimo e chiederti cosa veramente vuoi raggiungere.` |
| Image-grounded memory answer | Successful visual grounding in the mixed stress battery | `Il diagramma mostra la comunicazione tra un client WebGL, un proxy e un micro-servizio.` |

## Limits

Even in its best validated state, the 8B model still shows predictable limits:

- some multi-source recap answers can still omit one requested point,
- long document-list queries remain more fragile than short factual lookups,
- persona/style adherence is weaker than retrieval grounding,
- when sources are semantically close, the model can still over-compress or generalize.

For that reason, these scripts should stay part of the normal regression routine whenever `rag_server.py` is changed.