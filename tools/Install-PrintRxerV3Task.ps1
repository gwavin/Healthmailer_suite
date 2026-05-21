param(
    [string]$ExePath = (Resolve-Path ".\publish\PrintRxerV3\printrxer_v3.exe").Path,
    [string]$IncomingRoot = "C:\ProgramData\printrxer_v3\work\incoming",
    [string]$DataRoot = "C:\ProgramData\printrxer_v3",
    [string]$ProcessedRoot = "",
    [string]$DeferredRoot = "",
    [string]$LocalOutboxRoot = "",
    [string]$PublishedRoot = "",
    [string]$FailedRoot = "",
    [string]$LogsRoot = "",
    [string]$TempRoot = "",
    [string]$HandoffRoot = "",
    [string]$ConfigPath = "C:\ProgramData\printrxer_v3\config\printrxer_v3.settings.json",
    [string]$RecipientDataRoot = "C:\ProgramData\printrxer_v3\data",
    [int]$PayloadStableSeconds = 1,
    [int]$RetryIntervalSeconds = 1,
    [string]$TaskName = "PrintRxerV3",
    [switch]$SkipTaskRegistration
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $ExePath)) {
    throw "printrxer_v3.exe was not found at $ExePath"
}

if ([string]::IsNullOrWhiteSpace($HandoffRoot)) {
    Add-Type -AssemblyName System.Windows.Forms
    $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
    $dialog.Description = 'Select the HealthMailer handoff folder. This may be a local folder or UNC share.'
    if ($dialog.PSObject.Properties.Name -contains 'UseDescriptionForTitle') {
        $dialog.UseDescriptionForTitle = $true
    }
    $dialog.ShowNewFolderButton = $true
    $dialog.SelectedPath = 'C:\ProgramData\printrxer_v3\handoff'
    if ($dialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) {
        throw 'PrintRxerV3 install cancelled because no handoff folder was selected.'
    }
    $HandoffRoot = $dialog.SelectedPath
}

if ([string]::IsNullOrWhiteSpace($ProcessedRoot)) { $ProcessedRoot = Join-Path $DataRoot 'processed' }
if ([string]::IsNullOrWhiteSpace($DeferredRoot)) { $DeferredRoot = Join-Path $DataRoot 'deferred' }
if ([string]::IsNullOrWhiteSpace($LocalOutboxRoot)) { $LocalOutboxRoot = Join-Path $DataRoot 'pending-outbox' }
if ([string]::IsNullOrWhiteSpace($PublishedRoot)) { $PublishedRoot = Join-Path $DataRoot 'published' }
if ([string]::IsNullOrWhiteSpace($FailedRoot)) { $FailedRoot = Join-Path $DataRoot 'failed' }
if ([string]::IsNullOrWhiteSpace($LogsRoot)) { $LogsRoot = Join-Path $DataRoot 'logs' }
if ([string]::IsNullOrWhiteSpace($TempRoot)) { $TempRoot = Join-Path $DataRoot 'temp' }

New-Item -ItemType Directory -Force -Path $IncomingRoot, $ProcessedRoot, $DeferredRoot, $LocalOutboxRoot, $PublishedRoot, $FailedRoot, $LogsRoot, $TempRoot, (Split-Path -Parent $ConfigPath), (Join-Path $RecipientDataRoot 'recipients'), (Join-Path $RecipientDataRoot 'Images') | Out-Null

$repoRoot = Split-Path -Parent $PSScriptRoot
$seedRecipients = Join-Path $repoRoot 'assets\recipients\recipients.csv'
$seedImage = Join-Path $repoRoot 'assets\branding\mncms_400x400.jpg'
$installedRecipients = Join-Path $RecipientDataRoot 'recipients\recipients.csv'
$installedImage = Join-Path $RecipientDataRoot 'Images\mncms_400x400.jpg'
if ((Test-Path -LiteralPath $seedRecipients) -and -not (Test-Path -LiteralPath $installedRecipients)) {
    Copy-Item -LiteralPath $seedRecipients -Destination $installedRecipients -Force
}

if ((Test-Path -LiteralPath $seedImage) -and -not (Test-Path -LiteralPath $installedImage)) {
    Copy-Item -LiteralPath $seedImage -Destination $installedImage -Force
}

$config = [ordered]@{
    IncomingRoot = $IncomingRoot
    ProcessedRoot = $ProcessedRoot
    DeferredRoot = $DeferredRoot
    LocalOutboxRoot = $LocalOutboxRoot
    PublishedRoot = $PublishedRoot
    FailedRoot = $FailedRoot
    LogsRoot = $LogsRoot
    TempRoot = $TempRoot
    HandoffRoot = $HandoffRoot
    PayloadStableSeconds = $PayloadStableSeconds
    RequireJobOwnerMatch = $true
    AllowMissingSubmittingSid = $false
    RetryIntervalSeconds = $RetryIntervalSeconds
    MaxLogBytes = 5242880
    MaxLogFiles = 3
}
$config | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $ConfigPath -Encoding UTF8

if ($SkipTaskRegistration) {
    Write-Host "PrintRxerV3 config written. Scheduled task registration skipped."
    Write-Host "Incoming:  $IncomingRoot"
    Write-Host "Handoff:   $HandoffRoot"
    Write-Host "Config:    $ConfigPath"
    return
}

$arguments = "--watch --config `"$ConfigPath`""
$action = New-ScheduledTaskAction -Execute $ExePath -Argument $arguments
$logonTrigger = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
$watchdogTrigger = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1) -RepetitionInterval (New-TimeSpan -Minutes 1) -RepetitionDuration (New-TimeSpan -Days 999)
$principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet `
    -MultipleInstances IgnoreNew `
    -RestartCount 999 `
    -RestartInterval (New-TimeSpan -Minutes 1) `
    -ExecutionTimeLimit (New-TimeSpan -Days 999) `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -StartWhenAvailable

Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger @($logonTrigger, $watchdogTrigger) -Principal $principal -Settings $settings -Force | Out-Null
Start-ScheduledTask -TaskName $TaskName

Write-Host "PrintRxerV3 task installed and started."
Write-Host "Incoming:  $IncomingRoot"
Write-Host "Handoff:   $HandoffRoot"
Write-Host "Config:    $ConfigPath"
