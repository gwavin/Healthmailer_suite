param(
    [string]$PrintRxerConfig = "C:\ProgramData\printRxer\config\printRxer.settings.json",
    [string]$HealthMailerConfig = "C:\ProgramData\HealthMailer\healthmailer.settings.json",
    [int]$PendingWarningMinutes = 10,
    [int]$ReadyWarningMinutes = 10,
    [int]$MinimumFreeDiskGb = 1,
    [switch]$Json,
    [switch]$PlanOnly
)

$ErrorActionPreference = 'Stop'

function Get-DirectoryCount([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) { return 0 }
    return @(Get-ChildItem -LiteralPath $Path -Directory -ErrorAction SilentlyContinue).Count
}

function Get-ReadyPackageCount([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) { return 0 }
    return @(Get-ChildItem -LiteralPath $Path -Directory -ErrorAction SilentlyContinue | Where-Object {
        -not $_.Name.StartsWith('.') -and (Test-Path -LiteralPath (Join-Path $_.FullName 'READY'))
    }).Count
}

function Get-OldestAgeMinutes([string]$Path, [switch]$ReadyOnly) {
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) { return $null }
    $items = Get-ChildItem -LiteralPath $Path -Directory -ErrorAction SilentlyContinue
    if ($ReadyOnly) {
        $items = $items | Where-Object {
            -not $_.Name.StartsWith('.') -and (Test-Path -LiteralPath (Join-Path $_.FullName 'READY'))
        }
    }
    $oldest = $items | Sort-Object CreationTimeUtc | Select-Object -First 1
    if ($null -eq $oldest) { return $null }
    return [math]::Round(((Get-Date).ToUniversalTime() - $oldest.CreationTimeUtc).TotalMinutes, 1)
}

function Get-TaskState([string]$Name) {
    try {
        return (Get-ScheduledTask -TaskName $Name -ErrorAction Stop).State.ToString()
    } catch {
        return 'NotInstalled'
    }
}

$warnings = New-Object System.Collections.Generic.List[string]
$critical = New-Object System.Collections.Generic.List[string]

$printConfig = $null
$healthConfig = $null
if (Test-Path -LiteralPath $PrintRxerConfig) {
    $printConfig = Get-Content -LiteralPath $PrintRxerConfig | ConvertFrom-Json
} else {
    $critical.Add("printRxer config not found: $PrintRxerConfig")
}

if (Test-Path -LiteralPath $HealthMailerConfig) {
    $healthConfig = Get-Content -LiteralPath $HealthMailerConfig | ConvertFrom-Json
} else {
    $critical.Add("HealthMailer config not found: $HealthMailerConfig")
}

$printPendingCount = 0
$printPendingAge = $null
$healthReadyCount = 0
$healthReadyAge = $null
$failedCount = 0
$quarantineCount = 0

if ($printConfig) {
    $printPendingCount = Get-DirectoryCount $printConfig.LocalOutboxRoot
    $printPendingAge = Get-OldestAgeMinutes $printConfig.LocalOutboxRoot
    if ($printPendingAge -ne $null -and $printPendingAge -ge $PendingWarningMinutes) {
        $warnings.Add("printRxer pending package age is $printPendingAge minutes.")
    }
    if (-not (Test-Path -LiteralPath $printConfig.HandoffRoot -PathType Container)) {
        $warnings.Add("printRxer handoff folder is unavailable: $($printConfig.HandoffRoot)")
    }
}

if ($healthConfig) {
    $healthReadyCount = Get-ReadyPackageCount $healthConfig.HandoffRoot
    $healthReadyAge = Get-OldestAgeMinutes $healthConfig.HandoffRoot -ReadyOnly
    $localRoot = $healthConfig.LocalRoot
    $failedCount = Get-DirectoryCount (Join-Path $localRoot 'failed')
    $quarantineCount = Get-DirectoryCount (Join-Path $localRoot 'quarantine')
    if ($healthReadyAge -ne $null -and $healthReadyAge -ge $ReadyWarningMinutes) {
        $warnings.Add("HealthMailer READY package age is $healthReadyAge minutes.")
    }
    if ($failedCount -gt 0) { $warnings.Add("HealthMailer failed package count is $failedCount.") }
    if ($quarantineCount -gt 0) { $warnings.Add("HealthMailer quarantine package count is $quarantineCount.") }
    if (-not (Test-Path -LiteralPath $healthConfig.HandoffRoot -PathType Container)) {
        $warnings.Add("HealthMailer handoff folder is unavailable: $($healthConfig.HandoffRoot)")
    }
}

$result = [ordered]@{
    Status = if ($critical.Count -gt 0) { 'Critical' } elseif ($warnings.Count -gt 0) { 'Warning' } else { 'Healthy' }
    PrintRxerConfig = $PrintRxerConfig
    HealthMailerConfig = $HealthMailerConfig
    PrintRxerTask = Get-TaskState 'printRxer'
    HealthMailerTask = Get-TaskState 'HealthMailer'
    PrintRxerPendingCount = $printPendingCount
    PrintRxerOldestPendingAgeMinutes = $printPendingAge
    HealthMailerReadyCount = $healthReadyCount
    HealthMailerOldestReadyAgeMinutes = $healthReadyAge
    HealthMailerFailedCount = $failedCount
    HealthMailerQuarantineCount = $quarantineCount
    Warnings = @($warnings)
    Critical = @($critical)
    PlanOnly = [bool]$PlanOnly
}

if ($Json) {
    $result | ConvertTo-Json -Depth 5
} else {
    Write-Host "PrintRxer Suite health: $($result.Status)"
    Write-Host "printRxer task: $($result.PrintRxerTask)"
    Write-Host "HealthMailer task: $($result.HealthMailerTask)"
    Write-Host "Pending/READY/failed/quarantine: $printPendingCount/$healthReadyCount/$failedCount/$quarantineCount"
    foreach ($item in $warnings) { Write-Warning $item }
    foreach ($item in $critical) { Write-Error $item -ErrorAction Continue }
}

if ($critical.Count -gt 0) { exit 2 }
if ($warnings.Count -gt 0) { exit 1 }
exit 0
