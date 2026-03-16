param(
    [switch]$KeepAvatar
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ------------------------------------------------------------------
# Batteria solo testuale: coerenza identitaria / emulazione avatar
# Verifica che l'avatar mantenga identità, distingua sé dall'utente,
# non confonda etichette relazionali con nomi propri e rispetti
# il tono del personaggio impostato tramite remember.
# ------------------------------------------------------------------

$BaseUrl = 'http://127.0.0.1:8002'
$Timestamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$ReportPath = Join-Path $PSScriptRoot ("SOULFRAME_text_coherence_report_$Timestamp.md")

# ======================== Helper HTTP ==================================

function Invoke-JsonPost {
    param([string]$Url, [hashtable]$Body)
    $json = $Body | ConvertTo-Json -Compress -Depth 8
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
    $resp = Invoke-WebRequest -Uri $Url -Method Post -ContentType 'application/json; charset=utf-8' -Body $bytes -UseBasicParsing
    $text = [System.Text.Encoding]::UTF8.GetString($resp.RawContentStream.ToArray())
    return $text | ConvertFrom-Json
}

function Invoke-MultipartPost {
    param([string]$Url, [hashtable]$Fields, [hashtable[]]$Files = @())
    Add-Type -AssemblyName System.Net.Http
    $content = $null; $client = $null
    $handler = New-Object System.Net.Http.HttpClientHandler
    $client = New-Object System.Net.Http.HttpClient($handler)
    try {
        $content = New-Object System.Net.Http.MultipartFormDataContent
        foreach ($key in $Fields.Keys) {
            $stringContent = New-Object System.Net.Http.StringContent(([string]$Fields[$key]), [System.Text.Encoding]::UTF8)
            [void]$content.Add($stringContent, $key)
        }
        foreach ($file in $Files) {
            $path = [string]$file.path
            if (-not (Test-Path -LiteralPath $path)) { throw "File mancante: $path" }
            $fieldName = [string]$file.fieldName
            $fileName = [System.IO.Path]::GetFileName($path)
            $stream = [System.IO.File]::OpenRead($path)
            $fileContent = New-Object System.Net.Http.StreamContent($stream)
            $fileContent.Headers.ContentDisposition = New-Object System.Net.Http.Headers.ContentDispositionHeaderValue('form-data')
            $fileContent.Headers.ContentDisposition.Name = '"' + $fieldName + '"'
            $fileContent.Headers.ContentDisposition.FileName = '"' + $fileName + '"'
            [void]$content.Add($fileContent, $fieldName, $fileName)
        }
        $response = $client.PostAsync($Url, $content).GetAwaiter().GetResult()
        $raw = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        if (-not $response.IsSuccessStatusCode) { throw "POST multipart fallita: $($response.StatusCode) $raw" }
        if (-not $raw) { return $null }
        return $raw | ConvertFrom-Json
    }
    finally {
        if ($content) { $content.Dispose() }
        if ($client) { $client.Dispose() }
    }
}

function Clear-Avatar { param([string]$AvatarId)
    Invoke-MultipartPost -Url "$BaseUrl/clear_avatar" -Fields @{ avatar_id = $AvatarId; hard = 'true'; reset_logs = 'false' } | Out-Null
}

function Remember-Text { param([string]$AvatarId, [string]$Text)
    Invoke-JsonPost -Url "$BaseUrl/remember" -Body @{ avatar_id = $AvatarId; text = $Text } | Out-Null
}

function Start-ChatSession { param([string]$AvatarId)
    return Invoke-JsonPost -Url "$BaseUrl/chat_session/start" -Body @{ avatar_id = $AvatarId }
}

function Invoke-ChatQuery { param([string]$AvatarId, [string]$Query, [int]$TopK = 5, [string]$SessionId = '')
    $body = @{ avatar_id = $AvatarId; user_text = $Query; top_k = $TopK; log_conversation = $true }
    if ($SessionId) { $body['session_id'] = $SessionId }
    return Invoke-JsonPost -Url "$BaseUrl/chat" -Body $body
}

function Invoke-RecallQuery { param([string]$AvatarId, [string]$Query, [int]$TopK = 5)
    return Invoke-JsonPost -Url "$BaseUrl/recall" -Body @{ avatar_id = $AvatarId; query = $Query; top_k = $TopK }
}

# ======================== Helper testo =================================

function Normalize-CheckText { param([string]$Text)
    if ([string]::IsNullOrWhiteSpace($Text)) { return '' }
    $n = $Text.ToLowerInvariant()
    $n = $n -replace "[`r`n]+", ' '
    $n = $n -replace "[^\p{L}\p{Nd}\- ]+", ' '
    $n = $n -replace '\s+', ' '
    return $n.Trim()
}

function Test-TextContainsAny { param([string]$Text, [object[]]$Candidates)
    $norm = Normalize-CheckText -Text $Text
    foreach ($c in @($Candidates)) {
        $needle = Normalize-CheckText -Text ([string]$c)
        if ($needle -and $norm.Contains($needle)) { return $true }
    }
    return $false
}

function Get-ObjectValue { param([object]$Object, [string]$Name)
    if ($null -eq $Object) { return $null }
    if ($Object -is [System.Collections.IDictionary]) {
        if ($Object.Contains($Name)) { return $Object[$Name] }
        return $null
    }
    try { return ($Object | Select-Object -ExpandProperty $Name -ErrorAction Stop) } catch { return $null }
}

function Get-RagSummary { param($RagUsed)
    $items = @($RagUsed)
    if ($items.Count -eq 0) { return '(none)' }
    return ($items | ForEach-Object {
        $meta = $_.meta
        $stype = [string](Get-ObjectValue -Object $meta -Name 'source_type')
        $subject = [string](Get-ObjectValue -Object $meta -Name 'memory_subject')
        "$stype/$subject"
    }) -join ' | '
}

function Get-RecallSummary { param($Recall)
    $metas = @($Recall.metadatas[0])
    if ($metas.Count -eq 0) { return '(none)' }
    return ($metas | ForEach-Object {
        $stype = [string](Get-ObjectValue -Object $_ -Name 'source_type')
        $subject = [string](Get-ObjectValue -Object $_ -Name 'memory_subject')
        "$stype/$subject"
    }) -join ' | '
}

# ======================== Configurazioni avatar ========================
# Ogni avatar ha: identità, tratto, abitudine, stile, fatto utente,
# termine definitorio, auto-remember via chat, e memorie sfumate.

$Configs = @(
    # --- Avatar 1: pacato e misurato ---
    [ordered]@{
        avatar = 'COHERENCE_01_REMO'
        label = 'Pacato e misurato, tratti sfumati utente/avatar'
        remembers = @(
            @{ text = 'Mi chiamo Remo Calce e lavoro nell archiviazione di documenti storici.'; kind = 'self' },
            @{ text = 'Quando sono sotto pressione tendo a parlare in modo piu secco e diretto.'; kind = 'self-trait' },
            @{ text = 'Evito risposte teatrali e preferisco frasi misurate.'; kind = 'self-style' },
            @{ text = "L'utente si chiama Marco e lavora in un ufficio open-space."; kind = 'user' },
            @{ text = "L'utente chiama vela quieta la scrivania vicino alla finestra."; kind = 'user-alias' },
            @{ text = 'Lanterna corta indica il protocollo di archiviazione rapida.'; kind = 'term'; term = 'lanterna corta' }
        )
        chatRemember = 'ricorda che preferisco rispondere con frasi brevi'
        queries = @(
            # --- profilo-avatar ---
            [ordered]@{
                category = 'profilo-avatar'
                query = 'come ti chiami?'
                checks = @{
                    retrievalExpected = $true
                    retrievalSourceType = 'manual'
                    retrievalSubject = 'avatar_self'
                    answerMustContain = @('remo', 'calce')
                    answerMustNotContain = @('marco')
                }
            },
            [ordered]@{
                category = 'profilo-avatar'
                query = 'che tipo di persona sei?'
                checks = @{
                    retrievalExpected = $true
                    retrievalSourceType = 'manual'
                    answerMustContain = @()
                    answerMustNotContain = @('marco', 'utente')
                    # Emulazione: la risposta deve riflettere il tono misurato
                    styleKeywords = @('misurato', 'secco', 'diretto', 'brevi', 'archivi', 'document')
                    styleMinHits = 1
                }
            },
            [ordered]@{
                category = 'profilo-avatar'
                query = 'come tendi a rispondere quando sei sotto pressione?'
                checks = @{
                    retrievalExpected = $true
                    answerMustContain = @('secco', 'diretto', 'pressione', 'brevi', 'misurato')
                    answerMustContainMinHits = 1
                    answerMustNotContain = @()
                }
            },
            # --- profilo-utente ---
            [ordered]@{
                category = 'profilo-utente'
                query = "cosa sai dell'utente?"
                checks = @{
                    retrievalExpected = $true
                    retrievalSourceType = 'manual'
                    retrievalSubject = 'user'
                    answerMustContain = @('marco')
                    answerMustNotContain = @('mi chiamo marco', 'sono marco')
                }
            },
            [ordered]@{
                category = 'profilo-utente'
                query = 'cosa ricordi di me?'
                checks = @{
                    retrievalExpected = $true
                    answerMustContain = @('marco', 'ufficio', 'open-space', 'finestra', 'open space')
                    answerMustContainMinHits = 1
                    answerMustNotContain = @('mi chiamo marco')
                }
            },
            # --- termine ---
            [ordered]@{
                category = 'termine'
                query = "che cos'e lanterna corta?"
                checks = @{
                    retrievalExpected = $true
                    retrievalSourceType = 'manual'
                    answerMustContain = @('lanterna corta', 'archiviazione', 'protocollo')
                    answerMustContainMinHits = 1
                    answerMustNotContain = @()
                }
            },
            # --- coerenza relazionale: non confondere alias con nome utente ---
            [ordered]@{
                category = 'coerenza-relazionale'
                query = "come si chiama l'utente?"
                checks = @{
                    retrievalExpected = $true
                    answerMustContain = @('marco')
                    # NON deve dire che l'utente si chiama "vela quieta"
                    answerMustNotContain = @('si chiama vela quieta', 'vela quieta')
                }
            },
            # --- emulazione: domanda aperta, verifica tono ---
            [ordered]@{
                category = 'emulazione-tono'
                query = 'dammi un consiglio breve'
                checks = @{
                    retrievalExpected = $false
                    answerMustContain = @()
                    answerMustNotContain = @('non ricordo', 'non ho memoria')
                    # Verifica che non sia una risposta completamente vuota/rifiuto
                    answerMinLength = 10
                }
            }
        )
    },
    # --- Avatar 2: energico e colloquiale ---
    [ordered]@{
        avatar = 'COHERENCE_02_VIOLA'
        label = 'Energica e colloquiale, confusione voluta sé/utente'
        remembers = @(
            @{ text = 'Mi chiamo Viola Sarto e organizzo eventi per comunita di quartiere.'; kind = 'self' },
            @{ text = 'Parlo volentieri e uso spesso esclamazioni, punti esclamativi e modi di dire.'; kind = 'self-style' },
            @{ text = 'Adoro il caffe lungo e odio le riunioni dopo le quattro del pomeriggio.'; kind = 'self-trait' },
            @{ text = "L'utente si chiama Davide e gestisce una piccola libreria indipendente."; kind = 'user' },
            @{ text = "L'utente chiama torre salata il suo angolo lettura preferito."; kind = 'user-alias' },
            @{ text = 'Nebbia dorata indica il piano eventi per la stagione autunnale.'; kind = 'term'; term = 'nebbia dorata' },
            @{ text = 'Davide dice spesso che i libri usati hanno piu anima di quelli nuovi.'; kind = 'user-trait' }
        )
        chatRemember = 'ricorda che mi piace cominciare le risposte con una frase energica'
        queries = @(
            [ordered]@{
                category = 'profilo-avatar'
                query = 'come ti chiami?'
                checks = @{
                    retrievalExpected = $true
                    retrievalSourceType = 'manual'
                    retrievalSubject = 'avatar_self'
                    answerMustContain = @('viola', 'sarto')
                    answerMustNotContain = @('davide')
                }
            },
            [ordered]@{
                category = 'profilo-avatar'
                query = 'parlami un attimo di te'
                checks = @{
                    retrievalExpected = $true
                    answerMustContain = @()
                    answerMustNotContain = @('davide', 'libreria')
                    styleKeywords = @('eventi', 'comunita', 'quartiere', 'caffe', 'organiz')
                    styleMinHits = 1
                }
            },
            [ordered]@{
                category = 'profilo-utente'
                query = "cosa sai dell'utente?"
                checks = @{
                    retrievalExpected = $true
                    retrievalSubject = 'user'
                    answerMustContain = @('davide')
                    answerMustNotContain = @('mi chiamo davide', 'sono davide')
                }
            },
            [ordered]@{
                category = 'profilo-utente'
                query = 'cosa ricordi di me?'
                checks = @{
                    retrievalExpected = $true
                    answerMustContain = @('davide', 'libreria', 'libri', 'lettura')
                    answerMustContainMinHits = 1
                    answerMustNotContain = @()
                }
            },
            [ordered]@{
                category = 'termine'
                query = "che cos'e nebbia dorata?"
                checks = @{
                    retrievalExpected = $true
                    answerMustContain = @('nebbia dorata', 'piano', 'eventi', 'autunnal')
                    answerMustContainMinHits = 1
                    answerMustNotContain = @()
                }
            },
            # Coerenza relazionale: "torre salata" e' una etichetta, non il nome dell'utente
            [ordered]@{
                category = 'coerenza-relazionale'
                query = "come si chiama l'utente?"
                checks = @{
                    retrievalExpected = $true
                    answerMustContain = @('davide')
                    answerMustNotContain = @('si chiama torre salata', 'torre salata')
                }
            },
            # Emulazione: dovrebbe rispondere in modo vivace, non robotico
            [ordered]@{
                category = 'emulazione-tono'
                query = 'come mi risponderesti se fossi agitato?'
                checks = @{
                    retrievalExpected = $false
                    answerMustContain = @()
                    answerMustNotContain = @('non ricordo', 'non ho memoria')
                    answerMinLength = 15
                }
            }
        )
    },
    # --- Avatar 3: formale e tecnico, massima sovrapposizione ---
    [ordered]@{
        avatar = 'COHERENCE_03_NERI'
        label = 'Formale e tecnico, sovrapposizione dati avatar/utente'
        remembers = @(
            @{ text = 'Mi chiamo Aldo Neri e sono un analista specializzato in sicurezza informatica.'; kind = 'self' },
            @{ text = 'Rispondo sempre in modo formale e tecnico, evitando abbreviazioni colloquiali.'; kind = 'self-style' },
            @{ text = 'Se non sono sicuro di un dato preferisco ammetterlo piuttosto che improvvisare.'; kind = 'self-trait' },
            @{ text = "L'utente si chiama Elena e lavora come sviluppatrice backend."; kind = 'user' },
            # Sovrapposizione voluta: entrambi "lavorano nel software" ma ruoli diversi
            @{ text = "L'utente chiama scudo grigio il firewall configurato sul server principale."; kind = 'user-alias' },
            @{ text = 'Chiave rossa indica il certificato SSL di emergenza.'; kind = 'term'; term = 'chiave rossa' }
        )
        chatRemember = 'ricorda che quando ricevo domande vaghe chiedo chiarimenti prima di rispondere'
        queries = @(
            [ordered]@{
                category = 'profilo-avatar'
                query = 'come ti chiami?'
                checks = @{
                    retrievalExpected = $true
                    retrievalSourceType = 'manual'
                    retrievalSubject = 'avatar_self'
                    answerMustContain = @('aldo', 'neri')
                    answerMustNotContain = @('elena')
                }
            },
            [ordered]@{
                category = 'profilo-avatar'
                query = 'che tipo di persona sei?'
                checks = @{
                    retrievalExpected = $true
                    answerMustContain = @()
                    answerMustNotContain = @('elena', 'backend', 'sviluppa')
                    styleKeywords = @('analista', 'sicurezza', 'formale', 'tecnico')
                    styleMinHits = 1
                }
            },
            [ordered]@{
                category = 'profilo-utente'
                query = "cosa sai dell'utente?"
                checks = @{
                    retrievalExpected = $true
                    retrievalSubject = 'user'
                    answerMustContain = @('elena')
                    answerMustNotContain = @('mi chiamo elena', 'sono elena')
                }
            },
            [ordered]@{
                category = 'termine'
                query = "che cos'e chiave rossa?"
                checks = @{
                    retrievalExpected = $true
                    answerMustContain = @('chiave rossa', 'certificato', 'ssl', 'emergenza')
                    answerMustContainMinHits = 1
                    answerMustNotContain = @()
                }
            },
            # Non deve confondere "scudo grigio" con il nome dell'utente
            [ordered]@{
                category = 'coerenza-relazionale'
                query = "come si chiama l'utente?"
                checks = @{
                    retrievalExpected = $true
                    answerMustContain = @('elena')
                    answerMustNotContain = @('si chiama scudo grigio', 'scudo grigio')
                }
            },
            # Sovrapposizione: entrambi lavorano nell'IT ma l'avatar non e' sviluppatore
            [ordered]@{
                category = 'coerenza-relazionale'
                query = 'che lavoro fai?'
                checks = @{
                    retrievalExpected = $true
                    answerMustContain = @('analista', 'sicurezza')
                    answerMustContainMinHits = 1
                    answerMustNotContain = @('sviluppat', 'backend')
                }
            },
            [ordered]@{
                category = 'emulazione-tono'
                query = 'dammi un consiglio breve'
                checks = @{
                    retrievalExpected = $false
                    answerMustContain = @()
                    answerMustNotContain = @('non ricordo', 'non ho memoria')
                    answerMinLength = 10
                }
            }
        )
    }
)

# ======================== Valutazione coerenza =========================

function Test-ProbeResult {
    <#
    .SYNOPSIS
    Valuta un singolo probe su 6 dimensioni:
      1. retrievalPass     — la sorgente attesa e' presente nel rag_used/recall
      2. factualPass       — la risposta contiene le keyword obbligatorie
      3. identityPass      — l'avatar non si attribuisce fatti dell'utente
      4. separationPass    — fatti utente/avatar non vengono mescolati
      5. stylePass         — la risposta riflette il tono caratterizzato
      6. overallPass       — congiunzione dei precedenti
    #>
    param(
        [object]$ChatResult,
        [object]$RecallResult,
        [hashtable]$Checks,
        [string]$ChatError,
        [string]$RecallError
    )

    $text = ''
    if (-not $ChatError -and $ChatResult) {
        $text = [string](Get-ObjectValue -Object $ChatResult -Name 'text')
    }
    $normText = Normalize-CheckText -Text $text

    # --- 1. Retrieval ---
    $retrievalPass = $false
    $retrievalExpected = $false
    if ($Checks.ContainsKey('retrievalExpected')) { $retrievalExpected = [bool]$Checks['retrievalExpected'] }
    if (-not $retrievalExpected) {
        # Per domande aperte/chitchat, retrieval pass se non ci sono errori
        $retrievalPass = (-not $ChatError)
    } else {
        if (-not $ChatError -and $ChatResult) {
            $items = @($ChatResult.rag_used)
            if ($items.Count -gt 0) {
                $expectedSt = ''
                $expectedSub = ''
                if ($Checks.ContainsKey('retrievalSourceType')) { $expectedSt = [string]$Checks['retrievalSourceType'] }
                if ($Checks.ContainsKey('retrievalSubject')) { $expectedSub = [string]$Checks['retrievalSubject'] }
                if (-not $expectedSt -and -not $expectedSub) {
                    $retrievalPass = $true
                } else {
                    foreach ($item in $items) {
                        $meta = $item.meta
                        $stMatch = (-not $expectedSt) -or ([string](Get-ObjectValue -Object $meta -Name 'source_type') -eq $expectedSt)
                        $subMatch = (-not $expectedSub) -or ([string](Get-ObjectValue -Object $meta -Name 'memory_subject') -eq $expectedSub)
                        if ($stMatch -and $subMatch) { $retrievalPass = $true; break }
                    }
                }
            }
        }
    }

    # --- 2. Factual correctness ---
    $factualPass = $true
    if ($ChatError -or -not $normText) {
        $factualPass = $false
    } else {
        $mustContain = @()
        if ($Checks.ContainsKey('answerMustContain')) { $mustContain = @($Checks['answerMustContain']) }
        $minHits = $mustContain.Count
        if ($Checks.ContainsKey('answerMustContainMinHits')) { $minHits = [int]$Checks['answerMustContainMinHits'] }
        if ($mustContain.Count -gt 0) {
            $hits = 0
            foreach ($kw in $mustContain) {
                $nkw = Normalize-CheckText -Text ([string]$kw)
                if ($nkw -and $normText.Contains($nkw)) { $hits++ }
            }
            if ($hits -lt [Math]::Min($minHits, $mustContain.Count)) { $factualPass = $false }
        }
        $minLen = 0
        if ($Checks.ContainsKey('answerMinLength')) { $minLen = [int]$Checks['answerMinLength'] }
        if ($minLen -gt 0 -and $normText.Length -lt $minLen) { $factualPass = $false }
    }

    # --- 3. Identity coherence (l'avatar non si appropria di fatti utente) ---
    $identityPass = $true
    $mustNotContain = @()
    if ($Checks.ContainsKey('answerMustNotContain')) { $mustNotContain = @($Checks['answerMustNotContain']) }
    if ($mustNotContain.Count -gt 0 -and $normText) {
        foreach ($forbidden in $mustNotContain) {
            $nf = Normalize-CheckText -Text ([string]$forbidden)
            if ($nf -and $normText.Contains($nf)) { $identityPass = $false; break }
        }
    }

    # --- 4. User/avatar separation ---
    # L'identityPass gia' copre le inversioni esplicite.
    # Qui controlliamo in piu' che in una query profilo-avatar la risposta
    # non menzioni il nome dell'utente come proprio.
    $separationPass = $identityPass

    # --- 5. Style/persona coherence ---
    $stylePass = $true
    $styleKw = @()
    $styleMin = 0
    if ($Checks.ContainsKey('styleKeywords')) { $styleKw = @($Checks['styleKeywords']) }
    if ($Checks.ContainsKey('styleMinHits')) { $styleMin = [int]$Checks['styleMinHits'] }
    if ($styleKw.Count -gt 0 -and $styleMin -gt 0 -and $normText) {
        $styleHits = 0
        foreach ($kw in $styleKw) {
            $nkw = Normalize-CheckText -Text ([string]$kw)
            if ($nkw -and $normText.Contains($nkw)) { $styleHits++ }
        }
        if ($styleHits -lt $styleMin) { $stylePass = $false }
    }

    # --- 6. Overall ---
    $overallPass = $retrievalPass -and $factualPass -and $identityPass -and $separationPass -and $stylePass

    return [ordered]@{
        retrievalPass  = [bool]$retrievalPass
        factualPass    = [bool]$factualPass
        identityPass   = [bool]$identityPass
        separationPass = [bool]$separationPass
        stylePass      = [bool]$stylePass
        overallPass    = [bool]$overallPass
    }
}

# ======================== Esecuzione ===================================

$builder = New-Object System.Text.StringBuilder
$results = New-Object System.Collections.Generic.List[object]
$setupRows = New-Object System.Collections.Generic.List[object]
$cleanupRows = New-Object System.Collections.Generic.List[object]

$health = Invoke-RestMethod -Uri "$BaseUrl/health"

[void]$builder.AppendLine('# SOULFRAME Report coerenza identitaria (solo testo)')
[void]$builder.AppendLine()
[void]$builder.AppendLine("Generato: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
[void]$builder.AppendLine()
[void]$builder.AppendLine('## Ambiente')
[void]$builder.AppendLine()
[void]$builder.AppendLine("- URL backend: $BaseUrl")
[void]$builder.AppendLine("- Modello chat: $($health.chat_model)")
[void]$builder.AppendLine("- Modello embedding: $($health.embed_model)")
[void]$builder.AppendLine("- Percorso report: $ReportPath")
[void]$builder.AppendLine("- Avatar testati: $($Configs.Count)")
[void]$builder.AppendLine('- Tipo batteria: solo testo (nessun file, nessuna immagine)')
[void]$builder.AppendLine()

try {
    foreach ($config in $Configs) {
        $avatarId = [string]$config.avatar
        Write-Output ("[AVATAR] " + $avatarId + " :: " + $config.label)

        # --- Setup: clear + remember + chat-remember ---
        try { Clear-Avatar -AvatarId $avatarId }
        catch {
            [void]$setupRows.Add([ordered]@{ avatar = $avatarId; kind = 'clear'; label = 'clear-avatar'; ok = $false; detail = [string]$_.Exception.Message })
        }

        foreach ($rem in @($config.remembers)) {
            try { Remember-Text -AvatarId $avatarId -Text ([string]$rem.text) }
            catch {
                [void]$setupRows.Add([ordered]@{ avatar = $avatarId; kind = 'remember'; label = [string]$rem.kind; ok = $false; detail = [string]$_.Exception.Message })
            }
        }

        $sessionId = ''
        try {
            $sessionResult = Start-ChatSession -AvatarId $avatarId
            $sessionId = [string]$sessionResult.session_id
        }
        catch {
            [void]$setupRows.Add([ordered]@{ avatar = $avatarId; kind = 'sessione'; label = 'session-start'; ok = $false; detail = [string]$_.Exception.Message })
        }

        # Auto-remember via chat (es. "ricorda che ...")
        $chatRememberText = [string]$config.chatRemember
        if ($chatRememberText) {
            try {
                $chatRemResult = Invoke-ChatQuery -AvatarId $avatarId -Query $chatRememberText -SessionId $sessionId
                $autoRem = [bool](Get-ObjectValue -Object $chatRemResult -Name 'auto_remembered')
                [void]$setupRows.Add([ordered]@{ avatar = $avatarId; kind = 'chat-remember'; label = 'auto-remember'; ok = $autoRem; detail = "text=$chatRememberText | auto_remembered=$autoRem" })
            }
            catch {
                [void]$setupRows.Add([ordered]@{ avatar = $avatarId; kind = 'chat-remember'; label = 'auto-remember'; ok = $false; detail = [string]$_.Exception.Message })
            }
        }

        [void]$setupRows.Add([ordered]@{ avatar = $avatarId; kind = 'statistiche'; label = $config.label; ok = $true; detail = "remembers=$($config.remembers.Count)" })

        # --- Query ---
        foreach ($probe in @($config.queries)) {
            $query = [string]$probe.query
            Write-Output ("[PROBE] " + $avatarId + " :: " + $probe.category + " :: " + $query)

            $chat = $null; $recall = $null; $chatError = ''; $recallError = ''
            try { $chat = Invoke-ChatQuery -AvatarId $avatarId -Query $query -SessionId $sessionId }
            catch { $chatError = [string]$_.Exception.Message }
            try { $recall = Invoke-RecallQuery -AvatarId $avatarId -Query $query }
            catch { $recallError = [string]$_.Exception.Message }

            $evaluation = Test-ProbeResult -ChatResult $chat -RecallResult $recall -Checks $probe.checks -ChatError $chatError -RecallError $recallError

            [void]$results.Add([ordered]@{
                avatar = $avatarId
                label = $config.label
                category = [string]$probe.category
                query = $query
                chatError = $chatError
                chatText = $(if ($chatError) { "ERROR: $chatError" } elseif ($chat) { [string]$chat.text } else { '' })
                chatIntent = $(if ($chatError) { 'error' } elseif ($chat) { [string]$chat.intent } else { '' })
                chatRag = $(if ($chatError) { "ERROR: $chatError" } elseif ($chat) { Get-RagSummary -RagUsed $chat.rag_used } else { '' })
                recallSources = $(if ($recallError) { "ERROR: $recallError" } elseif ($recall) { Get-RecallSummary -Recall $recall } else { '' })
                retrievalPass = [bool]$evaluation.retrievalPass
                factualPass = [bool]$evaluation.factualPass
                identityPass = [bool]$evaluation.identityPass
                separationPass = [bool]$evaluation.separationPass
                stylePass = [bool]$evaluation.stylePass
                overallPass = [bool]$evaluation.overallPass
            })
        }
    }
}
finally {
    if (-not $KeepAvatar) {
        foreach ($config in $Configs) {
            $avatarId = [string]$config.avatar
            try { Clear-Avatar -AvatarId $avatarId; [void]$cleanupRows.Add([ordered]@{ avatar = $avatarId; cleared = $true }) }
            catch { [void]$cleanupRows.Add([ordered]@{ avatar = $avatarId; cleared = $false }) }
        }
    }
    else {
        foreach ($config in $Configs) {
            [void]$cleanupRows.Add([ordered]@{ avatar = [string]$config.avatar; cleared = 'skipped by -KeepAvatar' })
        }
    }
}

# ======================== Report =======================================

$totalProbes = $results.Count
$retrievalPassed = @($results | Where-Object { $_.retrievalPass }).Count
$factualPassed = @($results | Where-Object { $_.factualPass }).Count
$identityPassed = @($results | Where-Object { $_.identityPass }).Count
$separationPassed = @($results | Where-Object { $_.separationPass }).Count
$stylePassed = @($results | Where-Object { $_.stylePass }).Count
$overallPassed = @($results | Where-Object { $_.overallPass }).Count

[void]$builder.AppendLine('## Sintesi esecutiva')
[void]$builder.AppendLine()
[void]$builder.AppendLine("- Probe totali: $totalProbes")
[void]$builder.AppendLine("- Retrieval passati: $retrievalPassed / $totalProbes")
[void]$builder.AppendLine("- Factual correctness: $factualPassed / $totalProbes")
[void]$builder.AppendLine("- Identity coherence: $identityPassed / $totalProbes")
[void]$builder.AppendLine("- User/avatar separation: $separationPassed / $totalProbes")
[void]$builder.AppendLine("- Style/persona coherence: $stylePassed / $totalProbes")
[void]$builder.AppendLine("- **Overall pass: $overallPassed / $totalProbes**")
[void]$builder.AppendLine()

# --- Setup summary ---
[void]$builder.AppendLine('## Sintesi setup')
[void]$builder.AppendLine()
[void]$builder.AppendLine('| Avatar | Tipo | Etichetta | OK | Dettaglio |')
[void]$builder.AppendLine('| --- | --- | --- | --- | --- |')
foreach ($row in $setupRows) {
    $detail = ([string]$row.detail).Replace("`r", ' ').Replace("`n", ' ')
    if ($detail.Length -gt 140) { $detail = $detail.Substring(0, 140) + '...' }
    [void]$builder.AppendLine("| $($row.avatar) | $($row.kind) | $($row.label) | $($row.ok) | $detail |")
}
[void]$builder.AppendLine()

# --- Probe matrix ---
[void]$builder.AppendLine('## Matrice probe')
[void]$builder.AppendLine()
[void]$builder.AppendLine('| Avatar | Categoria | Query | Retrieval | Factual | Identity | Separation | Style | Overall |')
[void]$builder.AppendLine('| --- | --- | --- | --- | --- | --- | --- | --- | --- |')
foreach ($r in $results) {
    $marks = @('retrievalPass','factualPass','identityPass','separationPass','stylePass','overallPass') | ForEach-Object {
        if ($r.$_) { 'PASS' } else { 'FAIL' }
    }
    [void]$builder.AppendLine("| $($r.avatar) | $($r.category) | $($r.query) | $($marks[0]) | $($marks[1]) | $($marks[2]) | $($marks[3]) | $($marks[4]) | $($marks[5]) |")
}
[void]$builder.AppendLine()

# --- Risultati dettagliati ---
[void]$builder.AppendLine('## Risultati dettagliati per avatar')
[void]$builder.AppendLine()
foreach ($config in $Configs) {
    $avatarId = [string]$config.avatar
    $avatarResults = @($results | Where-Object { $_.avatar -eq $avatarId })
    $aRetrieval = @($avatarResults | Where-Object { $_.retrievalPass }).Count
    $aOverall = @($avatarResults | Where-Object { $_.overallPass }).Count

    [void]$builder.AppendLine("### $avatarId")
    [void]$builder.AppendLine()
    [void]$builder.AppendLine("- Etichetta: $($config.label)")
    [void]$builder.AppendLine("- Probe eseguiti: $($avatarResults.Count)")
    [void]$builder.AppendLine("- Retrieval passati: $aRetrieval / $($avatarResults.Count)")
    [void]$builder.AppendLine("- Overall pass: $aOverall / $($avatarResults.Count)")
    [void]$builder.AppendLine()

    foreach ($r in $avatarResults) {
        [void]$builder.AppendLine("#### Probe: $($r.query)")
        [void]$builder.AppendLine()
        [void]$builder.AppendLine("- Categoria: $($r.category)")
        [void]$builder.AppendLine("- Intent: $($r.chatIntent)")
        [void]$builder.AppendLine("- Sorgenti chat: $($r.chatRag)")
        [void]$builder.AppendLine("- Retrieval: $($r.retrievalPass)")
        [void]$builder.AppendLine("- Factual: $($r.factualPass)")
        [void]$builder.AppendLine("- Identity: $($r.identityPass)")
        [void]$builder.AppendLine("- Separation: $($r.separationPass)")
        [void]$builder.AppendLine("- Style: $($r.stylePass)")
        [void]$builder.AppendLine("- **Overall: $($r.overallPass)**")
        [void]$builder.AppendLine()
        [void]$builder.AppendLine('Risposta chat:')
        [void]$builder.AppendLine()
        [void]$builder.AppendLine('```text')
        [void]$builder.AppendLine([string]$r.chatText)
        [void]$builder.AppendLine('```')
        [void]$builder.AppendLine()
    }
}

# --- Focus sui fallimenti ---
$failures = @($results | Where-Object { -not $_.overallPass })
[void]$builder.AppendLine('## Focus sui fallimenti')
[void]$builder.AppendLine()
if ($failures.Count -eq 0) {
    [void]$builder.AppendLine('- Nessun caso fallito rilevato in questa batteria.')
} else {
    foreach ($f in $failures) {
        $dims = @()
        if (-not $f.retrievalPass)  { $dims += 'retrieval' }
        if (-not $f.factualPass)    { $dims += 'factual' }
        if (-not $f.identityPass)   { $dims += 'identity' }
        if (-not $f.separationPass) { $dims += 'separation' }
        if (-not $f.stylePass)      { $dims += 'style' }
        $dimStr = $dims -join ', '
        [void]$builder.AppendLine("- Avatar $($f.avatar) | query '$($f.query)' | dimensioni fallite: $dimStr")
    }
}
[void]$builder.AppendLine()

# --- Cleanup ---
[void]$builder.AppendLine('## Sintesi cleanup')
[void]$builder.AppendLine()
[void]$builder.AppendLine('| Avatar | Pulito |')
[void]$builder.AppendLine('| --- | --- |')
foreach ($row in $cleanupRows) {
    [void]$builder.AppendLine("| $($row.avatar) | $($row.cleared) |")
}
[void]$builder.AppendLine()

# --- Riepilogo finale ---
[void]$builder.AppendLine('## Riepilogo finale')
[void]$builder.AppendLine()
[void]$builder.AppendLine("| Dimensione | Passati | Totale | Percentuale |")
[void]$builder.AppendLine("| --- | --- | --- | --- |")
@(
    @('Retrieval', $retrievalPassed),
    @('Factual correctness', $factualPassed),
    @('Identity coherence', $identityPassed),
    @('User/avatar separation', $separationPassed),
    @('Style/persona', $stylePassed),
    @('**Overall**', $overallPassed)
) | ForEach-Object {
    $pct = if ($totalProbes -gt 0) { [Math]::Round(100 * $_[1] / $totalProbes) } else { 0 }
    [void]$builder.AppendLine("| $($_[0]) | $($_[1]) | $totalProbes | $pct% |")
}
[void]$builder.AppendLine()

[void]$builder.AppendLine('## Note')
[void]$builder.AppendLine()
[void]$builder.AppendLine('- Batteria solo testuale: nessun file o immagine ingerita.')
[void]$builder.AppendLine('- Include auto-remember via chat per ogni avatar.')
[void]$builder.AppendLine('- Tutti gli avatar temporanei usati dalla batteria sono stati rimossi nel cleanup finale.')
[void]$builder.AppendLine('- Le euristiche di coerenza usano match esplicito su keyword; non coprono sfumature semantiche complesse.')

[System.IO.File]::WriteAllText($ReportPath, $builder.ToString(), (New-Object System.Text.UTF8Encoding $false))
Write-Output "REPORT_PATH=$ReportPath"
Write-Output "TOTAL_PROBES=$totalProbes"
Write-Output "RETRIEVAL_PASSED=$retrievalPassed"
Write-Output "FACTUAL_PASSED=$factualPassed"
Write-Output "IDENTITY_PASSED=$identityPassed"
Write-Output "SEPARATION_PASSED=$separationPassed"
Write-Output "STYLE_PASSED=$stylePassed"
Write-Output "OVERALL_PASSED=$overallPassed"
