param(
    [switch]$PlanOnly,
    [switch]$RemoveData,
    [string]$TaskName = 'PrintRxerV3',
    [string]$DataRoot = 'C:\ProgramData\printrxer_v3',
    [string]$PublishedRuntime = ''
)

$ErrorActionPreference = 'Stop'

function Write-Step {
    param([string]$Message)
    Write-Host "[PrintRxerV3 uninstall] $Message"
}

$task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
$processes = Get-Process -Name 'printrxer_v3' -ErrorAction SilentlyContinue

Write-Step "Plan:"
if ($task) { Write-Step "Remove scheduled task '$TaskName'." } else { Write-Step "Scheduled task '$TaskName' is already absent." }
if ($processes) { Write-Step "Stop $($processes.Count) running PrintRxerV3 process(es)." } else { Write-Step "No running PrintRxerV3 process found." }
if ($PublishedRuntime) { Write-Step "Remove published runtime folder '$PublishedRuntime' if present." } else { Write-Step "No published runtime folder was supplied; runtime files will be left in place." }
if ($RemoveData) { Write-Step "Remove data root '$DataRoot'." } else { Write-Step "Preserve data root '$DataRoot' by default." }

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
    Write-Step "Running PrintRxerV3 process(es) stopped."
}

if ($PublishedRuntime -and (Test-Path -LiteralPath $PublishedRuntime)) {
    Remove-Item -LiteralPath $PublishedRuntime -Recurse -Force
    Write-Step "Published runtime folder removed."
}

if ($RemoveData -and (Test-Path -LiteralPath $DataRoot)) {
    Remove-Item -LiteralPath $DataRoot -Recurse -Force
    Write-Step "PrintRxerV3 data removed."
}

Write-Step "Uninstall completed."
