param(
    [string]$ExePath = (Resolve-Path ".\publish\HealthMailer\HealthMailer.exe").Path,
    [string]$ConfigPath = "C:\ProgramData\HealthMailer\healthmailer.settings.json"
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $ExePath)) {
    throw "HealthMailer.exe was not found at $ExePath"
}

if (-not (Test-Path -LiteralPath $ConfigPath)) {
    throw "HealthMailer config was not found at $ConfigPath"
}

$action = New-ScheduledTaskAction -Execute $ExePath -Argument "--watch --config `"$ConfigPath`""
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

Register-ScheduledTask -TaskName 'HealthMailer' -Action $action -Trigger @($logonTrigger, $watchdogTrigger) -Principal $principal -Settings $settings -Force | Out-Null
Start-ScheduledTask -TaskName 'HealthMailer'

Write-Host 'HealthMailer task installed and started.'
