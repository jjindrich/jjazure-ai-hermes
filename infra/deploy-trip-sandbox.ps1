param(
    [Parameter(Mandatory)][guid]$DiskId,
    [string]$RuntimeFile = (Join-Path (Split-Path $PSScriptRoot -Parent) 'config-secret\runtime.json'),
    [string]$VnetConnection = 'jjsandbox-trip',
    [string]$StorageAccount = 'tripfiv6psfk5vqje'
)

$ErrorActionPreference = 'Stop'
$runtime = Get-Content -LiteralPath $RuntimeFile -Raw | ConvertFrom-Json
foreach ($name in @('COPILOT_GITHUB_TOKEN', 'TRIP_API_KEY')) {
    if ($runtime.$name -isnot [string] -or $runtime.$name.Length -lt 32) {
        throw "Missing or invalid deployment setting: $name"
    }
}
try {
    # Keep deployment credentials in memory, never in argv, files or console output.
    $manifest = @{
        sourcesRef = @{ diskImage = @{ id = $DiskId.ToString() } }
        entrypoint = @('/usr/share/dotnet/dotnet', '/app/Trip.dll')
        cmd = @()
        customerVnetConnectionName = $VnetConnection
        resources = @{ cpu = '1000m'; memory = '2048Mi' }
        environment = @{
            AZURE_STORAGE_ACCOUNT_NAME = $StorageAccount
            TRIP_API_KEY = $runtime.TRIP_API_KEY
            TRIP_COPILOT_TOKEN = $runtime.COPILOT_GITHUB_TOKEN
        }
        labels = @{ app = 'hermes-trip'; runtime = 'dotnet'; network = $VnetConnection }
        lifecycle = @{ autoSuspendPolicy = @{ enabled = $false; interval = 3600 } }
    }
    $accessToken = az account get-access-token --subscription 80214fc0-7aff-43b6-bbff-cf81a36cbb10 `
        --scope https://dynamicsessions.io/.default --query accessToken -o tsv
    if ($LASTEXITCODE -ne 0) { throw 'Azure authentication failed.' }
    $headers = @{ Authorization = ('Bearer ' + $accessToken.Trim()) }
    $endpoint = 'https://management.swedencentral.azuredevcompute.io/subscriptions/80214fc0-7aff-43b6-bbff-cf81a36cbb10/resourceGroups/rg-jjsandbox/sandboxGroups/jjsandbox-gr1/sandboxes'
    $response = Invoke-RestMethod "$endpoint`?api-version=2026-02-01-preview" `
        -Method Put -Headers $headers -ContentType 'application/json' `
        -Body ($manifest | ConvertTo-Json -Depth 8 -Compress) -TimeoutSec 120
    if (-not $response.id) { throw 'Sandbox create returned no instance ID.' }
    $id = [guid]$response.id
    $deadline = [DateTime]::UtcNow.AddMinutes(5)
    do {
        $state = Invoke-RestMethod "$endpoint/$id`?api-version=2026-02-01-preview" `
            -Headers $headers -TimeoutSec 30
        if ($state.state -eq 'Running') {
            Write-Output "Created sandbox: $id"
            return
        }
        if ($state.state -in @('Failed', 'Deleted') -or [DateTime]::UtcNow -gt $deadline) {
            throw "Sandbox $id failed readiness: $($state.state)"
        }
        Start-Sleep -Seconds 5
    } while ($true)
} finally {
    $runtime = $null
    $manifest = $null
    $headers = $null
    $accessToken = $null
}
