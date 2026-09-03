$ErrorActionPreference = "Stop"
$repoRoot = Split-Path $PSScriptRoot -Parent
$hermes = (Get-Command hermes -ErrorAction SilentlyContinue).Source

if (-not $hermes) {
    $hermes = Join-Path $env:LOCALAPPDATA "hermes\hermes-agent\.venv\Scripts\hermes.exe"
}

if (-not (Test-Path $hermes)) {
    throw "Hermes CLI was not found. Install Hermes Agent first."
}

$authStatus = (& $hermes auth status copilot 2>&1 | Out-String)
if ($authStatus -notmatch "logged in") {
    throw "GitHub Copilot is not authenticated. Run 'hermes model' and select GitHub Copilot."
}

& $hermes config set model.default "gpt-5.4-mini"
& $hermes config set model.provider "copilot"
& $hermes config unset model.base_url
& $hermes config unset model.context_length
& $hermes config unset model.ollama_num_ctx
& $hermes config set delegation.max_concurrent_children 3
& $hermes config set delegation.max_spawn_depth 2
& $hermes config set delegation.max_iterations 20
& $hermes config set agent.max_turns 50

Push-Location $repoRoot
try {
    if (-not (Test-Path ".\data\travel-request.txt")) {
        throw "Missing input file: data\travel-request.txt"
    }

    New-Item -ItemType Directory -Path ".\hermes\artifacts" -Force | Out-Null
    Set-Clipboard (Get-Content ".\hermes\prompts\travel-demo.txt" -Raw)
    $originalTitle = $Host.UI.RawUI.WindowTitle
    $Host.UI.RawUI.WindowTitle = "Hermes demo: press Ctrl+V and Enter"

    Write-Host ""
    Write-Host "============================================================" -ForegroundColor Cyan
    Write-Host " READY: the travel assignment is copied to the clipboard." -ForegroundColor Green
    Write-Host ""
    Write-Host " Hermes cannot submit an interactive prompt automatically."
    Write-Host " After Hermes opens:"
    Write-Host "   1. Press Ctrl+V" -ForegroundColor Yellow
    Write-Host "   2. Press Enter" -ForegroundColor Yellow
    Write-Host ""
    Write-Host " Keep the Hermes session open while the four agents work."
    Write-Host "============================================================" -ForegroundColor Cyan
    Write-Host ""
    Read-Host "Press Enter now to open Hermes"

    try {
        & $hermes chat `
            --in $repoRoot `
            --provider copilot `
            --model "gpt-5.4-mini" `
            --toolsets "file,web,delegation" `
            --max-turns 50 `
            --cli
    } finally {
        $Host.UI.RawUI.WindowTitle = $originalTitle
    }
} finally {
    Pop-Location
}
