param(
    [string]$TaskName = 'PrintRxerV3',
    [string]$DataRoot = 'C:\ProgramData\printrxer_v3'
)

$ErrorActionPreference = 'Stop'
$failures = 0

function Test-State {
    param([string]$Name, [bool]$Passed, [string]$Detail)
    if ($Passed) {
        Write-Host "[PASS] $Name - $Detail"
    } else {
        Write-Host "[FAIL] $Name - $Detail"
        $script:failures++
    }
}

$task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
Test-State "Scheduled task absent" ($null -eq $task) "TaskName=$TaskName"

$processes = Get-Process -Name 'printrxer_v3' -ErrorAction SilentlyContinue
Test-State "No running PrintRxerV3 process" ($null -eq $processes) "ProcessCount=$(if ($processes) { $processes.Count } else { 0 })"

if (Test-Path -LiteralPath $DataRoot) {
    Write-Host "[INFO] Data root retained: $DataRoot"
    foreach ($child in @('handoff', 'processed', 'deferred')) {
        $path = Join-Path $DataRoot $child
        Write-Host "[INFO] $child present: $(Test-Path -LiteralPath $path)"
    }
} else {
    Write-Host "[INFO] Data root absent: $DataRoot"
}

if ($failures -gt 0) {
    throw "$failures uninstall state check(s) failed."
}

Write-Host "PrintRxerV3 uninstall state checks passed."
