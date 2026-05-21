param(
    [string]$TaskName = 'HealthMailer',
    [string]$LocalRoot = 'C:\ProgramData\HealthMailer'
)

$ErrorActionPreference = 'Stop'
$failures = 0

function Test-State {
    param(
        [string]$Name,
        [bool]$Passed,
        [string]$Detail
    )

    if ($Passed) {
        Write-Host "[PASS] $Name - $Detail"
    } else {
        Write-Host "[FAIL] $Name - $Detail"
        $script:failures++
    }
}

$task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
Test-State "Scheduled task absent" ($null -eq $task) "TaskName=$TaskName"

$processes = Get-Process -Name 'HealthMailer' -ErrorAction SilentlyContinue
Test-State "No running HealthMailer process" ($null -eq $processes) "ProcessCount=$(if ($processes) { $processes.Count } else { 0 })"

$rootExists = Test-Path -LiteralPath $LocalRoot
if ($rootExists) {
    Write-Host "[INFO] Local data root retained: $LocalRoot"
    foreach ($child in @('healthmailer.settings.json', 'logs', 'sent', 'failed', 'quarantine', 'processed-ledger.jsonl')) {
        $path = Join-Path $LocalRoot $child
        Write-Host "[INFO] $child present: $(Test-Path -LiteralPath $path)"
    }
} else {
    Write-Host "[INFO] Local data root absent: $LocalRoot"
}

if ($failures -gt 0) {
    throw "$failures uninstall state check(s) failed."
}

Write-Host "HealthMailer uninstall state checks passed."
