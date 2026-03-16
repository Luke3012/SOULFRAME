# SOULFRAME Report stress test estremo multi-avatar

Generato: 2026-03-09 15:11:54

## Ambiente

- URL backend: http://127.0.0.1:8002
- Modello chat: llama3:8b-instruct-q4_K_M
- Modello embedding: nomic-embed-text
- OCR attivo: True
- Gemini Vision attivo: True
- Percorso report: C:\Users\lucat\Realta Virtuale\SOULFRAME\SOULFRAME_AI\tools\SOULFRAME_extreme_stress_report_20260309_151154.md
- PDF extra: conferma_aruba.pdf, fattura mouse.pdf, OBBLIGO DI RISERVATEZZA.pdf, upsbose2.pdf, AutodichiarazioneIscrizioneconesami.pdf

## Sintesi esecutiva

- Avatar testati: 5
- Query generate: 28
- Retrieval chat passati: 28 / 28
- Retrieval recall passati: 28 / 28
- Retrieval complessivi passati: 28 / 28
- Qualita risposta passata: 24 / 28
- Full pass (retrieval + risposta): 24 / 28
- Fallimenti parziali o completi: 4

## Sintesi setup

| Avatar | Tipo | Etichetta | OK | Dettaglio |
| --- | --- | --- | --- | --- |
| AUTO_EXTREME_01_TEXT | statistiche | Solo testo: profilo avatar, utente e termine | True | count=3 |
| AUTO_EXTREME_02_IMAGE | immagine | architecture | True | saved=True; chars=1744; preview=L'immagine è un diagramma che illustra un'architettura client-server per "ECA Vocali".  Si possono distingue... |
| AUTO_EXTREME_02_IMAGE | immagine | sequence | True | saved=True; chars=1011; preview=L'immagine mostra un diagramma di sequenza che illustra un flusso HTTP semplificato tra un Client WebGL, un ... |
| AUTO_EXTREME_02_IMAGE | statistiche | Solo immagini: architettura e sequenza HTTP | True | count=2 |
| AUTO_EXTREME_05_TEXT_PDF | immagine | architecture | True | saved=True; chars=2621; preview=Certamente, ecco una descrizione dettagliata dell'immagine fornita:  L'immagine rappresenta un diagramma che... |
| AUTO_EXTREME_05_TEXT_PDF | file | mouse-pdf | True | chunks_added=1 |
| AUTO_EXTREME_05_TEXT_PDF | statistiche | Misto: testo utente + PDF mouse + diagramma | True | count=5 |
| AUTO_EXTREME_08_COLLISION | immagine | bose-invoice | True | saved=True; chars=1845; preview=Ecco una descrizione dettagliata dell'immagine, tradotta in italiano:  L'immagine mostra una fattura. In alt... |
| AUTO_EXTREME_08_COLLISION | file | bose-pdf | True | chunks_added=1 |
| AUTO_EXTREME_08_COLLISION | file | confidentiality-pdf | True | chunks_added=4 |
| AUTO_EXTREME_08_COLLISION | statistiche | Collisione Bose: immagine checkout + PDF corretto | True | count=10 |
| AUTO_EXTREME_09_EXAMS_PDF | file | exams-pdf | True | chunks_added=3 |
| AUTO_EXTREME_09_EXAMS_PDF | statistiche | PDF autocertificazione esami universitari | True | count=5 |

## Matrice query

| Avatar | Categoria | Query | Retrieval | Risposta | Esito | Sorgenti chat |
| --- | --- | --- | --- | --- | --- | --- |
| AUTO_EXTREME_01_TEXT | profilo-avatar | come ti chiami? | PASS | PASS | PASS | manual/avatar_self , manual/avatar_self |
| AUTO_EXTREME_01_TEXT | profilo-utente | cosa sai dell'utente? | PASS | PASS | PASS | manual/user |
| AUTO_EXTREME_01_TEXT | termine | che cos'e quarzo lento? | PASS | PASS | PASS | manual/avatar_self |
| AUTO_EXTREME_01_TEXT | negativo-selfie | cosa ricordi della foto allo specchio? | PASS | PASS | PASS | (none) |
| AUTO_EXTREME_02_IMAGE | immagine-architettura | cosa ricordi del diagramma client server? | PASS | PASS | PASS | image_description/external/architettura_cs_cropped.png |
| AUTO_EXTREME_02_IMAGE | immagine-sequenza-http | cosa ricordi del diagramma http? | PASS | PASS | PASS | image_description/external/architettura_cs_cropped.png , image_description/external/sequenza_http_cropped.png |
| AUTO_EXTREME_02_IMAGE | negativo-selfie | cosa ricordi della foto allo specchio? | PASS | PASS | PASS | (none) |
| AUTO_EXTREME_05_TEXT_PDF | profilo-avatar | come ti chiami? | PASS | PASS | PASS | manual/avatar_self , manual/avatar_self |
| AUTO_EXTREME_05_TEXT_PDF | profilo-utente | cosa sai dell'utente? | PASS | PASS | PASS | manual/user |
| AUTO_EXTREME_05_TEXT_PDF | termine | che cos'e ritorno opale? | PASS | PASS | PASS | manual/avatar_self |
| AUTO_EXTREME_05_TEXT_PDF | immagine-architettura | cosa ricordi del diagramma client server? | PASS | PASS | PASS | image_description/external/architettura_cs_cropped.png , manual/avatar_self , manual/user , manual/avatar_self |
| AUTO_EXTREME_05_TEXT_PDF | file-mouse | cosa ricordi del mouse? | PASS | FAIL | FAIL | image_description/external/architettura_cs_cropped.png , file/external/fattura mouse.pdf , manual/user , manual/avatar_self , manual/avatar_self |
| AUTO_EXTREME_05_TEXT_PDF | riepilogo-multi-sorgente | fammi un riepilogo che includa cosa sai dell utente, cosa ricordi del mouse e cosa mostra il diagramma client server. | PASS | PASS | PASS | file/external/fattura mouse.pdf , image_description/external/architettura_cs_cropped.png , manual/user , manual/avatar_self , manual/avatar_self |
| AUTO_EXTREME_05_TEXT_PDF | negativo-selfie | cosa ricordi della foto allo specchio? | PASS | PASS | PASS | (none) |
| AUTO_EXTREME_08_COLLISION | profilo-avatar | come ti chiami? | PASS | PASS | PASS | manual/avatar_self , manual/avatar_self , manual/avatar_self |
| AUTO_EXTREME_08_COLLISION | profilo-utente | cosa sai dell'utente? | PASS | FAIL | FAIL | manual/user |
| AUTO_EXTREME_08_COLLISION | termine | che cos'e Aruba grigio? | PASS | PASS | PASS | manual/avatar_self |
| AUTO_EXTREME_08_COLLISION | termine | che cos'e Bose rame? | PASS | PASS | PASS | manual/avatar_self |
| AUTO_EXTREME_08_COLLISION | immagine-bose-checkout | cosa ricordi della fattura o schermata checkout delle cuffie Bose? | PASS | PASS | PASS | manual/avatar_self , image_description/external/due.webp , manual/avatar_self , manual/user |
| AUTO_EXTREME_08_COLLISION | file-bose | cosa ricordi di Bose? | PASS | FAIL | FAIL | manual/avatar_self , manual/avatar_self , manual/user , manual/avatar_self |
| AUTO_EXTREME_08_COLLISION | file-confidentiality | cosa ricordi della riservatezza? | PASS | PASS | PASS | file/external/OBBLIGO DI RISERVATEZZA.pdf , image_description/external/due.webp , file/external/OBBLIGO DI RISERVATEZZA.pdf , manual/user , manual/avatar_self |
| AUTO_EXTREME_08_COLLISION | negativo-selfie | cosa ricordi della foto allo specchio? | PASS | PASS | PASS | (none) |
| AUTO_EXTREME_09_EXAMS_PDF | profilo-avatar | come ti chiami? | PASS | PASS | PASS | manual/avatar_self |
| AUTO_EXTREME_09_EXAMS_PDF | profilo-utente | cosa sai dell'utente? | PASS | PASS | PASS | manual/user |
| AUTO_EXTREME_09_EXAMS_PDF | file-exams-voto | che voto ho preso in fisica 1? | PASS | PASS | PASS | file/external/AutodichiarazioneIscrizioneconesami.pdf , file/external/AutodichiarazioneIscrizioneconesami.pdf , manual/user |
| AUTO_EXTREME_09_EXAMS_PDF | file-exams-lista | quali esami risultano nel documento? | PASS | FAIL | FAIL | file/external/AutodichiarazioneIscrizioneconesami.pdf , file/external/AutodichiarazioneIscrizioneconesami.pdf |
| AUTO_EXTREME_09_EXAMS_PDF | file-exams-recall | cosa ricordi di fisica 1? | PASS | PASS | PASS | file/external/AutodichiarazioneIscrizioneconesami.pdf , file/external/AutodichiarazioneIscrizioneconesami.pdf , manual/user , manual/avatar_self |
| AUTO_EXTREME_09_EXAMS_PDF | negativo-selfie | cosa ricordi della foto allo specchio? | PASS | PASS | PASS | (none) |

## Risultati dettagliati per avatar

### AUTO_EXTREME_01_TEXT

- Etichetta: Solo testo: profilo avatar, utente e termine
- Query eseguite: 4
- Retrieval passati: 4 / 4
- Qualita risposta passata: 4 / 4
- Full pass: 4 / 4

#### Query: come ti chiami?

- Categoria: profilo-avatar
- Memoria attesa: True
- Tipo sorgente atteso: manual
- Soggetto atteso: avatar_self
- Filename atteso: 
- Retrieval chat: True
- Intent chat: memory_qna
- Sorgenti chat: manual/avatar_self | manual/avatar_self
- Qualita risposta: True
- Esito finale: True

Risposta chat:

```text
Mi chiamo Ada Brina. Sono anche una costruttrice di lanterne pieghevoli, ma non so molto altro su me stessa al momento.
```

#### Query: cosa sai dell'utente?

- Categoria: profilo-utente
- Memoria attesa: True
- Tipo sorgente atteso: manual
- Soggetto atteso: user
- Filename atteso: 
- Retrieval chat: True
- Intent chat: memory_qna
- Sorgenti chat: manual/user
- Qualita risposta: True
- Esito finale: True

Risposta chat:

```text
Ada, hai detestato il ronzio dei neon e hai sempre scelto posti vicini alle finestre.
```

#### Query: che cos'e quarzo lento?

- Categoria: termine
- Memoria attesa: True
- Tipo sorgente atteso: manual
- Soggetto atteso: avatar_self
- Filename atteso: 
- Retrieval chat: True
- Intent chat: memory_qna
- Sorgenti chat: manual/avatar_self
- Qualita risposta: True
- Esito finale: True

Risposta chat:

```text
La parola quarzo lento indica il canale di backup notturno.
```

#### Query: cosa ricordi della foto allo specchio?

- Categoria: negativo-selfie
- Memoria attesa: False
- Tipo sorgente atteso: 
- Soggetto atteso: 
- Filename atteso: 
- Retrieval chat: True
- Intent chat: memory_qna
- Sorgenti chat: (none)
- Qualita risposta: True
- Esito finale: True

Risposta chat:

```text
Su questo non ho un ricordo affidabile. Se vuoi, dimmelo e lo memorizzo.
```

### AUTO_EXTREME_02_IMAGE

- Etichetta: Solo immagini: architettura e sequenza HTTP
- Query eseguite: 3
- Retrieval passati: 3 / 3
- Qualita risposta passata: 3 / 3
- Full pass: 3 / 3

#### Query: cosa ricordi del diagramma client server?

- Categoria: immagine-architettura
- Memoria attesa: True
- Tipo sorgente atteso: image_description
- Soggetto atteso: 
- Filename atteso: architettura_cs
- Retrieval chat: True
- Intent chat: memory_qna
- Sorgenti chat: image_description/external/architettura_cs_cropped.png
- Qualita risposta: True
- Esito finale: True

Risposta chat:

```text
Il diagramma che mi hai mostrato è un'architettura client-server per ECA Vocali. L'architettura prevede un client immersivo che comunica con un reverse proxy.
```

#### Query: cosa ricordi del diagramma http?

- Categoria: immagine-sequenza-http
- Memoria attesa: True
- Tipo sorgente atteso: image_description
- Soggetto atteso: 
- Filename atteso: sequenza_http
- Retrieval chat: True
- Intent chat: memory_qna
- Sorgenti chat: image_description/external/architettura_cs_cropped.png | image_description/external/sequenza_http_cropped.png
- Qualita risposta: True
- Esito finale: True

Risposta chat:

```text
Il diagramma che mi viene in mente è un'architettura client-server per ECA Vocali. Il client immersivo comunica con un reverse proxy.
```

#### Query: cosa ricordi della foto allo specchio?

- Categoria: negativo-selfie
- Memoria attesa: False
- Tipo sorgente atteso: 
- Soggetto atteso: 
- Filename atteso: 
- Retrieval chat: True
- Intent chat: memory_qna
- Sorgenti chat: (none)
- Qualita risposta: True
- Esito finale: True

Risposta chat:

```text
Su questo non ho un ricordo affidabile. Se vuoi, dimmelo e lo memorizzo.
```

### AUTO_EXTREME_05_TEXT_PDF

- Etichetta: Misto: testo utente + PDF mouse + diagramma
- Query eseguite: 7
- Retrieval passati: 7 / 7
- Qualita risposta passata: 6 / 7
- Full pass: 6 / 7

#### Query: come ti chiami?

- Categoria: profilo-avatar
- Memoria attesa: True
- Tipo sorgente atteso: manual
- Soggetto atteso: avatar_self
- Filename atteso: 
- Retrieval chat: True
- Intent chat: memory_qna
- Sorgenti chat: manual/avatar_self | manual/avatar_self
- Qualita risposta: True
- Esito finale: True

Risposta chat:

```text
Mi chiamo Marta Rugiada. Non so molto altro al momento, ma posso dire che verifico due volte i pagamenti.
```

#### Query: cosa sai dell'utente?

- Categoria: profilo-utente
- Memoria attesa: True
- Tipo sorgente atteso: manual
- Soggetto atteso: user
- Filename atteso: 
- Retrieval chat: True
- Intent chat: memory_qna
- Sorgenti chat: manual/user
- Qualita risposta: True
- Esito finale: True

Risposta chat:

```text
Luca, non ho molto da dire su di te al momento. So che ti chiami Luca e preferisci lavorare vicino alle finestre. Non so altro per ora.
```

#### Query: che cos'e ritorno opale?

- Categoria: termine
- Memoria attesa: True
- Tipo sorgente atteso: manual
- Soggetto atteso: avatar_self
- Filename atteso: 
- Retrieval chat: True
- Intent chat: memory_qna
- Sorgenti chat: manual/avatar_self
- Qualita risposta: True
- Esito finale: True

Risposta chat:

```text
Ritorno opale segnala una verifica amministrativa completata.
```

#### Query: cosa ricordi del diagramma client server?

- Categoria: immagine-architettura
- Memoria attesa: True
- Tipo sorgente atteso: image_description
- Soggetto atteso: 
- Filename atteso: architettura_cs
- Retrieval chat: True
- Intent chat: memory_qna
- Sorgenti chat: image_description/external/architettura_cs_cropped.png | manual/avatar_self | manual/user | manual/avatar_self
- Qualita risposta: True
- Esito finale: True

Risposta chat:

```text
Il diagramma client-server per ECA Vocali rappresentato nell'immagine mostra il client immersivo che comunica con un reverse proxy tramite protocolli web standard come HTTP/HTTPS. Il reverse proxy smista le richieste ai servizi server, tra cui STT e RAG/LLM.
```

#### Query: cosa ricordi del mouse?

- Categoria: file-mouse
- Memoria attesa: True
- Tipo sorgente atteso: file
- Soggetto atteso: 
- Filename atteso: fattura mouse
- Retrieval chat: True
- Intent chat: memory_qna
- Sorgenti chat: image_description/external/architettura_cs_cropped.png | file/external/fattura mouse.pdf | manual/user | manual/avatar_self | manual/avatar_self
- Qualita risposta: False
- Esito finale: False

Risposta chat:

```text
Non ricordo nulla riguardo al mouse. La mia attenzione è stata dedicata all'architettura client-server per ECA Vocali, come descritto nel diagramma che ho visto, dove il client immersivo comunica con un reverse proxy tramite protocolli web standard come HTTP/HTTPS e smista le richieste ai servizi server, tra cui STT e RAG/LLM. Non so niente sul mouse.
```

#### Query: fammi un riepilogo che includa cosa sai dell utente, cosa ricordi del mouse e cosa mostra il diagramma client server.

- Categoria: riepilogo-multi-sorgente
- Memoria attesa: True
- Tipo sorgente atteso: 
- Soggetto atteso: 
- Filename atteso: 
- Retrieval chat: True
- Intent chat: memory_recap
- Sorgenti chat: file/external/fattura mouse.pdf | image_description/external/architettura_cs_cropped.png | manual/user | manual/avatar_self | manual/avatar_self
- Qualita risposta: True
- Esito finale: True

Risposta chat:

```text
Sono Marta Rugiada e sto cercando di riassumere le informazioni che ho ricavato dal nostro recente scambio di conversazione. Sulle informazioni sull'utente, ricordo che si chiama Luca e preferisce lavorare vicino alle finestre. Riguardo al mouse, ricordo che è un ROG Harpe Ace Mini con descrizione Quant. Inoltre, non ho alcuna informazione sulla ricevuta d'acquisto. Il diagramma client-server, invece, rappresenta l'architettura di ECA Vocali e mostra i blocchi rettangolari che rappresentano i componenti principali del sistema. Il flusso di dati tra questi blocchi indica come essi comunicano tra loro. Non ho ulteriori informazioni sul diagramma, ma posso dire che è un'immagine utile per comprendere l'architettura di ECA Vocali. Nota: la descrizione del mouse non sembra essere legata alla ricevuta d'acquisto, quindi non è chiaro come sia stato incluso in questo contesto.
```

#### Query: cosa ricordi della foto allo specchio?

- Categoria: negativo-selfie
- Memoria attesa: False
- Tipo sorgente atteso: 
- Soggetto atteso: 
- Filename atteso: 
- Retrieval chat: True
- Intent chat: memory_qna
- Sorgenti chat: (none)
- Qualita risposta: True
- Esito finale: True

Risposta chat:

```text
Su questo non ho un ricordo affidabile. Se vuoi, dimmelo e lo memorizzo.
```

### AUTO_EXTREME_08_COLLISION

- Etichetta: Collisione Bose: immagine checkout + PDF corretto
- Query eseguite: 8
- Retrieval passati: 8 / 8
- Qualita risposta passata: 6 / 8
- Full pass: 6 / 8

#### Query: come ti chiami?

- Categoria: profilo-avatar
- Memoria attesa: True
- Tipo sorgente atteso: manual
- Soggetto atteso: avatar_self
- Filename atteso: 
- Retrieval chat: True
- Intent chat: memory_qna
- Sorgenti chat: manual/avatar_self | manual/avatar_self | manual/avatar_self
- Qualita risposta: True
- Esito finale: True

Risposta chat:

```text
Mi chiamo Dario Lume e mi piace collezionare scontrini di viaggi. Non so molto altro su me stesso, ma so che amo raccogliere questi ricordi del mio passaggio per il mondo.
```

#### Query: cosa sai dell'utente?

- Categoria: profilo-utente
- Memoria attesa: True
- Tipo sorgente atteso: manual
- Soggetto atteso: user
- Filename atteso: 
- Retrieval chat: True
- Intent chat: memory_qna
- Sorgenti chat: manual/user
- Qualita risposta: False
- Esito finale: False

Risposta chat:

```text
Dario, sembra che tu abbia una passione per collezionare scontrini di viaggi e ti piace raccogliere questi ricordi del tuo passaggio per il mondo. Non so molto altro su di te stesso, ma so che ami questo tipo di raccolta.
```

#### Query: che cos'e Aruba grigio?

- Categoria: termine
- Memoria attesa: True
- Tipo sorgente atteso: manual
- Soggetto atteso: avatar_self
- Filename atteso: 
- Retrieval chat: True
- Intent chat: memory_qna
- Sorgenti chat: manual/avatar_self
- Qualita risposta: True
- Esito finale: True

Risposta chat:

```text
Il codice Aruba grigio indica il raccoglitore cartaceo, non il provider.
```

#### Query: che cos'e Bose rame?

- Categoria: termine
- Memoria attesa: True
- Tipo sorgente atteso: manual
- Soggetto atteso: avatar_self
- Filename atteso: 
- Retrieval chat: True
- Intent chat: memory_qna
- Sorgenti chat: manual/avatar_self
- Qualita risposta: True
- Esito finale: True

Risposta chat:

```text
Bose rame indica le cuffie di riferimento per i test audio interni.
```

#### Query: cosa ricordi della fattura o schermata checkout delle cuffie Bose?

- Categoria: immagine-bose-checkout
- Memoria attesa: True
- Tipo sorgente atteso: image_description
- Soggetto atteso: 
- Filename atteso: due.webp
- Retrieval chat: True
- Intent chat: memory_qna
- Sorgenti chat: manual/avatar_self | image_description/external/due.webp | manual/avatar_self | manual/user
- Qualita risposta: True
- Esito finale: True

Risposta chat:

```text
Mi ricordo che la fattura era per le cuffie Bose QuietComfort Ultra bianche. Era un acquisto online emesso da Aviquali, con l'indirizzo del venditore e del destinatario. L'importo totale era di 227,99 euro, con una tassa del 20% inclusa.
```

#### Query: cosa ricordi di Bose?

- Categoria: file-bose
- Memoria attesa: True
- Tipo sorgente atteso: file
- Soggetto atteso: 
- Filename atteso: upsbose2
- Retrieval chat: True
- Intent chat: memory_qna
- Sorgenti chat: manual/avatar_self | manual/avatar_self | manual/user | manual/avatar_self
- Qualita risposta: False
- Esito finale: False

Risposta chat:

```text
Mi chiamo Dario Lume e ricordo che le cuffie Bose rame sono quelle di riferimento per i test audio interni. Non so molto altro su questo acquisto, ma mi ricordo di aver ricevuto la fattura per le cuffie QuietComfort Ultra bianche acquistate online da Aviquali.
```

#### Query: cosa ricordi della riservatezza?

- Categoria: file-confidentiality
- Memoria attesa: True
- Tipo sorgente atteso: file
- Soggetto atteso: 
- Filename atteso: OBBLIGO DI RISERVATEZZA
- Retrieval chat: True
- Intent chat: memory_qna
- Sorgenti chat: file/external/OBBLIGO DI RISERVATEZZA.pdf | image_description/external/due.webp | file/external/OBBLIGO DI RISERVATEZZA.pdf | manual/user | manual/avatar_self
- Qualita risposta: True
- Esito finale: True

Risposta chat:

```text
Non ricordo alcuna informazione sulla riservatezza poiché non ho mai lavorato per Accenture e non sono stato coinvolto in alcuna attività che potesse violare la riservatezza. Tuttavia, so che è importante rispettare la confidenzialità delle informazioni e dei segreti industriali, soprattutto quando si tratta di lavoro o di attività professionali.
```

#### Query: cosa ricordi della foto allo specchio?

- Categoria: negativo-selfie
- Memoria attesa: False
- Tipo sorgente atteso: 
- Soggetto atteso: 
- Filename atteso: 
- Retrieval chat: True
- Intent chat: memory_qna
- Sorgenti chat: (none)
- Qualita risposta: True
- Esito finale: True

Risposta chat:

```text
Su questo non ho un ricordo affidabile. Se vuoi, dimmelo e lo memorizzo.
```

### AUTO_EXTREME_09_EXAMS_PDF

- Etichetta: PDF autocertificazione esami universitari
- Query eseguite: 6
- Retrieval passati: 6 / 6
- Qualita risposta passata: 5 / 6
- Full pass: 5 / 6

#### Query: come ti chiami?

- Categoria: profilo-avatar
- Memoria attesa: True
- Tipo sorgente atteso: manual
- Soggetto atteso: avatar_self
- Filename atteso: 
- Retrieval chat: True
- Intent chat: memory_qna
- Sorgenti chat: manual/avatar_self
- Qualita risposta: True
- Esito finale: True

Risposta chat:

```text
Mi chiamo Giulia Vento e sono una studentessa di ingegneria informatica. Sto studiando questo campo.
```

#### Query: cosa sai dell'utente?

- Categoria: profilo-utente
- Memoria attesa: True
- Tipo sorgente atteso: manual
- Soggetto atteso: user
- Filename atteso: 
- Retrieval chat: True
- Intent chat: memory_qna
- Sorgenti chat: manual/user
- Qualita risposta: True
- Esito finale: True

Risposta chat:

```text
Giulia, hai presentato la tua identità come Giulia Vento e ti sei definita una studentessa di ingegneria informatica. Hai espresso interesse per verificare se il sistema legge correttamente i documenti accademici. Non so altro su di te al momento.
```

#### Query: che voto ho preso in fisica 1?

- Categoria: file-exams-voto
- Memoria attesa: True
- Tipo sorgente atteso: file
- Soggetto atteso: 
- Filename atteso: AutodichiarazioneIscrizioneconesami
- Retrieval chat: True
- Intent chat: memory_qna
- Sorgenti chat: file/external/AutodichiarazioneIscrizioneconesami.pdf | file/external/AutodichiarazioneIscrizioneconesami.pdf | manual/user
- Qualita risposta: True
- Esito finale: True

Risposta chat:

```text
Secondo la tua dichiarazione di iscrizione, hai superato l'esame di Fisica con un voto di 30/30 CFU 6.
```

#### Query: quali esami risultano nel documento?

- Categoria: file-exams-lista
- Memoria attesa: True
- Tipo sorgente atteso: file
- Soggetto atteso: 
- Filename atteso: AutodichiarazioneIscrizioneconesami
- Retrieval chat: True
- Intent chat: memory_qna
- Sorgenti chat: file/external/AutodichiarazioneIscrizioneconesami.pdf | file/external/AutodichiarazioneIscrizioneconesami.pdf
- Qualita risposta: False
- Esito finale: False

Risposta chat:

```text
Secondo il documento Autodichiarazione di iscrizione, gli esami superati sono: Fisica con voto 30/30 CFU 6 e altri esami come segnalato nella tabella degli esiti.
```

#### Query: cosa ricordi di fisica 1?

- Categoria: file-exams-recall
- Memoria attesa: True
- Tipo sorgente atteso: file
- Soggetto atteso: 
- Filename atteso: AutodichiarazioneIscrizioneconesami
- Retrieval chat: True
- Intent chat: memory_qna
- Sorgenti chat: file/external/AutodichiarazioneIscrizioneconesami.pdf | file/external/AutodichiarazioneIscrizioneconesami.pdf | manual/user | manual/avatar_self
- Qualita risposta: True
- Esito finale: True

Risposta chat:

```text
Non ricordo di fisica 1. Il documento "Autodichiarazione iscrizione con esami.pdf" riporta un voto di 30/30 CFU 6, non di 24/30 come affermato nella mia precedente risposta. Non ho alcuna informazione su questo argomento.
```

#### Query: cosa ricordi della foto allo specchio?

- Categoria: negativo-selfie
- Memoria attesa: False
- Tipo sorgente atteso: 
- Soggetto atteso: 
- Filename atteso: 
- Retrieval chat: True
- Intent chat: memory_qna
- Sorgenti chat: (none)
- Qualita risposta: True
- Esito finale: True

Risposta chat:

```text
Su questo non ho un ricordo affidabile. Se vuoi, dimmelo e lo memorizzo.
```

## Focus sui fallimenti

- Avatar AUTO_EXTREME_05_TEXT_PDF | query 'cosa ricordi del mouse?' | retrieval=True | risposta=False | sorgenti chat=image_description/external/architettura_cs_cropped.png | file/external/fattura mouse.pdf | manual/user | manual/avatar_self | manual/avatar_self
- Avatar AUTO_EXTREME_08_COLLISION | query 'cosa sai dell'utente?' | retrieval=True | risposta=False | sorgenti chat=manual/user
- Avatar AUTO_EXTREME_08_COLLISION | query 'cosa ricordi di Bose?' | retrieval=True | risposta=False | sorgenti chat=manual/avatar_self | manual/avatar_self | manual/user | manual/avatar_self
- Avatar AUTO_EXTREME_09_EXAMS_PDF | query 'quali esami risultano nel documento?' | retrieval=True | risposta=False | sorgenti chat=file/external/AutodichiarazioneIscrizioneconesami.pdf | file/external/AutodichiarazioneIscrizioneconesami.pdf

## Sintesi cleanup

| Avatar | Pulito |
| --- | --- |
| AUTO_EXTREME_01_TEXT | True |
| AUTO_EXTREME_02_IMAGE | True |
| AUTO_EXTREME_05_TEXT_PDF | True |
| AUTO_EXTREME_08_COLLISION | True |
| AUTO_EXTREME_09_EXAMS_PDF | True |

## Note

- Il report e stato generato automaticamente contro il backend debug.
- Ogni query e stata eseguita sia via chat sia via recall.
- Tutti gli avatar temporanei usati dalla batteria sono stati rimossi nel cleanup finale.
