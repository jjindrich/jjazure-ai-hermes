param(
    [switch]$SkipInstall,

    [ValidateSet("Quiet", "Terminal", "Tui", "Browser")]
    [string]$Monitor = "Tui",

    [ValidateSet("info", "debug", "trace")]
    [string]$LogLevel = "info"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path $PSScriptRoot -Parent
$openClaw = Join-Path $repoRoot ".openclaw-runtime\openclaw.mjs"
$config = Join-Path $PSScriptRoot "openclaw.json"
$prompt = Join-Path $PSScriptRoot "prompts\travel-demo.txt"
$output = Join-Path $PSScriptRoot "artifacts\weekend-plan.md"
$runResult = Join-Path $PSScriptRoot "artifacts\run-result.json"
$stateDir = Join-Path $PSScriptRoot ".state\$([guid]::NewGuid().ToString('N'))"
$gatewayLog = Join-Path $PSScriptRoot "artifacts\gateway.log"
$gatewayErrorLog = Join-Path $PSScriptRoot "artifacts\gateway-error.log"
$agentErrorLog = Join-Path $PSScriptRoot "artifacts\agent-error.log"
$agentResponse = Join-Path $PSScriptRoot "artifacts\agent-response.json"
$gatewayProcess = $null
$agentProcess = $null

Push-Location $repoRoot
try {
    if (-not (Test-Path ".\data\travel-request.txt")) {
        throw "Missing input file: data\travel-request.txt"
    }

    if (-not (Test-Path $openClaw)) {
        if ($SkipInstall) {
            throw "OpenClaw is not installed. Run '.\openclaw\install.ps1' first."
        }

        & (Join-Path $PSScriptRoot "install.ps1")
        if ($LASTEXITCODE -ne 0) {
            throw "OpenClaw installation failed."
        }
    }

    New-Item -ItemType Directory -Path ".\openclaw\artifacts", $stateDir -Force | Out-Null
    Remove-Item `
        $output, `
        $runResult, `
        $gatewayLog, `
        $gatewayErrorLog, `
        $agentErrorLog, `
        $agentResponse `
        -Force `
        -ErrorAction SilentlyContinue

    $env:OPENCLAW_CONFIG_PATH = $config
    $env:OPENCLAW_STATE_DIR = $stateDir
    $env:OPENCLAW_WORKSPACE_DIR = $repoRoot
    $env:OPENCLAW_GATEWAY_TOKEN = [guid]::NewGuid().ToString("N")

    $listener = [System.Net.Sockets.TcpListener]::new(
        [System.Net.IPAddress]::Loopback,
        0
    )
    $listener.Start()
    $gatewayPort = $listener.LocalEndpoint.Port
    $listener.Stop()
    $env:OPENCLAW_GATEWAY_PORT = [string]$gatewayPort

    $gatewayArguments = @(
        $openClaw,
        "--log-level",
        $LogLevel,
        "gateway",
        "run",
        "--port",
        $gatewayPort,
        "--bind",
        "loopback",
        "--auth",
        "token"
    )
    $gatewayOptions = @{
        FilePath = (Get-Command node).Source
        ArgumentList = $gatewayArguments
        NoNewWindow = $true
        PassThru = $true
    }

    if ($Monitor -ne "Terminal") {
        $gatewayOptions.RedirectStandardOutput = $gatewayLog
        $gatewayOptions.RedirectStandardError = $gatewayErrorLog
    }

    Write-Host "Starting OpenClaw Gateway..." -ForegroundColor Cyan
    Write-Host "Monitor: $Monitor | Log level: $LogLevel"

    $gatewayProcess = Start-Process @gatewayOptions

    $gatewayReady = $false
    for ($attempt = 0; $attempt -lt 240; $attempt++) {
        Start-Sleep -Milliseconds 500
        $probe = [System.Net.Sockets.TcpClient]::new()
        try {
            $connection = $probe.ConnectAsync(
                [System.Net.IPAddress]::Loopback,
                $gatewayPort
            )
            if ($connection.Wait(250) -and $probe.Connected) {
                $gatewayReady = $true
                break
            }
        } catch {
            # Gateway is still starting.
        } finally {
            $probe.Dispose()
        }

        if (($attempt + 1) % 10 -eq 0) {
            $elapsed = [math]::Round(($attempt + 1) / 2)
            Write-Host "  Still starting Gateway... ${elapsed}s"
        }

        if ($gatewayProcess.HasExited) {
            break
        }
    }

    if (-not $gatewayReady) {
        if ($Monitor -ne "Terminal" -and (Test-Path $gatewayErrorLog)) {
            Get-Content $gatewayErrorLog -Tail 20 | Write-Host
        }

        throw "OpenClaw Gateway did not become healthy. See openclaw\artifacts\gateway-error.log."
    }

    Write-Host "Gateway port is ready; connecting monitor..." -ForegroundColor Cyan
    Start-Sleep -Seconds 2

    $gatewayUrl = "ws://127.0.0.1:$gatewayPort"
    Write-Host "OpenClaw Gateway: $gatewayUrl" -ForegroundColor Cyan
    Write-Host "State directory: $stateDir"

    switch ($Monitor) {
        "Terminal" {
            Write-Host "Streaming Gateway events in this terminal (log level: $LogLevel)." -ForegroundColor Green
        }
        "Tui" {
            Write-Host ""
            Write-Host "Starting OpenClaw TUI in this terminal." -ForegroundColor Green
            Write-Host "The first task can take about 60 seconds to appear while Copilot initializes."
            Write-Host "Watch the main session and use /subagents list for worker status."
            Write-Host "Use /exit or Ctrl+D to leave; processing continues until the plan is ready."
            Write-Host ""
            $agentProcess = Start-Process `
                -FilePath (Get-Command node).Source `
                -ArgumentList @(
                    $openClaw,
                    "agent",
                    "--agent",
                    "main",
                    "--message-file",
                    $prompt,
                    "--model",
                    "github-copilot/gpt-5.4-mini",
                    "--thinking",
                    "high",
                    "--timeout",
                    "1800",
                    "--json"
                ) `
                -RedirectStandardOutput $agentResponse `
                -RedirectStandardError $agentErrorLog `
                -NoNewWindow `
                -PassThru

            $ErrorActionPreference = "Continue"
            node $openClaw tui `
                --url $gatewayUrl `
                --token $env:OPENCLAW_GATEWAY_TOKEN `
                --session "main"
            $tuiExitCode = $LASTEXITCODE
            $ErrorActionPreference = "Stop"

            if ($tuiExitCode -ne 0) {
                throw "OpenClaw TUI exited with code $tuiExitCode."
            }

            Write-Host "TUI closed; waiting for the final plan." -ForegroundColor Cyan
        }
        "Browser" {
            Write-Host "Opening the authenticated OpenClaw Control UI." -ForegroundColor Green
            $encodedToken = [uri]::EscapeDataString($env:OPENCLAW_GATEWAY_TOKEN)
            $dashboardUrl = "http://127.0.0.1:$gatewayPort/#token=$encodedToken"
            Start-Process $dashboardUrl
        }
        default {
            Write-Host "Monitoring disabled. Use -Monitor Terminal, Tui, or Browser to watch progress."
        }
    }

    if ($Monitor -ne "Tui") {
        $ErrorActionPreference = "Continue"
        $jsonResult = node $openClaw agent `
            --agent "main" `
            --message-file $prompt `
            --model "github-copilot/gpt-5.4-mini" `
            --thinking high `
            --timeout 1800 `
            --json 2> $agentErrorLog |
            Out-String
        $openClawExitCode = $LASTEXITCODE
        $ErrorActionPreference = "Stop"

        if ($openClawExitCode -ne 0) {
            $agentError = Get-Content $agentErrorLog -Raw
            if ($agentError -notmatch "session aborted: user_initiated") {
                throw "OpenClaw run failed. See openclaw\artifacts\agent-error.log."
            }
        } elseif ($jsonResult) {
            Set-Content -Path $agentResponse -Value $jsonResult -Encoding utf8
        }
    }

    $planReady = Test-Path $output
    for ($attempt = 0; -not $planReady -and $attempt -lt 360; $attempt++) {
        Start-Sleep -Seconds 5
        if ($gatewayProcess.HasExited) {
            throw "OpenClaw Gateway stopped before producing the plan."
        }

        $planReady = Test-Path $output
    }

    if (-not $planReady) {
        throw "OpenClaw did not create the plan within 30 minutes."
    }

    & (Join-Path $PSScriptRoot "test-result.ps1") `
        -StateDir $stateDir `
        -RunResult $runResult `
        -Plan $output
} finally {
    if ($agentProcess -and -not $agentProcess.HasExited) {
        Stop-Process -Id $agentProcess.Id
        $agentProcess.WaitForExit()
    }

    if ($gatewayProcess -and -not $gatewayProcess.HasExited) {
        Stop-Process -Id $gatewayProcess.Id
        $gatewayProcess.WaitForExit()
    }

    Pop-Location
}
