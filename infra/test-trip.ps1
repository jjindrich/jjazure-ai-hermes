param(
    [Parameter(Mandatory)][uri]$BaseUrl,
    [string]$RuntimeFile = (Join-Path (Split-Path $PSScriptRoot -Parent) 'config-secret\runtime.json'),
    [guid]$ExistingJobId = [guid]::Empty,
    [string]$RequestText = 'Naplánuj jednodenní výlet 7. listopadu 2026 z Prahy do Kutné Hory a zpět vlakem pro dva dospělé, odjezd po 8:00, návrat do 20:00, rozpočet 3000 Kč. Historické památky, krátká procházka a oběd. Ověř počasí; neověřené údaje označ. Nic nerezervuj.'
)

$ErrorActionPreference = 'Stop'
if ($BaseUrl.Scheme -ne 'https' -or $BaseUrl.UserInfo) {
    throw 'The deployed intake must use HTTPS without URL credentials.'
}
$runtime = Get-Content -LiteralPath $RuntimeFile -Raw | ConvertFrom-Json
$headers = @{ Authorization = "Bearer $($runtime.TRIP_API_KEY)" }
$root = $BaseUrl.AbsoluteUri.TrimEnd('/')
$health = Invoke-RestMethod "$root/healthz" -TimeoutSec 30
if ($health.status -ne 'ok') { throw 'Health check failed.' }
$page = Invoke-WebRequest $root -TimeoutSec 30
if ($page.Content -notmatch 'textarea id="request-text"') {
    throw 'The trip form was not served.'
}
try {
    Invoke-WebRequest "$root/api/trips" -Method Post -ContentType 'application/json' `
        -Body '{"requestText":"Unauthorized smoke test"}' -TimeoutSec 30 | Out-Null
    throw 'The API accepted an unauthenticated request.'
} catch {
    if ([int]$_.Exception.Response.StatusCode -ne 401) { throw }
}
if ($ExistingJobId -ne [guid]::Empty) {
    $job = @{ id = $ExistingJobId.ToString() }
} else {
    $body = [Text.Encoding]::UTF8.GetBytes(
        (@{ requestText = $RequestText } | ConvertTo-Json -Compress)
    )
    $job = Invoke-RestMethod "$root/api/trips" -Method Post -Headers $headers `
        -ContentType 'application/json; charset=utf-8' -Body $body -TimeoutSec 30
    if ($job.status -ne 'queued' -or $job.id -notmatch '^[a-f0-9-]{36}$') {
        throw 'The API did not accept a queued trip job.'
    }
}
Write-Output "Validating trip job: $($job.id)"
$deadline = [DateTime]::UtcNow.AddMinutes(25)
do {
    Start-Sleep -Seconds 5
    $result = Invoke-RestMethod "$root/api/trips/$($job.id)" -Headers $headers -TimeoutSec 30
    if ($result.status -in @('failed', 'expired')) {
        throw "Trip job $($job.id) finished as $($result.status): $($result.error)"
    }
    if ([DateTime]::UtcNow -gt $deadline) {
        throw "Trip job $($job.id) exceeded the validation deadline."
    }
} while ($result.status -ne 'completed')
if ($result.result.Length -lt 1000 -or $result.result -notmatch 'Jak Hermes koordinoval agenty') {
    throw 'The completed response does not contain the expected trip itinerary.'
}
[pscustomobject]@{
    endpoint = $root
    jobId = $job.id
    status = $result.status
    resultCharacters = $result.result.Length
    authenticatedApi = $true
    unauthorizedRejected = $true
    checkedAt = [DateTime]::UtcNow.ToString('o')
} | ConvertTo-Json
