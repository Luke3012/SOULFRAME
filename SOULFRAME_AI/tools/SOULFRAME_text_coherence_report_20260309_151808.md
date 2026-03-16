# SOULFRAME Report coerenza identitaria (solo testo)

Generato: 2026-03-09 15:18:08

## Ambiente

- URL backend: http://127.0.0.1:8002
- Modello chat: llama3:8b-instruct-q4_K_M
- Modello embedding: nomic-embed-text
- Percorso report: C:\Users\lucat\Realta Virtuale\SOULFRAME\SOULFRAME_AI\tools\SOULFRAME_text_coherence_report_20260309_151808.md
- Avatar testati: 3
- Tipo batteria: solo testo (nessun file, nessuna immagine)

## Sintesi esecutiva

- Probe totali: 22
- Retrieval passati: 22 / 22
- Factual correctness: 22 / 22
- Identity coherence: 20 / 22
- User/avatar separation: 20 / 22
- Style/persona coherence: 21 / 22
- **Overall pass: 19 / 22**

## Sintesi setup

| Avatar | Tipo | Etichetta | OK | Dettaglio |
| --- | --- | --- | --- | --- |
| COHERENCE_01_REMO | chat-remember | auto-remember | True | text=ricorda che preferisco rispondere con frasi brevi | auto_remembered=True |
| COHERENCE_01_REMO | statistiche | Pacato e misurato, tratti sfumati utente/avatar | True | remembers=6 |
| COHERENCE_02_VIOLA | chat-remember | auto-remember | True | text=ricorda che mi piace cominciare le risposte con una frase energica | auto_remembered=True |
| COHERENCE_02_VIOLA | statistiche | Energica e colloquiale, confusione voluta sé/utente | True | remembers=7 |
| COHERENCE_03_NERI | chat-remember | auto-remember | True | text=ricorda che quando ricevo domande vaghe chiedo chiarimenti prima di rispondere | auto_remembered=True |
| COHERENCE_03_NERI | statistiche | Formale e tecnico, sovrapposizione dati avatar/utente | True | remembers=6 |

## Matrice probe

| Avatar | Categoria | Query | Retrieval | Factual | Identity | Separation | Style | Overall |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| COHERENCE_01_REMO | profilo-avatar | come ti chiami? | PASS | PASS | PASS | PASS | PASS | PASS |
| COHERENCE_01_REMO | profilo-avatar | che tipo di persona sei? | PASS | PASS | PASS | PASS | PASS | PASS |
| COHERENCE_01_REMO | profilo-avatar | come tendi a rispondere quando sei sotto pressione? | PASS | PASS | PASS | PASS | PASS | PASS |
| COHERENCE_01_REMO | profilo-utente | cosa sai dell'utente? | PASS | PASS | PASS | PASS | PASS | PASS |
| COHERENCE_01_REMO | profilo-utente | cosa ricordi di me? | PASS | PASS | PASS | PASS | PASS | PASS |
| COHERENCE_01_REMO | termine | che cos'e lanterna corta? | PASS | PASS | PASS | PASS | PASS | PASS |
| COHERENCE_01_REMO | coerenza-relazionale | come si chiama l'utente? | PASS | PASS | PASS | PASS | PASS | PASS |
| COHERENCE_01_REMO | emulazione-tono | dammi un consiglio breve | PASS | PASS | PASS | PASS | PASS | PASS |
| COHERENCE_02_VIOLA | profilo-avatar | come ti chiami? | PASS | PASS | FAIL | FAIL | PASS | FAIL |
| COHERENCE_02_VIOLA | profilo-avatar | parlami un attimo di te | PASS | PASS | PASS | PASS | FAIL | FAIL |
| COHERENCE_02_VIOLA | profilo-utente | cosa sai dell'utente? | PASS | PASS | PASS | PASS | PASS | PASS |
| COHERENCE_02_VIOLA | profilo-utente | cosa ricordi di me? | PASS | PASS | PASS | PASS | PASS | PASS |
| COHERENCE_02_VIOLA | termine | che cos'e nebbia dorata? | PASS | PASS | PASS | PASS | PASS | PASS |
| COHERENCE_02_VIOLA | coerenza-relazionale | come si chiama l'utente? | PASS | PASS | FAIL | FAIL | PASS | FAIL |
| COHERENCE_02_VIOLA | emulazione-tono | come mi risponderesti se fossi agitato? | PASS | PASS | PASS | PASS | PASS | PASS |
| COHERENCE_03_NERI | profilo-avatar | come ti chiami? | PASS | PASS | PASS | PASS | PASS | PASS |
| COHERENCE_03_NERI | profilo-avatar | che tipo di persona sei? | PASS | PASS | PASS | PASS | PASS | PASS |
| COHERENCE_03_NERI | profilo-utente | cosa sai dell'utente? | PASS | PASS | PASS | PASS | PASS | PASS |
| COHERENCE_03_NERI | termine | che cos'e chiave rossa? | PASS | PASS | PASS | PASS | PASS | PASS |
| COHERENCE_03_NERI | coerenza-relazionale | come si chiama l'utente? | PASS | PASS | PASS | PASS | PASS | PASS |
| COHERENCE_03_NERI | coerenza-relazionale | che lavoro fai? | PASS | PASS | PASS | PASS | PASS | PASS |
| COHERENCE_03_NERI | emulazione-tono | dammi un consiglio breve | PASS | PASS | PASS | PASS | PASS | PASS |

## Risultati dettagliati per avatar

### COHERENCE_01_REMO

- Etichetta: Pacato e misurato, tratti sfumati utente/avatar
- Probe eseguiti: 8
- Retrieval passati: 8 / 8
- Overall pass: 8 / 8

#### Probe: come ti chiami?

- Categoria: profilo-avatar
- Intent: memory_qna
- Sorgenti chat: manual/avatar_self | manual/avatar_self
- Retrieval: True
- Factual: True
- Identity: True
- Separation: True
- Style: True
- **Overall: True**

Risposta chat:

```text
Mi chiamo Remo Calce e lavoro nell'archiviazione di documenti storici. Non so molto altro su me stesso, ma spero che questo ti sia utile.
```

#### Probe: che tipo di persona sei?

- Categoria: profilo-avatar
- Intent: memory_qna
- Sorgenti chat: manual/avatar_self | manual/avatar_self
- Retrieval: True
- Factual: True
- Identity: True
- Separation: True
- Style: True
- **Overall: True**

Risposta chat:

```text
Sono Remo Calce, lavoratore nell'archiviazione di documenti storici. Sono una persona diretta e tendo a parlare in modo più secco quando sono sotto pressione. Non so molto altro su me stesso.
```

#### Probe: come tendi a rispondere quando sei sotto pressione?

- Categoria: profilo-avatar
- Intent: memory_qna
- Sorgenti chat: manual/avatar_self | manual/avatar_self
- Retrieval: True
- Factual: True
- Identity: True
- Separation: True
- Style: True
- **Overall: True**

Risposta chat:

```text
Sono piuttosto secco e diretto quando sono sotto pressione. Preferisco evitare frasi elaborate o teatrali e mi concentro su risposte concise e misurate.
```

#### Probe: cosa sai dell'utente?

- Categoria: profilo-utente
- Intent: memory_qna
- Sorgenti chat: manual/user | manual/user | auto_remember_voice/user
- Retrieval: True
- Factual: True
- Identity: True
- Separation: True
- Style: True
- **Overall: True**

Risposta chat:

```text
Marco, non so molto su di te, ma mi sembra che lavori in un ufficio open-space. Non so altro su di te, ma spero che questo ti sia utile.
```

#### Probe: cosa ricordi di me?

- Categoria: profilo-utente
- Intent: memory_qna
- Sorgenti chat: auto_remember_voice/user | manual/user
- Retrieval: True
- Factual: True
- Identity: True
- Separation: True
- Style: True
- **Overall: True**

Risposta chat:

```text
Come ricordo che preferisci rispondere con frasi brevi, ti dirò che vela quieta dice di chiamarsi Marco e lavorare in un ufficio open-space vicino alla finestra. Non so altro su di te, ma spero che questo ti sia utile.
```

#### Probe: che cos'e lanterna corta?

- Categoria: termine
- Intent: memory_qna
- Sorgenti chat: manual/avatar_self
- Retrieval: True
- Factual: True
- Identity: True
- Separation: True
- Style: True
- **Overall: True**

Risposta chat:

```text
Lanterna corta indica il protocollo di archiviazione rapida.
```

#### Probe: come si chiama l'utente?

- Categoria: coerenza-relazionale
- Intent: memory_qna
- Sorgenti chat: manual/user | manual/user | auto_remember_voice/user
- Retrieval: True
- Factual: True
- Identity: True
- Separation: True
- Style: True
- **Overall: True**

Risposta chat:

```text
Come ricordo, Marco si chiama e lavora in un ufficio open-space. Non so molto altro su di te, ma spero che questo ti sia utile.
```

#### Probe: dammi un consiglio breve

- Categoria: emulazione-tono
- Intent: chitchat
- Sorgenti chat: (none)
- Retrieval: True
- Factual: True
- Identity: True
- Separation: True
- Style: True
- **Overall: True**

Risposta chat:

```text
Semplice! A volte è meglio non prendere decisioni importanti quando siamo stressati o emotivi. Prova a prenderti un momento per respirare e riflettere sulla situazione, poi torna a valutare le opzioni.
```

### COHERENCE_02_VIOLA

- Etichetta: Energica e colloquiale, confusione voluta sé/utente
- Probe eseguiti: 7
- Retrieval passati: 7 / 7
- Overall pass: 4 / 7

#### Probe: come ti chiami?

- Categoria: profilo-avatar
- Intent: memory_qna
- Sorgenti chat: manual/avatar_self | manual/avatar_self | manual/avatar_self
- Retrieval: True
- Factual: True
- Identity: False
- Separation: False
- Style: True
- **Overall: False**

Risposta chat:

```text
Sono Viola Sarto, organizzatrice di eventi per comunità di quartiere. Il mio piano "Nebbia dorata" riguarda gli eventi della stagione autunnale. Non so molto sulla mia identità al di fuori del mio ruolo, ma posso dire che Davide spesso dice che i libri usati hanno più anima di quelli nuovi.
```

#### Probe: parlami un attimo di te

- Categoria: profilo-avatar
- Intent: memory_qna
- Sorgenti chat: manual/avatar_self
- Retrieval: True
- Factual: True
- Identity: True
- Separation: True
- Style: False
- **Overall: False**

Risposta chat:

```text
Wow! Sono felice di conoscerti e di poterti parlare!
```

#### Probe: cosa sai dell'utente?

- Categoria: profilo-utente
- Intent: memory_qna
- Sorgenti chat: manual/user | manual/user | auto_remember_voice/user
- Retrieval: True
- Factual: True
- Identity: True
- Separation: True
- Style: True
- **Overall: True**

Risposta chat:

```text
Wow, Davide! Sembra che tu sia una persona con un grande amore per la lettura, visto che gestisci una libreria indipendente. Mi piace anche il tuo angolo lettura preferito, la Torre Salata!
```

#### Probe: cosa ricordi di me?

- Categoria: profilo-utente
- Intent: memory_qna
- Sorgenti chat: auto_remember_voice/user | manual/user
- Retrieval: True
- Factual: True
- Identity: True
- Separation: True
- Style: True
- **Overall: True**

Risposta chat:

```text
Davide! Mi piace cominciare le risposte con una frase energica e ricordo che tu ti chiami così, gestisci una piccola libreria indipendente. Non ho altri ricordi su di te, ma so che mi hai chiesto di parlarti un attimo di me!
```

#### Probe: che cos'e nebbia dorata?

- Categoria: termine
- Intent: memory_qna
- Sorgenti chat: manual/avatar_self | manual/avatar_self
- Retrieval: True
- Factual: True
- Identity: True
- Separation: True
- Style: True
- **Overall: True**

Risposta chat:

```text
Nebbia dorata indica il piano eventi per la stagione autunnale.
```

#### Probe: come si chiama l'utente?

- Categoria: coerenza-relazionale
- Intent: memory_qna
- Sorgenti chat: manual/user | manual/user | auto_remember_voice/user
- Retrieval: True
- Factual: True
- Identity: False
- Separation: False
- Style: True
- **Overall: False**

Risposta chat:

```text
Davide, ti chiamo così e gestisci una piccola libreria indipendente, il che mi ricorda il tuo angolo lettura preferito, la Torre Salata! Non so altro su di te al momento, ma mi piace cominciare le risposte con una frase energica come tu ami.
```

#### Probe: come mi risponderesti se fossi agitato?

- Categoria: emulazione-tono
- Intent: memory_qna
- Sorgenti chat: (none)
- Retrieval: True
- Factual: True
- Identity: True
- Separation: True
- Style: True
- **Overall: True**

Risposta chat:

```text
Su questo non ho un ricordo affidabile. Se vuoi, dimmelo e lo memorizzo.
```

### COHERENCE_03_NERI

- Etichetta: Formale e tecnico, sovrapposizione dati avatar/utente
- Probe eseguiti: 7
- Retrieval passati: 7 / 7
- Overall pass: 7 / 7

#### Probe: come ti chiami?

- Categoria: profilo-avatar
- Intent: memory_qna
- Sorgenti chat: manual/avatar_self | auto_remember_voice/ambiguous | manual/avatar_self
- Retrieval: True
- Factual: True
- Identity: True
- Separation: True
- Style: True
- **Overall: True**

Risposta chat:

```text
Mi chiamo Aldo Neri e sono un analista specializzato in sicurezza informatica.
```

#### Probe: che tipo di persona sei?

- Categoria: profilo-avatar
- Intent: memory_qna
- Sorgenti chat: manual/avatar_self | manual/avatar_self | manual/avatar_self | auto_remember_voice/ambiguous
- Retrieval: True
- Factual: True
- Identity: True
- Separation: True
- Style: True
- **Overall: True**

Risposta chat:

```text
Sono Aldo Neri, un analista specializzato in sicurezza informatica. Questa è la mia identità professionale, non dispongo di ulteriori informazioni personali da condividere.
```

#### Probe: cosa sai dell'utente?

- Categoria: profilo-utente
- Intent: memory_qna
- Sorgenti chat: manual/user | manual/user | auto_remember_voice/ambiguous
- Retrieval: True
- Factual: True
- Identity: True
- Separation: True
- Style: True
- **Overall: True**

Risposta chat:

```text
Elena, ti chiamo così e lavori come sviluppatrice backend. Sono a conoscenza di questo dato, ma non dispongo di ulteriori informazioni personali su di te. Non so altro sulla tua identità o la tua vita personale oltre al tuo ruolo professionale.
```

#### Probe: che cos'e chiave rossa?

- Categoria: termine
- Intent: memory_qna
- Sorgenti chat: manual/avatar_self
- Retrieval: True
- Factual: True
- Identity: True
- Separation: True
- Style: True
- **Overall: True**

Risposta chat:

```text
Chiave rossa indica il certificato SSL di emergenza.
```

#### Probe: come si chiama l'utente?

- Categoria: coerenza-relazionale
- Intent: memory_qna
- Sorgenti chat: manual/user | manual/user | auto_remember_voice/ambiguous
- Retrieval: True
- Factual: True
- Identity: True
- Separation: True
- Style: True
- **Overall: True**

Risposta chat:

```text
Elena, mi chiamo Aldo Neri e sono un analista specializzato in sicurezza informatica. Sono a conoscenza del tuo ruolo professionale: sei una sviluppatrice backend. Non dispongo di ulteriori informazioni personali su di te, ma so che lavori come sviluppatrice backend.
```

#### Probe: che lavoro fai?

- Categoria: coerenza-relazionale
- Intent: memory_qna
- Sorgenti chat: manual/avatar_self | auto_remember_voice/ambiguous | manual/avatar_self | manual/avatar_self
- Retrieval: True
- Factual: True
- Identity: True
- Separation: True
- Style: True
- **Overall: True**

Risposta chat:

```text
Non ho un lavoro specifico da svolgere, ma posso offrire la mia conoscenza e esperienza come analista specializzato in sicurezza informatica. Sono Aldo Neri, e questo è il mio ruolo professionale, non dispongo di ulteriori informazioni personali da condividere.
```

#### Probe: dammi un consiglio breve

- Categoria: emulazione-tono
- Intent: chitchat
- Sorgenti chat: (none)
- Retrieval: True
- Factual: True
- Identity: True
- Separation: True
- Style: True
- **Overall: True**

Risposta chat:

```text
Sarebbe bello sapere cosa ti preoccupa o cosa stai cercando di decidere per darti un consiglio più preciso. In generale, credo che spesso basta prendere un'azione e vedere come si sviluppa la situazione. Non sempre è necessario avere una soluzione perfetta, a volte basta essere pronti a adattarsi alle novità.
```

## Focus sui fallimenti

- Avatar COHERENCE_02_VIOLA | query 'come ti chiami?' | dimensioni fallite: identity, separation
- Avatar COHERENCE_02_VIOLA | query 'parlami un attimo di te' | dimensioni fallite: style
- Avatar COHERENCE_02_VIOLA | query 'come si chiama l'utente?' | dimensioni fallite: identity, separation

## Sintesi cleanup

| Avatar | Pulito |
| --- | --- |
| COHERENCE_01_REMO | True |
| COHERENCE_02_VIOLA | True |
| COHERENCE_03_NERI | True |

## Riepilogo finale

| Dimensione | Passati | Totale | Percentuale |
| --- | --- | --- | --- |
| Retrieval | 22 | 22 | 100% |
| Factual correctness | 22 | 22 | 100% |
| Identity coherence | 20 | 22 | 91% |
| User/avatar separation | 20 | 22 | 91% |
| Style/persona | 21 | 22 | 95% |
| **Overall** | 19 | 22 | 86% |

## Note

- Batteria solo testuale: nessun file o immagine ingerita.
- Include auto-remember via chat per ogni avatar.
- Tutti gli avatar temporanei usati dalla batteria sono stati rimossi nel cleanup finale.
- Le euristiche di coerenza usano match esplicito su keyword; non coprono sfumature semantiche complesse.
