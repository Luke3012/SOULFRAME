> [English](README.md) | **Italiano**

# SOULFRAME AI Test Tools

Piccole note operative per gli script PowerShell di questa cartella.
Questi script sono stati usati come batteria di regressione pratica per portare l'affidabilità del modellino 8B il più vicino possibile al suo limite utile, senza cambiare filosofia di progetto: fix conservativi, risposte ancorate alle fonti e controlli ripetibili.

## Scopo

Gli script colpiscono i punti deboli che contano davvero per un modello locale compatto:

- retrieval mancati tra testo, PDF e immagini,
- risposte parziali o troppo corte quando bisogna unire più sorgenti,
- derive identitarie tra fatti dell'avatar e fatti dell'utente,
- coerenza debole di stile e persona,
- regressioni dopo modifiche conservative in rag_server.

Non sono benchmark generici. Sono sonde specifiche per la pipeline RAG reale di SOULFRAME.

## Script

| Script | Cosa controlla | Output tipico |
| --- | --- | --- |
| `run_extreme_stress_test.ps1` | Stress test misto su memorie testuali, PDF con OCR, descrizioni immagine, collisioni di sorgente e query di riepilogo multi-sorgente. | Report Markdown sul Desktop con sintesi retrieval, qualità risposta e full-pass. |
| `run_text_coherence_identity_test.ps1` | Batteria solo testuale per identità avatar, separazione utente/avatar, correttezza fattuale e coerenza di persona/stile. | Report Markdown sul Desktop con matrice pass/fail per ogni probe. |

## Requisiti

- backend RAG raggiungibile su `http://127.0.0.1:8002`
- modelli Ollama già presenti (`llama3:8b-instruct-q4_K_M`, `nomic-embed-text`)
- per lo script stress, file campione attesi dentro `Downloads`
- esecuzione script PowerShell consentita in locale

Limite importante:

- alcuni asset di input usati da questi script non sono salvati dentro questo repository;
- la batteria stress attuale si aspetta file esterni nella cartella locale `Downloads`;
- per questo motivo i test più completi non sono pienamente replicabili out-of-the-box su una clone pulita del progetto.

## Come si lanciano

Da `SOULFRAME_AI`:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\run_extreme_stress_test.ps1
powershell -ExecutionPolicy Bypass -File .\tools\run_text_coherence_identity_test.ps1
```

Ogni script salva un report Markdown sul Desktop e stampa anche contatori sintetici su stdout, per esempio `RETRIEVAL_PASSED`, `FULL_PASS` oppure `OVERALL_PASSED`.

In pratica, la batteria solo testuale è quella più semplice da rilanciare in modo coerente, mentre la batteria stress mista dipende da asset locali esterni finché quei file non vengono ricostruiti separatamente.

## Perché questi script hanno migliorato il piccolo 8B

Nel concreto, questi controlli sono serviti per migliorare il setup 8B nel modo più utile per questo progetto:

- verificare che le sorgenti recuperate siano davvero quelle attese,
- intercettare risposte plausibili ma incomplete quando serve coprire più fonti,
- trovare artefatti di meta-output e perdite del repair path,
- mettere sotto pressione query profilo e separazione utente/avatar,
- mantenere i fix conservativi invece di trasformare il backend in un intreccio fragile di prompt.

Questo conta perché un 8B piccolo può comportarsi molto bene quando il grounding è disciplinato, ma diventa fragile in fretta se retrieval, repair della risposta o vincoli di persona slittano.

## Miglior snapshot di output verificato

La tabella sotto riassume i risultati migliori verificati dagli ultimi report salvati il 2026-03-08.

| Area | Miglior risultato verificato | Esempio di output |
| --- | --- | --- |
| Stress test estremo | `28/28` retrieval passati, `25/28` full pass | `Secondo la dichiarazione sostitutiva di certificazione, ho superato l'esame di Fisica con un voto di 30/30.` |
| Coerenza identitaria | `22/22` retrieval passati, `18/22` overall pass | `Elena, secondo la mia memoria, tu ti chiami così e lavori come sviluppatrice backend.` |
| Chitchat con persona | Risposta con stile coerente passata nella batteria testuale | `Ehi! Se devi prendere una decisione difficile, ti suggerisco di fermarti un attimo e chiederti cosa veramente vuoi raggiungere.` |
| Memoria ancorata a immagine | Grounding visivo riuscito nella batteria mista | `Il diagramma mostra la comunicazione tra un client WebGL, un proxy e un micro-servizio.` |

## Limiti

Anche nel suo stato migliore validato, il modello 8B mostra ancora limiti prevedibili:

- alcuni riepiloghi multi-sorgente possono ancora saltare un punto richiesto,
- le query lunghe che chiedono elenchi da documenti restano più fragili dei lookup fattuali brevi,
- l'aderenza a stile/persona e meno robusta del grounding retrieval,
- quando le fonti sono semanticamente vicine, il modello può ancora comprimere troppo o generalizzare.

Per questo conviene tenere questi script dentro la routine normale di regressione ogni volta che cambia `rag_server.py`.