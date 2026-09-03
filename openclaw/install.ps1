$ErrorActionPreference = "Stop"
$repoRoot = Split-Path $PSScriptRoot -Parent
$runtimeRoot = Join-Path $repoRoot ".openclaw-runtime"
$openClaw = Join-Path $runtimeRoot "openclaw.mjs"
$builtEntry = Join-Path $runtimeRoot "dist\entry.js"
$expectedVersion = "2026.6.34"
$expectedCommit = "5c38f996d4059ebd9080cf74dc611ec3a17f4d50"

if (Test-Path $runtimeRoot) {
    $actualCommit = git -C $runtimeRoot rev-parse HEAD
    if ($LASTEXITCODE -ne 0 -or $actualCommit -ne $expectedCommit) {
        throw "Unexpected OpenClaw checkout at $runtimeRoot. Remove it manually and retry."
    }

    if (Test-Path $builtEntry) {
        Write-Host "OpenClaw $expectedVersion is already installed." -ForegroundColor Green
        exit 0
    }
} else {
    git clone `
        --depth 1 `
        --branch "v$expectedVersion" `
        https://github.com/openclaw/openclaw.git `
        $runtimeRoot
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to clone OpenClaw v$expectedVersion."
    }

    $actualCommit = git -C $runtimeRoot rev-parse HEAD
    if ($LASTEXITCODE -ne 0 -or $actualCommit -ne $expectedCommit) {
        throw "OpenClaw checkout does not match the expected commit."
    }
}

Push-Location $runtimeRoot
try {
    pnpm install --frozen-lockfile
    if ($LASTEXITCODE -ne 0) {
        throw "OpenClaw dependency installation failed."
    }

    pnpm build
    if ($LASTEXITCODE -ne 0) {
        throw "OpenClaw build failed."
    }
} finally {
    Pop-Location
}

Write-Host "OpenClaw $expectedVersion installed at $runtimeRoot" -ForegroundColor Green
