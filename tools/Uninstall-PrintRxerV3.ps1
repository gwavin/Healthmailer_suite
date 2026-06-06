param(
    [switch]$PlanOnly,
    [switch]$RemoveData,
    [string]$TaskName = 'printRxer',
    [string]$DataRoot = 'C:\ProgramData\printRxer',
    [string]$PublishedRuntime = ''
)

$ErrorActionPreference = 'Stop'

function Write-Step {
    param([string]$Message)
    Write-Host "[printRxer uninstall] $Message"
}

$task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
$processes = Get-Process -Name 'printRxer' -ErrorAction SilentlyContinue

Write-Step "Plan:"
if ($task) { Write-Step "Remove scheduled task '$TaskName'." } else { Write-Step "Scheduled task '$TaskName' is already absent." }
if ($processes) { Write-Step "Stop $($processes.Count) running printRxer process(es)." } else { Write-Step "No running printRxer process found." }
if ($PublishedRuntime) { Write-Step "Remove published runtime folder '$PublishedRuntime' if present." } else { Write-Step "No published runtime folder was supplied; runtime files will be left in place." }
if ($RemoveData) { Write-Step "Remove local ProgramData root '$DataRoot', including local data, logs, configuration, outbox, processed captures, failed captures, and archives." } else { Write-Step "Preserve local ProgramData root '$DataRoot' by default, including local data, logs, configuration, outbox, processed captures, failed captures, and archives." }

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
    Write-Step "Running printRxer process(es) stopped."
}

if ($PublishedRuntime -and (Test-Path -LiteralPath $PublishedRuntime)) {
    Remove-Item -LiteralPath $PublishedRuntime -Recurse -Force
    Write-Step "Published runtime folder removed."
}

if ($RemoveData -and (Test-Path -LiteralPath $DataRoot)) {
    Remove-Item -LiteralPath $DataRoot -Recurse -Force
    Write-Step "printRxer ProgramData removed."
}

Write-Step "Uninstall completed."
