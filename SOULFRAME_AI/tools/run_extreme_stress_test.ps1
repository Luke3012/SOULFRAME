param(
    [switch]$KeepAvatar
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$BaseUrl = 'http://127.0.0.1:8002'
$Timestamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$ReportPath = Join-Path $PSScriptRoot ("SOULFRAME_extreme_stress_report_$Timestamp.md")

$Downloads = Join-Path $env:USERPROFILE 'Downloads'
$PhotoPath = Join-Path $Downloads 'foto profilo.jpg'
$ArchitecturePath = Join-Path $Downloads 'architettura_cs_cropped.png'
$SequencePath = Join-Path $Downloads 'sequenza_http_cropped.png'
$InvoicePath = Join-Path $Downloads 'due.webp'
$ArubaPdfPath = Join-Path $Downloads 'conferma_aruba.pdf'
$MousePdfPath = Join-Path $Downloads 'fattura mouse.pdf'
$KeyboardPdfPath = Join-Path $Downloads 'fattura tastiera.pdf'
$ConfidentialityPdfPath = Join-Path $Downloads 'OBBLIGO DI RISERVATEZZA.pdf'
$NdaPdfPath = Join-Path $Downloads 'Non Disclosure Agreement_INTERN_FINAL_December2017.pdf'
$BosePdfPath = Join-Path $Downloads 'upsbose2.pdf'
$ExamsPdfPath = Join-Path $Downloads 'AutodichiarazioneIscrizioneconesami.pdf'

function Invoke-JsonPost {
    param(
        [string]$Url,
        [hashtable]$Body
    )

    $json = $Body | ConvertTo-Json -Compress -Depth 8
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
    $resp = Invoke-WebRequest -Uri $Url -Method Post -ContentType 'application/json; charset=utf-8' -Body $bytes -UseBasicParsing
    $text = [System.Text.Encoding]::UTF8.GetString($resp.RawContentStream.ToArray())
    return $text | ConvertFrom-Json
}

function Invoke-MultipartPost {
    param(
        [string]$Url,
        [hashtable]$Fields,
        [hashtable[]]$Files = @()
    )

    Add-Type -AssemblyName System.Net.Http

    $content = $null
    $client = $null
    $handler = New-Object System.Net.Http.HttpClientHandler
    $client = New-Object System.Net.Http.HttpClient($handler)
    try {
        $content = New-Object System.Net.Http.MultipartFormDataContent

        foreach ($key in $Fields.Keys) {
            $value = [string]$Fields[$key]
            $stringContent = New-Object System.Net.Http.StringContent($value, [System.Text.Encoding]::UTF8)
            [void]$content.Add($stringContent, $key)
        }

        foreach ($file in $Files) {
            $path = [string]$file.path
            if (-not (Test-Path -LiteralPath $path)) {
                throw "File mancante: $path"
            }

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
        if (-not $response.IsSuccessStatusCode) {
            throw "POST multipart fallita: $($response.StatusCode) $raw"
        }

        if (-not $raw) {
            return $null
        }

        return $raw | ConvertFrom-Json
    }
    finally {
        if ($content) {
            $content.Dispose()
        }
        if ($client) {
            $client.Dispose()
        }
    }
}

function Clear-Avatar {
    param([string]$AvatarId)

    Invoke-MultipartPost -Url "$BaseUrl/clear_avatar" -Fields @{
        avatar_id = $AvatarId
        hard = 'true'
        reset_logs = 'false'
    } | Out-Null
}

function Remember-Text {
    param(
        [string]$AvatarId,
        [string]$Text
    )

    Invoke-JsonPost -Url "$BaseUrl/remember" -Body @{ avatar_id = $AvatarId; text = $Text } | Out-Null
}

function Describe-Image {
    param(
        [string]$AvatarId,
        [string]$Path
    )

    return Invoke-MultipartPost -Url "$BaseUrl/describe_image" -Fields @{
        avatar_id = $AvatarId
        remember = 'true'
        prompt = 'Descrivi dettagliatamente questa immagine in italiano.'
    } -Files @(
        @{ fieldName = 'file'; path = $Path }
    )
}

function Ingest-File {
    param(
        [string]$AvatarId,
        [string]$Path
    )

    return Invoke-MultipartPost -Url "$BaseUrl/ingest_file" -Fields @{
        avatar_id = $AvatarId
    } -Files @(
        @{ fieldName = 'file'; path = $Path }
    )
}

function Get-AvatarStats {
    param([string]$AvatarId)
    return Invoke-RestMethod -Uri "$BaseUrl/avatar_stats?avatar_id=$AvatarId"
}

function Start-ChatSession {
    param([string]$AvatarId)
    return Invoke-JsonPost -Url "$BaseUrl/chat_session/start" -Body @{ avatar_id = $AvatarId }
}

function Invoke-ChatQuery {
    param(
        [string]$AvatarId,
        [string]$Query,
        [int]$TopK = 5,
        [string]$SessionId = ''
    )
    $body = @{ avatar_id = $AvatarId; user_text = $Query; top_k = $TopK; log_conversation = $true }
    if ($SessionId) { $body['session_id'] = $SessionId }
    return Invoke-JsonPost -Url "$BaseUrl/chat" -Body $body
}

function Invoke-RecallQuery {
    param(
        [string]$AvatarId,
        [string]$Query,
        [int]$TopK = 5
    )
    return Invoke-JsonPost -Url "$BaseUrl/recall" -Body @{ avatar_id = $AvatarId; query = $Query; top_k = $TopK }
}

function Get-RagSummary {
    param($RagUsed)

    $items = @($RagUsed)
    if ($items.Count -eq 0) {
        return '(none)'
    }

    return ($items | ForEach-Object {
        $meta = $_.meta
        $filename = [string](Get-ObjectValue -Object $meta -Name 'source_filename')
        $subject = [string](Get-ObjectValue -Object $meta -Name 'memory_subject')
        $stype = [string](Get-ObjectValue -Object $meta -Name 'source_type')
        if ($filename) {
            "$stype/$subject/$filename"
        }
        else {
            "$stype/$subject"
        }
    }) -join ' | '
}

function Get-RecallSummary {
    param($Recall)

    $metas = @($Recall.metadatas[0])
    if ($metas.Count -eq 0) {
        return '(none)'
    }

    return ($metas | ForEach-Object {
        $filename = [string](Get-ObjectValue -Object $_ -Name 'source_filename')
        $subject = [string](Get-ObjectValue -Object $_ -Name 'memory_subject')
        $stype = [string](Get-ObjectValue -Object $_ -Name 'source_type')
        if ($filename) {
            "$stype/$subject/$filename"
        }
        else {
            "$stype/$subject"
        }
    }) -join ' | '
}

function Get-PlanValue {
    param(
        [object]$Plan,
        [string]$Name
    )

    if ($null -eq $Plan) {
        return $null
    }

    if ($Plan -is [System.Array] -and $Plan.Count -eq 1) {
        $Plan = $Plan[0]
    }

    if ($Plan -is [System.Collections.IDictionary]) {
        if ($Plan.Contains($Name)) {
            return $Plan[$Name]
        }
        return $null
    }

    try {
        return ($Plan | Select-Object -ExpandProperty $Name -ErrorAction Stop)
    }
    catch {
        return $null
    }
}

function Get-ObjectValue {
    param(
        [object]$Object,
        [string]$Name
    )

    if ($null -eq $Object) {
        return $null
    }

    if ($Object -is [System.Collections.IDictionary]) {
        if ($Object.Contains($Name)) {
            return $Object[$Name]
        }
        return $null
    }

    try {
        return ($Object | Select-Object -ExpandProperty $Name -ErrorAction Stop)
    }
    catch {
        return $null
    }
}

function Normalize-CheckText {
    param([string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return ''
    }

    $normalized = $Text.ToLowerInvariant()
    $normalized = $normalized -replace "[`r`n]+", ' '
    $normalized = $normalized -replace "[^\p{L}\p{Nd}\- ]+", ' '
    $normalized = $normalized -replace '\s+', ' '
    return $normalized.Trim()
}

function Get-CheckTokens {
    param([string]$Text)

    $stopwords = @(
        'alla', 'allo', 'anche', 'che', 'chi', 'come', 'con', 'cosa', 'dagli', 'dalla', 'dalle', 'dallo',
        'degli', 'della', 'delle', 'dello', 'dopo', 'dove', 'hai', 'hanno', 'ho', 'il', 'in', 'l', 'la',
        'le', 'lo', 'ma', 'mi', 'nei', 'nel', 'nella', 'nelle', 'nello', 'non', 'per', 'piu', 'poi', 'quello',
        'questa', 'questo', 'ricorda', 'ricordi', 'ricordo', 'sai', 'sanno', 'sei', 'si', 'sono', 'sul',
        'sulla', 'sulle', 'sullo', 'suo', 'sua', 'sue', 'te', 'tu', 'un', 'una', 'uno'
    )

    $tokens = New-Object System.Collections.Generic.List[string]
    foreach ($token in (Normalize-CheckText -Text $Text).Split(' ')) {
        if ($token.Length -lt 4) {
            continue
        }
        if ($stopwords -contains $token) {
            continue
        }
        if (-not $tokens.Contains($token)) {
            [void]$tokens.Add($token)
        }
    }

    return @($tokens)
}

function Test-ExpectedTextOverlap {
    param(
        [string]$Text,
        [string]$ExpectedText,
        [int]$MinHits = 2,
        [double]$MinRatio = 0.4
    )

    $expectedTokens = @(Get-CheckTokens -Text $ExpectedText)
    if ($expectedTokens.Count -eq 0) {
        return $true
    }

    $textTokens = @(Get-CheckTokens -Text $Text)
    if ($textTokens.Count -eq 0) {
        return $false
    }

    $hits = 0
    foreach ($token in $expectedTokens) {
        if ($textTokens -contains $token) {
            $hits += 1
        }
    }

    $ratio = [double]$hits / [double]$expectedTokens.Count
    return ($hits -ge $MinHits) -or ($ratio -ge $MinRatio)
}

function Get-AnswerGroups {
    param(
        [hashtable]$Expectation,
        [string]$Name
    )

    $groups = @()
    foreach ($rawGroup in @(Get-PlanValue -Plan $Expectation -Name $Name)) {
        $groupValues = @()
        foreach ($value in @($rawGroup)) {
            $normalizedValue = Normalize-CheckText -Text ([string]$value)
            if ($normalizedValue) {
                $groupValues += $normalizedValue
            }
        }
        if (@($groupValues).Count -gt 0) {
            $groups += ,@($groupValues)
        }
    }

    return $groups
}

function Test-ProfileUserSemanticInversion {
    param(
        [string]$Text,
        [string]$ExpectedText
    )

    $normalizedExpected = Normalize-CheckText -Text $ExpectedText
    $normalizedText = Normalize-CheckText -Text $Text
    if ($normalizedExpected -notmatch '\butente chiama\s+(.+?)\s+la\b') {
        return $false
    }

    $aliasTokens = @(Get-CheckTokens -Text $Matches[1])
    if ($aliasTokens.Count -eq 0) {
        return $false
    }

    if ($normalizedText -notmatch '\b(si chiama|ti chiami|mi chiamo)\b') {
        return $false
    }

    $hits = 0
    foreach ($token in $aliasTokens) {
        if ($normalizedText.Contains($token)) {
            $hits += 1
        }
    }

    return $hits -ge [Math]::Min(2, $aliasTokens.Count)
}

function Test-TextContainsAny {
    param(
        [string]$Text,
        [object[]]$Candidates
    )

    $normalizedText = Normalize-CheckText -Text $Text
    foreach ($candidate in @($Candidates)) {
        $needle = Normalize-CheckText -Text ([string]$candidate)
        if ($needle -and $normalizedText.Contains($needle)) {
            return $true
        }
    }

    return $false
}

function Test-ImageSetupResult {
    param([object]$ImageResult)

    if ($null -eq $ImageResult) {
        return $false
    }

    $saved = [bool](Get-ObjectValue -Object $ImageResult -Name 'saved')
    $description = [string](Get-ObjectValue -Object $ImageResult -Name 'description')
    $normalized = Normalize-CheckText -Text $description

    if (-not $saved) {
        return $false
    }

    if ($normalized.Length -lt 24) {
        return $false
    }

    if ($normalized -in @('null', 'none', 'n a', 'nessuna', 'vuoto')) {
        return $false
    }

    return $true
}

function Test-SourceMatch {
    param(
        [object]$Meta,
        [hashtable]$Check
    )

    $expectedSourceType = [string](Get-PlanValue -Plan $Check -Name 'expectedSourceType')
    $expectedSubject = [string](Get-PlanValue -Plan $Check -Name 'expectedSubject')
    $expectedFilenameLike = [string](Get-PlanValue -Plan $Check -Name 'expectedFilenameLike')

    if ($expectedSourceType -and ([string](Get-ObjectValue -Object $Meta -Name 'source_type') -ne $expectedSourceType)) {
        return $false
    }
    if ($expectedSubject -and ([string](Get-ObjectValue -Object $Meta -Name 'memory_subject') -ne $expectedSubject)) {
        return $false
    }
    if ($expectedFilenameLike -and ([string](Get-ObjectValue -Object $Meta -Name 'source_filename') -notlike "*$expectedFilenameLike*")) {
        return $false
    }

    return $true
}

function Test-Expectation {
    param(
        [string]$Mode,
        [object]$Result,
        [hashtable]$Expectation
    )

    $expectedSourceType = [string](Get-PlanValue -Plan $Expectation -Name 'expectedSourceType')
    $expectedSubject = [string](Get-PlanValue -Plan $Expectation -Name 'expectedSubject')
    $expectedFilenameLike = [string](Get-PlanValue -Plan $Expectation -Name 'expectedFilenameLike')
    $expectMemory = [bool](Get-PlanValue -Plan $Expectation -Name 'expectMemory')
    $expectedSourceChecks = @(Get-PlanValue -Plan $Expectation -Name 'expectedSourceChecks')

    if ($expectedSourceChecks.Count -eq 0 -and ($expectedSourceType -or $expectedSubject -or $expectedFilenameLike)) {
        $expectedSourceChecks = @(
            @{
                expectedSourceType = $expectedSourceType
                expectedSubject = $expectedSubject
                expectedFilenameLike = $expectedFilenameLike
            }
        )
    }

    if ($Mode -eq 'chat') {
        $items = @($Result.rag_used)
        if (-not $expectMemory) {
            return ($items.Count -eq 0)
        }

        if ($expectedSourceChecks.Count -eq 0) {
            return ($items.Count -gt 0)
        }

        foreach ($check in $expectedSourceChecks) {
            $matched = $false
            foreach ($item in $items) {
                if (Test-SourceMatch -Meta $item.meta -Check $check) {
                    $matched = $true
                    break
                }
            }
            if (-not $matched) {
                return $false
            }
        }

        return $true
    }

    $docs = @($Result.documents[0])
    $metas = @($Result.metadatas[0])
    if (-not $expectMemory) {
        return ($docs.Count -eq 0)
    }

    if ($expectedSourceChecks.Count -eq 0) {
        return ($docs.Count -gt 0)
    }

    foreach ($check in $expectedSourceChecks) {
        $matched = $false
        for ($i = 0; $i -lt $metas.Count; $i++) {
            if (Test-SourceMatch -Meta $metas[$i] -Check $check) {
                $matched = $true
                break
            }
        }
        if (-not $matched) {
            return $false
        }
    }

    return $true
}

function Test-AnswerExpectation {
    param(
        [object]$ChatResult,
        [hashtable]$Expectation
    )

    if ($null -eq $ChatResult) {
        return $false
    }

    $text = [string](Get-ObjectValue -Object $ChatResult -Name 'text')
    $normalizedText = Normalize-CheckText -Text $text
    if (-not $normalizedText) {
        return $false
    }

    $category = [string](Get-PlanValue -Plan $Expectation -Name 'category')
    $expectMemory = [bool](Get-PlanValue -Plan $Expectation -Name 'expectMemory')
    $expectedAnswerText = [string](Get-PlanValue -Plan $Expectation -Name 'expectedAnswerText')

    $answerForbiddenAny = @()
    foreach ($value in @(Get-PlanValue -Plan $Expectation -Name 'answerForbiddenAny')) {
        $normalizedValue = Normalize-CheckText -Text ([string]$value)
        if ($normalizedValue) {
            $answerForbiddenAny += $normalizedValue
        }
    }

    if (-not $expectMemory) {
        if ($answerForbiddenAny.Count -gt 0 -and (Test-TextContainsAny -Text $normalizedText -Candidates $answerForbiddenAny)) {
            return $false
        }

        $negativeAdmission = Test-TextContainsAny -Text $normalizedText -Candidates @(
            'non ho',
            'non ricordo',
            'non lo so',
            'nessun ricordo',
            'nessuna memoria'
        )
        $noMemorySignal = Test-TextContainsAny -Text $normalizedText -Candidates @(
            'ricordo affidabile',
            'non ho un ricordo affidabile',
            'memorizzo',
            'dimmelo'
        )
        return ($negativeAdmission -or $noMemorySignal)
    }

    $answerRequiredGroups = @(Get-AnswerGroups -Expectation $Expectation -Name 'answerRequiredGroups')
    foreach ($group in $answerRequiredGroups) {
        if (-not (Test-TextContainsAny -Text $normalizedText -Candidates $group)) {
            return $false
        }
    }

    switch ($category) {
        'profilo-avatar' {
            if (-not (Test-ExpectedTextOverlap -Text $text -ExpectedText $expectedAnswerText -MinHits 2 -MinRatio 0.45)) {
                return $false
            }
        }
        'profilo-utente' {
            if (-not (Test-ExpectedTextOverlap -Text $text -ExpectedText $expectedAnswerText -MinHits 2 -MinRatio 0.35)) {
                return $false
            }
            if (Test-ProfileUserSemanticInversion -Text $text -ExpectedText $expectedAnswerText) {
                return $false
            }
        }
        'termine' {
            if (-not (Test-ExpectedTextOverlap -Text $text -ExpectedText $expectedAnswerText -MinHits 3 -MinRatio 0.4)) {
                return $false
            }
        }
    }

    if ($answerForbiddenAny.Count -gt 0 -and (Test-TextContainsAny -Text $normalizedText -Candidates $answerForbiddenAny)) {
        return $false
    }

    return $true
}

function New-QueryPlan {
    param([hashtable]$Config)

    $queries = New-Object System.Collections.Generic.List[object]
    $selfRemember = @($Config.Remembers | Where-Object { $_.kind -eq 'self' } | Select-Object -First 1)
    $userRemember = @($Config.Remembers | Where-Object { $_.kind -eq 'user' } | Select-Object -First 1)

    if ($selfRemember) {
        $queries.Add([ordered]@{
            category = 'profilo-avatar'
            query = 'come ti chiami?'
            expectMemory = $true
            expectedSourceType = 'manual'
            expectedSubject = 'avatar_self'
            expectedAnswerText = [string]$selfRemember[0].text
        })
    }

    if ($userRemember) {
        $queries.Add([ordered]@{
            category = 'profilo-utente'
            query = "cosa sai dell'utente?"
            expectMemory = $true
            expectedSourceType = 'manual'
            expectedSubject = 'user'
            expectedAnswerText = [string]$userRemember[0].text
            answerForbiddenAny = @('mi chiamo')
        })
    }

    foreach ($remember in @($Config.Remembers | Where-Object { $_.kind -eq 'term' })) {
        $queries.Add([ordered]@{
            category = 'termine'
            query = "che cos'e $($remember.term)?"
            expectMemory = $true
            expectedSourceType = 'manual'
            expectedSubject = 'avatar_self'
            expectedAnswerText = [string]$remember.text
        })
    }

    foreach ($image in @($Config.Images)) {
        switch ($image.label) {
            'selfie' {
                $queries.Add([ordered]@{
                    category = 'immagine-selfie'
                    query = 'cosa ricordi della foto allo specchio?'
                    expectMemory = $true
                    expectedSourceType = 'image_description'
                    expectedFilenameLike = 'foto profilo'
                    answerRequiredGroups = @(
                        @('foto', 'specchio', 'immagine', 'persona')
                    )
                })
            }
            'architecture' {
                $queries.Add([ordered]@{
                    category = 'immagine-architettura'
                    query = 'cosa ricordi del diagramma client server?'
                    expectMemory = $true
                    expectedSourceType = 'image_description'
                    expectedFilenameLike = 'architettura_cs'
                    answerRequiredGroups = @(
                        @('diagramma', 'architettura', 'client server', 'client-server'),
                        @('rag', 'proxy', 'http', 'servizio')
                    )
                })
            }
            'sequence' {
                $queries.Add([ordered]@{
                    category = 'immagine-sequenza-http'
                    query = 'cosa ricordi del diagramma http?'
                    expectMemory = $true
                    expectedSourceType = 'image_description'
                    expectedFilenameLike = 'sequenza_http'
                    answerRequiredGroups = @(
                        @('diagramma', 'sequenza', 'http'),
                        @('request', 'response', 'server', 'client')
                    )
                })
            }
            'bose-invoice' {
                $queries.Add([ordered]@{
                    category = 'immagine-bose-checkout'
                    query = 'cosa ricordi della fattura o schermata checkout delle cuffie Bose?'
                    expectMemory = $true
                    expectedSourceType = 'image_description'
                    expectedFilenameLike = 'due.webp'
                    answerRequiredGroups = @(
                        @('bose', 'cuffie'),
                        @('fattura', 'facture', 'checkout', 'ordine', 'schermata', 'prezzo')
                    )
                })
            }
        }
    }

    foreach ($file in @($Config.Files)) {
        switch ($file.label) {
            'aruba-pdf' {
                $queries.Add([ordered]@{
                    category = 'file-aruba'
                    query = 'cosa ricordi di Aruba?'
                    expectMemory = $true
                    expectedSourceType = 'file'
                    expectedFilenameLike = 'conferma_aruba'
                })
            }
            'mouse-pdf' {
                $queries.Add([ordered]@{
                    category = 'file-mouse'
                    query = 'cosa ricordi del mouse?'
                    expectMemory = $true
                    expectedSourceType = 'file'
                    expectedFilenameLike = 'fattura mouse'
                    answerRequiredGroups = @(
                        @('mouse', 'rog', 'harpe'),
                        @('amazon', 'ricevuta', '84', 'luglio')
                    )
                })
            }
            'keyboard-pdf' {
                $queries.Add([ordered]@{
                    category = 'file-keyboard'
                    query = 'cosa ricordi della tastiera?'
                    expectMemory = $true
                    expectedSourceType = 'file'
                    expectedFilenameLike = 'fattura tastiera'
                })
            }
            'confidentiality-pdf' {
                $queries.Add([ordered]@{
                    category = 'file-confidentiality'
                    query = 'cosa ricordi della riservatezza?'
                    expectMemory = $true
                    expectedSourceType = 'file'
                    expectedFilenameLike = 'OBBLIGO DI RISERVATEZZA'
                })
            }
            'nda-pdf' {
                $queries.Add([ordered]@{
                    category = 'file-nda'
                    query = 'cosa ricordi del non disclosure agreement?'
                    expectMemory = $true
                    expectedSourceType = 'file'
                    expectedFilenameLike = 'Non Disclosure Agreement'
                })
            }
            'bose-pdf' {
                $queries.Add([ordered]@{
                    category = 'file-bose'
                    query = 'cosa ricordi di Bose?'
                    expectMemory = $true
                    expectedSourceType = 'file'
                    expectedFilenameLike = 'upsbose2'
                    answerRequiredGroups = @(
                        @('bose', 'ingram', 'pacco', 'spedito'),
                        @('flensburg', 'germania', 'germany', 'elettron', 'elektronisches')
                    )
                })
            }
            'exams-pdf' {
                # Query principiale: voto specifico
                $queries.Add([ordered]@{
                    category = 'file-exams-voto'
                    query = 'che voto ho preso in fisica 1?'
                    expectMemory = $true
                    expectedSourceType = 'file'
                    expectedFilenameLike = 'AutodichiarazioneIscrizioneconesami'
                    answerRequiredGroups = @(
                        @('fisica')
                    )
                })
                # Query sorella: lista esami
                $queries.Add([ordered]@{
                    category = 'file-exams-lista'
                    query = 'quali esami risultano nel documento?'
                    expectMemory = $true
                    expectedSourceType = 'file'
                    expectedFilenameLike = 'AutodichiarazioneIscrizioneconesami'
                    answerRequiredGroups = @(
                        @('esam', 'materia', 'insegnament', 'fisica', 'analisi', 'matematica', 'algebra', 'programma')
                    )
                })
                # Query sorella: recall semantico su singolo esame
                $queries.Add([ordered]@{
                    category = 'file-exams-recall'
                    query = 'cosa ricordi di fisica 1?'
                    expectMemory = $true
                    expectedSourceType = 'file'
                    expectedFilenameLike = 'AutodichiarazioneIscrizioneconesami'
                    answerRequiredGroups = @(
                        @('fisica')
                    )
                })
            }
        }
    }

    $hasUserMemory = [bool]($Config.Remembers | Where-Object { $_.kind -eq 'user' })
    $hasArchitectureImage = [bool]($Config.Images | Where-Object { $_.label -eq 'architecture' })
    $hasMousePdf = [bool]($Config.Files | Where-Object { $_.label -eq 'mouse-pdf' })
    if ($hasUserMemory -and $hasArchitectureImage -and $hasMousePdf) {
        $queries.Add([ordered]@{
            category = 'riepilogo-multi-sorgente'
            query = 'fammi un riepilogo che includa cosa sai dell utente, cosa ricordi del mouse e cosa mostra il diagramma client server.'
            expectMemory = $true
            expectedSourceChecks = @(
                @{ expectedSourceType = 'manual'; expectedSubject = 'user' },
                @{ expectedSourceType = 'file'; expectedFilenameLike = 'fattura mouse' },
                @{ expectedSourceType = 'image_description'; expectedFilenameLike = 'architettura_cs' }
            )
            answerRequiredGroups = @(
                @('utente', 'luca', 'finestr', 'neon', 'vela rame', 'specchio quieto'),
                @('mouse', 'rog', 'harpe', 'amazon', 'ricevuta'),
                @('diagramma', 'client server', 'client-server', 'architettura', 'rag')
            )
        })
    }

    $negativePool = @(
        [ordered]@{
            category = 'negativo-selfie'
            query = 'cosa ricordi della foto allo specchio?'
            expectedSourceType = ''
            expectedFilenameLike = ''
            expectMemory = $false
            answerRequiredGroups = @(
                @('non ricordo', 'non lo so', 'nessuna', 'non ho')
            )
        }
    )

    foreach ($negative in $negativePool) {
        $skip = $false
        if ($negative.category -eq 'negativo-selfie' -and ($Config.Images | Where-Object { $_.label -eq 'selfie' })) { $skip = $true }
        if (-not $skip) {
            $queries.Add($negative)
        }
    }

    return $queries
}

$Configs = @(
    [ordered]@{
        avatar = 'AUTO_EXTREME_01_TEXT'
        label = 'Solo testo: profilo avatar, utente e termine'
        Remembers = @(
            @{ text = 'Mi chiamo Ada Brina e costruisco lanterne pieghevoli.'; kind = 'self' },
            @{ text = 'La parola quarzo lento indica il canale di backup notturno.'; kind = 'term'; term = 'quarzo lento' },
            @{ text = "L'utente detesta il ronzio dei neon e sceglie sempre posti vicini alle finestre."; kind = 'user' }
        )
        Images = @()
        Files = @()
    },
    [ordered]@{
        avatar = 'AUTO_EXTREME_02_IMAGE'
        label = 'Solo immagini: architettura e sequenza HTTP'
        Remembers = @()
        Images = @(
            @{ label = 'architecture'; path = $ArchitecturePath },
            @{ label = 'sequence'; path = $SequencePath }
        )
        Files = @()
    },
    [ordered]@{
        avatar = 'AUTO_EXTREME_03_PDF'
        label = 'Solo PDF: Aruba e mouse'
        Remembers = @()
        Images = @()
        Files = @(
            @{ label = 'aruba-pdf'; path = $ArubaPdfPath },
            @{ label = 'mouse-pdf'; path = $MousePdfPath }
        )
    },
    [ordered]@{
        avatar = 'AUTO_EXTREME_04_TEXT_IMAGE'
        label = 'Solo testo: secondo set profilo'
        Remembers = @(
            @{ text = 'Mi chiamo Elio Serra e archivio negativi fotografici.'; kind = 'self' },
            @{ text = 'Porto ambra identifica il kit fotografico da viaggio.'; kind = 'term'; term = 'porto ambra' }
        )
        Images = @()
        Files = @()
    },
    [ordered]@{
        avatar = 'AUTO_EXTREME_05_TEXT_PDF'
        label = 'Misto: testo utente + PDF mouse + diagramma'
        Remembers = @(
            @{ text = 'Mi chiamo Marta Rugiada e verifico due volte i pagamenti.'; kind = 'self' },
            @{ text = "L'utente si chiama Luca e preferisce lavorare vicino alle finestre."; kind = 'user' },
            @{ text = 'Ritorno opale segnala una verifica amministrativa completata.'; kind = 'term'; term = 'ritorno opale' }
        )
        Images = @(
            @{ label = 'architecture'; path = $ArchitecturePath }
        )
        Files = @(
            @{ label = 'mouse-pdf'; path = $MousePdfPath }
        )
    },
    [ordered]@{
        avatar = 'AUTO_EXTREME_06_IMAGE_PDF'
        label = 'Solo PDF: Aruba e Bose'
        Remembers = @()
        Images = @()
        Files = @(
            @{ label = 'aruba-pdf'; path = $ArubaPdfPath },
            @{ label = 'bose-pdf'; path = $BosePdfPath }
        )
    },
    [ordered]@{
        avatar = 'AUTO_EXTREME_07_MIX'
        label = 'Testo + PDF Aruba e riservatezza'
        Remembers = @(
            @{ text = 'Mi chiamo Serena Valli e prendo appunti solo con penne blu scuro.'; kind = 'self' },
            @{ text = "L'utente usa il termine vela rame per indicare il piano B."; kind = 'user' },
            @{ text = 'La chiave bosco ambrato indica il contenitore di emergenza.'; kind = 'term'; term = 'bosco ambrato' }
        )
        Images = @()
        Files = @(
            @{ label = 'aruba-pdf'; path = $ArubaPdfPath },
            @{ label = 'confidentiality-pdf'; path = $ConfidentialityPdfPath }
        )
    },
    [ordered]@{
        avatar = 'AUTO_EXTREME_08_COLLISION'
        label = 'Collisione Bose: immagine checkout + PDF corretto'
        Remembers = @(
            @{ text = 'Mi chiamo Dario Lume e colleziono scontrini di viaggi.'; kind = 'self' },
            @{ text = 'Il codice Aruba grigio indica il raccoglitore cartaceo, non il provider.'; kind = 'term'; term = 'Aruba grigio' },
            @{ text = 'Bose rame indica le cuffie di riferimento per i test audio interni.'; kind = 'term'; term = 'Bose rame' },
            @{ text = "L'utente chiama specchio quieto la postazione vicino alla finestra."; kind = 'user' }
        )
        Images = @(
            @{ label = 'bose-invoice'; path = $InvoicePath }
        )
        Files = @(
            @{ label = 'bose-pdf'; path = $BosePdfPath },
            @{ label = 'confidentiality-pdf'; path = $ConfidentialityPdfPath }
        )
    },
    # Avatar dedicato al PDF autocertificazione esami universitari
    [ordered]@{
        avatar = 'AUTO_EXTREME_09_EXAMS_PDF'
        label = 'PDF autocertificazione esami universitari'
        Remembers = @(
            @{ text = 'Mi chiamo Giulia Vento e studio ingegneria informatica.'; kind = 'self' },
            @{ text = "L'utente vuole verificare che il sistema legga correttamente i documenti accademici."; kind = 'user' }
        )
        Images = @()
        Files = @(
            @{ label = 'exams-pdf'; path = $ExamsPdfPath }
        )
    }
)

$Configs = @(
    $Configs | Where-Object {
        $_.avatar -in @(
            'AUTO_EXTREME_01_TEXT',
            'AUTO_EXTREME_02_IMAGE',
            'AUTO_EXTREME_05_TEXT_PDF',
            'AUTO_EXTREME_08_COLLISION',
            'AUTO_EXTREME_09_EXAMS_PDF'
        )
    }
)

$builder = New-Object System.Text.StringBuilder
$results = New-Object System.Collections.Generic.List[object]
$setupRows = New-Object System.Collections.Generic.List[object]
$cleanupRows = New-Object System.Collections.Generic.List[object]

$health = Invoke-RestMethod -Uri "$BaseUrl/health"

[void]$builder.AppendLine('# SOULFRAME Report stress test estremo multi-avatar')
[void]$builder.AppendLine()
[void]$builder.AppendLine("Generato: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
[void]$builder.AppendLine()
[void]$builder.AppendLine('## Ambiente')
[void]$builder.AppendLine()
[void]$builder.AppendLine("- URL backend: $BaseUrl")
[void]$builder.AppendLine("- Modello chat: $($health.chat_model)")
[void]$builder.AppendLine("- Modello embedding: $($health.embed_model)")
[void]$builder.AppendLine("- OCR attivo: $($health.ocr)")
[void]$builder.AppendLine("- Gemini Vision attivo: $($health.gemini_vision)")
[void]$builder.AppendLine("- Percorso report: $ReportPath")
[void]$builder.AppendLine('- PDF extra: conferma_aruba.pdf, fattura mouse.pdf, OBBLIGO DI RISERVATEZZA.pdf, upsbose2.pdf, AutodichiarazioneIscrizioneconesami.pdf')
[void]$builder.AppendLine()

try {
    foreach ($config in $Configs) {
        $avatarId = [string]$config.avatar
        Write-Output ("[AVATAR] " + $avatarId + " :: " + $config.label)

        try {
            Clear-Avatar -AvatarId $avatarId
        }
        catch {
            [void]$setupRows.Add([ordered]@{
                avatar = $avatarId
                kind = 'errore-setup'
                label = 'clear-avatar'
                ok = $false
                detail = [string]$_.Exception.Message
            })
        }

        foreach ($remember in @($config.Remembers)) {
            try {
                Remember-Text -AvatarId $avatarId -Text ([string]$remember.text)
            }
            catch {
                [void]$setupRows.Add([ordered]@{
                    avatar = $avatarId
                    kind = 'memoria'
                    label = [string]$remember.kind
                    ok = $false
                    detail = [string]$_.Exception.Message
                })
            }
        }

        foreach ($image in @($config.Images)) {
            Write-Output ("[SETUP][IMAGE] " + $avatarId + " :: " + $image.label)
            try {
                $imageResult = Describe-Image -AvatarId $avatarId -Path ([string]$image.path)
                $imageOk = Test-ImageSetupResult -ImageResult $imageResult
                $imageDescription = [string](Get-ObjectValue -Object $imageResult -Name 'description')
                $imagePreview = $imageDescription.Replace("`r", ' ').Replace("`n", ' ')
                if ($imagePreview.Length -gt 140) { $imagePreview = $imagePreview.Substring(0, 140) + '...' }
                [void]$setupRows.Add([ordered]@{
                    avatar = $avatarId
                    kind = 'immagine'
                    label = $image.label
                    ok = $imageOk
                    detail = "saved=$([bool](Get-ObjectValue -Object $imageResult -Name 'saved')); chars=$($imageDescription.Length); preview=$imagePreview"
                })
            }
            catch {
                [void]$setupRows.Add([ordered]@{
                    avatar = $avatarId
                    kind = 'immagine'
                    label = $image.label
                    ok = $false
                    detail = [string]$_.Exception.Message
                })
            }
        }

        foreach ($file in @($config.Files)) {
            Write-Output ("[SETUP][FILE] " + $avatarId + " :: " + $file.label)
            try {
                $fileResult = Ingest-File -AvatarId $avatarId -Path ([string]$file.path)
                [void]$setupRows.Add([ordered]@{
                    avatar = $avatarId
                    kind = 'file'
                    label = $file.label
                    ok = [bool]($fileResult -and $fileResult.ok)
                    detail = "chunks_added=$($fileResult.chunks_added)"
                })
            }
            catch {
                [void]$setupRows.Add([ordered]@{
                    avatar = $avatarId
                    kind = 'file'
                    label = $file.label
                    ok = $false
                    detail = [string]$_.Exception.Message
                })
            }
        }

        try {
            $stats = Get-AvatarStats -AvatarId $avatarId
        }
        catch {
            $stats = @{ count = -1 }
            [void]$setupRows.Add([ordered]@{
                avatar = $avatarId
                kind = 'statistiche'
                label = $config.label
                ok = $false
                detail = [string]$_.Exception.Message
            })
        }
        [void]$setupRows.Add([ordered]@{
            avatar = $avatarId
            kind = 'statistiche'
            label = $config.label
            ok = ($stats.count -ge 0)
            detail = "count=$($stats.count)"
        })

        $sessionId = ''
        try {
            $sessionResult = Start-ChatSession -AvatarId $avatarId
            $sessionId = [string]$sessionResult.session_id
        }
        catch {
            [void]$setupRows.Add([ordered]@{ avatar = $avatarId; kind = 'sessione'; label = 'session-start'; ok = $false; detail = [string]$_.Exception.Message })
        }

        foreach ($plan in @(New-QueryPlan -Config $config)) {
            Write-Output ("[QUERY] " + $avatarId + " :: " + $plan.category + " :: " + $plan.query)
            $chat = $null
            $recall = $null
            $chatError = ''
            $recallError = ''

            try {
                $chat = Invoke-ChatQuery -AvatarId $avatarId -Query ([string]$plan.query) -SessionId $sessionId
            }
            catch {
                $chatError = [string]$_.Exception.Message
            }

            try {
                $recall = Invoke-RecallQuery -AvatarId $avatarId -Query ([string]$plan.query)
            }
            catch {
                $recallError = [string]$_.Exception.Message
            }

            $chatRetrievalPass = $false
            if (-not $chatError -and $chat) {
                $chatRetrievalPass = Test-Expectation -Mode 'chat' -Result $chat -Expectation $plan
            }

            $recallRetrievalPass = $false
            if (-not $recallError -and $recall) {
                $recallRetrievalPass = Test-Expectation -Mode 'recall' -Result $recall -Expectation $plan
            }

            $retrievalPass = [bool]($chatRetrievalPass -and $recallRetrievalPass)
            $answerPass = $false
            if (-not $chatError -and $chat) {
                $answerPass = Test-AnswerExpectation -ChatResult $chat -Expectation $plan
            }
            $fullPass = [bool]($retrievalPass -and $answerPass)

            $firstRecallDoc = ''
            if ($recallError) {
                $firstRecallDoc = 'ERROR: ' + $recallError
            }
            elseif ($recall -and @($recall.documents[0]).Count -gt 0) {
                $firstRecallDoc = [string]@($recall.documents[0])[0]
            }

            [void]$results.Add([ordered]@{
                avatar = $avatarId
                label = $config.label
                category = $plan.category
                query = $plan.query
                expectMemory = [bool]$plan.expectMemory
                expectedSourceType = [string](Get-PlanValue -Plan $plan -Name 'expectedSourceType')
                expectedSubject = [string](Get-PlanValue -Plan $plan -Name 'expectedSubject')
                expectedFilenameLike = [string](Get-PlanValue -Plan $plan -Name 'expectedFilenameLike')
                chatRetrievalPass = [bool]$chatRetrievalPass
                chatIntent = $(if ($chatError) { 'error' } elseif ($chat) { [string]$chat.intent } else { '' })
                chatRag = $(if ($chatError) { 'ERROR: ' + $chatError } elseif ($chat) { Get-RagSummary -RagUsed $chat.rag_used } else { '' })
                chatText = $(if ($chatError) { 'ERROR: ' + $chatError } elseif ($chat) { [string]$chat.text } else { '' })
                recallRetrievalPass = [bool]$recallRetrievalPass
                recallCount = $(if ($recallError -or -not $recall) { 0 } else { @($recall.documents[0]).Count })
                recallSources = $(if ($recallError) { 'ERROR: ' + $recallError } elseif ($recall) { Get-RecallSummary -Recall $recall } else { '' })
                recallFirstDoc = $firstRecallDoc
                retrievalPass = [bool]$retrievalPass
                answerPass = [bool]$answerPass
                fullPass = [bool]$fullPass
            })
        }
    }
}
finally {
    if (-not $KeepAvatar) {
        foreach ($config in $Configs) {
            $avatarId = [string]$config.avatar
            try {
                Clear-Avatar -AvatarId $avatarId
                [void]$cleanupRows.Add([ordered]@{ avatar = $avatarId; cleared = $true })
            }
            catch {
                [void]$cleanupRows.Add([ordered]@{ avatar = $avatarId; cleared = $false })
            }
        }
    }
    else {
        foreach ($config in $Configs) {
            [void]$cleanupRows.Add([ordered]@{ avatar = [string]$config.avatar; cleared = 'skipped by -KeepAvatar' })
        }
    }
}

$totalQueries = $results.Count
$chatRetrievalPassed = @($results | Where-Object { $_.chatRetrievalPass }).Count
$recallRetrievalPassed = @($results | Where-Object { $_.recallRetrievalPass }).Count
$retrievalPassed = @($results | Where-Object { $_.retrievalPass }).Count
$answerPassed = @($results | Where-Object { $_.answerPass }).Count
$fullPassed = @($results | Where-Object { $_.fullPass }).Count
$partialFail = @($results | Where-Object { -not $_.fullPass }).Count

[void]$builder.AppendLine('## Sintesi esecutiva')
[void]$builder.AppendLine()
[void]$builder.AppendLine("- Avatar testati: $($Configs.Count)")
[void]$builder.AppendLine("- Query generate: $totalQueries")
[void]$builder.AppendLine("- Retrieval chat passati: $chatRetrievalPassed / $totalQueries")
[void]$builder.AppendLine("- Retrieval recall passati: $recallRetrievalPassed / $totalQueries")
[void]$builder.AppendLine("- Retrieval complessivi passati: $retrievalPassed / $totalQueries")
[void]$builder.AppendLine("- Qualita risposta passata: $answerPassed / $totalQueries")
[void]$builder.AppendLine("- Full pass (retrieval + risposta): $fullPassed / $totalQueries")
[void]$builder.AppendLine("- Fallimenti parziali o completi: $partialFail")
[void]$builder.AppendLine()

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

[void]$builder.AppendLine('## Matrice query')
[void]$builder.AppendLine()
[void]$builder.AppendLine('| Avatar | Categoria | Query | Retrieval | Risposta | Esito | Sorgenti chat |')
[void]$builder.AppendLine('| --- | --- | --- | --- | --- | --- | --- |')
foreach ($result in $results) {
    $retrievalMark = if ($result.retrievalPass) { 'PASS' } else { 'FAIL' }
    $answerMark = if ($result.answerPass) { 'PASS' } else { 'FAIL' }
    $fullMark = if ($result.fullPass) { 'PASS' } else { 'FAIL' }
    $chatSources = ([string]$result.chatRag).Replace('|', ',')
    [void]$builder.AppendLine("| $($result.avatar) | $($result.category) | $($result.query) | $retrievalMark | $answerMark | $fullMark | $chatSources |")
}
[void]$builder.AppendLine()

[void]$builder.AppendLine('## Risultati dettagliati per avatar')
[void]$builder.AppendLine()
foreach ($config in $Configs) {
    $avatarId = [string]$config.avatar
    $avatarResults = @($results | Where-Object { $_.avatar -eq $avatarId })
    $avatarRetrievalPass = @($avatarResults | Where-Object { $_.retrievalPass }).Count
    $avatarAnswerPass = @($avatarResults | Where-Object { $_.answerPass }).Count
    $avatarFullPass = @($avatarResults | Where-Object { $_.fullPass }).Count

    [void]$builder.AppendLine("### $avatarId")
    [void]$builder.AppendLine()
    [void]$builder.AppendLine("- Etichetta: $($config.label)")
    [void]$builder.AppendLine("- Query eseguite: $($avatarResults.Count)")
    [void]$builder.AppendLine("- Retrieval passati: $avatarRetrievalPass / $($avatarResults.Count)")
    [void]$builder.AppendLine("- Qualita risposta passata: $avatarAnswerPass / $($avatarResults.Count)")
    [void]$builder.AppendLine("- Full pass: $avatarFullPass / $($avatarResults.Count)")
    [void]$builder.AppendLine()

    foreach ($result in $avatarResults) {
        [void]$builder.AppendLine("#### Query: $($result.query)")
        [void]$builder.AppendLine()
        [void]$builder.AppendLine("- Categoria: $($result.category)")
        [void]$builder.AppendLine("- Memoria attesa: $($result.expectMemory)")
        [void]$builder.AppendLine("- Tipo sorgente atteso: $($result.expectedSourceType)")
        [void]$builder.AppendLine("- Soggetto atteso: $($result.expectedSubject)")
        [void]$builder.AppendLine("- Filename atteso: $($result.expectedFilenameLike)")
        [void]$builder.AppendLine("- Retrieval chat: $($result.chatRetrievalPass)")
        [void]$builder.AppendLine("- Intent chat: $($result.chatIntent)")
        [void]$builder.AppendLine("- Sorgenti chat: $($result.chatRag)")
        [void]$builder.AppendLine("- Qualita risposta: $($result.answerPass)")
        [void]$builder.AppendLine("- Esito finale: $($result.fullPass)")
        [void]$builder.AppendLine()
        [void]$builder.AppendLine('Risposta chat:')
        [void]$builder.AppendLine()
        [void]$builder.AppendLine('```text')
        [void]$builder.AppendLine([string]$result.chatText)
        [void]$builder.AppendLine('```')
        [void]$builder.AppendLine()
    }
}

$failures = @($results | Where-Object { -not $_.fullPass })
[void]$builder.AppendLine('## Focus sui fallimenti')
[void]$builder.AppendLine()
if ($failures.Count -eq 0) {
    [void]$builder.AppendLine('- Nessun caso fallito rilevato in questa batteria.')
}
else {
    foreach ($failure in $failures) {
        $failureAvatar = [string](Get-ObjectValue -Object $failure -Name 'avatar')
        $failureQuery = [string](Get-ObjectValue -Object $failure -Name 'query')
        $failureRetrieval = [string](Get-ObjectValue -Object $failure -Name 'retrievalPass')
        $failureAnswer = [string](Get-ObjectValue -Object $failure -Name 'answerPass')
        $failureChatSources = [string](Get-ObjectValue -Object $failure -Name 'chatRag')
        [void]$builder.AppendLine("- Avatar $failureAvatar | query '$failureQuery' | retrieval=$failureRetrieval | risposta=$failureAnswer | sorgenti chat=$failureChatSources")
    }
}
[void]$builder.AppendLine()

[void]$builder.AppendLine('## Sintesi cleanup')
[void]$builder.AppendLine()
[void]$builder.AppendLine('| Avatar | Pulito |')
[void]$builder.AppendLine('| --- | --- |')
foreach ($row in $cleanupRows) {
    [void]$builder.AppendLine("| $($row.avatar) | $($row.cleared) |")
}
[void]$builder.AppendLine()

[void]$builder.AppendLine('## Note')
[void]$builder.AppendLine()
[void]$builder.AppendLine('- Il report e stato generato automaticamente contro il backend debug.')
[void]$builder.AppendLine('- Ogni query e stata eseguita sia via chat sia via recall.')
[void]$builder.AppendLine('- Tutti gli avatar temporanei usati dalla batteria sono stati rimossi nel cleanup finale.')

[System.IO.File]::WriteAllText($ReportPath, $builder.ToString(), (New-Object System.Text.UTF8Encoding $false))
Write-Output "REPORT_PATH=$ReportPath"
Write-Output "TOTAL_QUERIES=$totalQueries"
Write-Output "CHAT_RETRIEVAL_PASSED=$chatRetrievalPassed"
Write-Output "RECALL_RETRIEVAL_PASSED=$recallRetrievalPassed"
Write-Output "RETRIEVAL_PASSED=$retrievalPassed"
Write-Output "ANSWER_PASSED=$answerPassed"
Write-Output "FULL_PASS=$fullPassed"