param(
    [string]$RunResult,

    [Parameter(Mandatory)]
    [string]$Plan,

    [string]$StateDir
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $Plan)) {
    throw "OpenClaw did not create the expected plan: $Plan"
}

if (-not $StateDir) {
    $openClawRoot = Split-Path (Split-Path $Plan -Parent) -Parent
    $StateDir = Get-ChildItem (Join-Path $openClawRoot ".state") -Directory |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}

if (-not $StateDir -or -not (Test-Path $StateDir)) {
    throw "Missing OpenClaw state directory for delegation validation."
}

$planText = Get-Content $Plan -Raw
$requiredPatterns = [ordered]@{
    "Route planner contribution" = "(?i)pl[aá]nova[cč]\s+tras|route planner|route_planner"
    "Meteorologist contribution" = "(?i)meteorolog"
    "Budget analyst contribution" = "(?i)rozpo[cč]t[aá][rř]|budget analyst|budget_analyst"
    "Weather sources" = "https?://"
    "Contingency reserve" = "(?i)10\s*%\s*(rezerv|reserve)|rezerv[a-zá-ž]*\s+10\s*%"
    "Per-person total" = "(?i)na\s+osobu|za\s+osobu"
    "OpenClaw coordination" = "(?i)Jak OpenClaw koordinoval agenty"
}

$missing = foreach ($check in $requiredPatterns.GetEnumerator()) {
    if ($planText -notmatch $check.Value) {
        $check.Key
    }
}

if ($missing) {
    throw "Plan validation failed. Missing: $($missing -join ', ')."
}

$transcripts = Get-ChildItem -Path $StateDir -Filter "*.jsonl" -File -Recurse
if (-not $transcripts) {
    throw "No OpenClaw session transcripts were created."
}

$events = foreach ($transcript in $transcripts) {
    foreach ($line in Get-Content -LiteralPath $transcript.FullName) {
        try {
            $line | ConvertFrom-Json
        } catch {
            continue
        }
    }
}

$spawnCallIds = $events |
    Where-Object {
        $_.type -eq "tool.execution_start" -and
        $_.data.toolName -eq "sessions_spawn"
    } |
    ForEach-Object { $_.data.toolCallId }

$acceptedTaskNames = foreach ($event in $events) {
    if (
        $event.type -ne "tool.execution_complete" -or
        $event.data.toolCallId -notin $spawnCallIds -or
        -not $event.data.success
    ) {
        continue
    }

    try {
        $spawnResult = $event.data.result.content | ConvertFrom-Json
        if ($spawnResult.status -eq "accepted") {
            $spawnResult.taskName
        }
    } catch {
        continue
    }
}

$requiredTaskNames = @(
    "program_coordinator",
    "route_planner",
    "meteorologist",
    "budget_analyst"
)

$missingTasks = foreach ($taskName in $requiredTaskNames) {
    if ($acceptedTaskNames -notcontains $taskName) {
        $taskName
    }
}

if ($missingTasks) {
    throw "Delegation validation failed. Missing agent traces: $($missingTasks -join ', ')."
}

$copilotEvents = Get-ChildItem -Path $StateDir -Filter "events.jsonl" -File -Recurse |
    Where-Object { $_.FullName -match "[\\/]agent[\\/]copilot[\\/]" }
if (-not $copilotEvents) {
    throw "The run did not use the GitHub Copilot agent harness."
}

$validationResult = [ordered]@{
    ok = $true
    status = "ok"
    provider = "github-copilot"
    model = "gpt-5.4-mini"
    agentHarnessId = "copilot"
    acceptedAgents = $requiredTaskNames
    transcriptCount = $transcripts.Count
    plan = (Resolve-Path $Plan).Path
}

if ($RunResult) {
    $validationResult | ConvertTo-Json -Depth 4 |
        Set-Content -Path $RunResult -Encoding utf8
}

Write-Host "OpenClaw created the plan with the coordinator and all three specialists." -ForegroundColor Green
