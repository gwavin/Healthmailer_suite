param(
    [switch]$PlanOnly,
    [switch]$RemoveData,
    [string]$TaskName = 'HealthMailer',
    [string]$LocalRoot = 'C:\ProgramData\HealthMailer',
    [string]$PublishedRuntime = ''
)

$ErrorActionPreference = 'Stop'

function Write-Step {
    param([string]$Message)
    Write-Host "[HealthMailer uninstall] $Message"
}

$task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
$processes = Get-Process -Name 'HealthMailer' -ErrorAction SilentlyContinue

Write-Step "Plan:"
if ($task) {
    Write-Step "Remove scheduled task '$TaskName'."
} else {
    Write-Step "Scheduled task '$TaskName' is already absent."
}

if ($processes) {
    Write-Step "Stop $($processes.Count) running HealthMailer process(es)."
} else {
    Write-Step "No running HealthMailer process found."
}

if ($PublishedRuntime) {
    Write-Step "Remove published runtime folder '$PublishedRuntime' if present."
} else {
    Write-Step "No published runtime folder was supplied; runtime files will be left in place."
}

if ($RemoveData) {
    Write-Step "Remove local ProgramData root '$LocalRoot', including local data, logs, configuration, sent archives, failed archives, quarantine, and ledger."
} else {
    Write-Step "Preserve local ProgramData root '$LocalRoot' by default, including local data, logs, configuration, archives, and ledger."
}

if ($PlanOnly) {
    Write-Step "PlanOnly was supplied; no changes made."
    return
}

if ($task) {
    Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
    Write-Step "Scheduled task removed."
}

if ($processes) {
    $processes | Stop-Process -Force
    Write-Step "Running HealthMailer process(es) stopped."
}

if ($PublishedRuntime -and (Test-Path -LiteralPath $PublishedRuntime)) {
    Remove-Item -LiteralPath $PublishedRuntime -Recurse -Force
    Write-Step "Published runtime folder removed."
}

if ($RemoveData -and (Test-Path -LiteralPath $LocalRoot)) {
    Remove-Item -LiteralPath $LocalRoot -Recurse -Force
    Write-Step "Local HealthMailer ProgramData removed."
}

Write-Step "Uninstall completed."
