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

function Test-PrintRxerPortMonitorSecurity {
    $dllPath = Join-Path $env:WINDIR 'System32\PrintRxerPortMonitor.dll'
    $regPath = "Registry::HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Print\Monitors\PrintRxer Port Monitor"
    
    if (Test-Path -LiteralPath $dllPath) {
        try {
            $acl = Get-Acl -LiteralPath $dllPath
            foreach ($rule in $acl.Access) {
                if ($rule.AccessControlType -eq 'Allow') {
                    $sid = $null
                    try {
                        $sid = $rule.IdentityReference.Translate([System.Security.Principal.SecurityIdentifier]).Value
                    } catch {
                        $sid = $rule.IdentityReference.Value
                    }
                    if ($sid -ne 'S-1-5-18' -and $sid -ne 'S-1-5-32-544') {
                        $rights = $rule.FileSystemRights
                        # Check for specific write/modify rights to avoid false positives with ReadAndExecute/Read
                        $writeModifyFlags = [System.Security.AccessControl.FileSystemRights]::WriteData -bor
                                            [System.Security.AccessControl.FileSystemRights]::AppendData -bor
                                            [System.Security.AccessControl.FileSystemRights]::WriteAttributes -bor
                                            [System.Security.AccessControl.FileSystemRights]::WriteExtendedAttributes -bor
                                            [System.Security.AccessControl.FileSystemRights]::Delete -bor
                                            [System.Security.AccessControl.FileSystemRights]::ChangePermissions -bor
                                            [System.Security.AccessControl.FileSystemRights]::TakeOwnership
                        if (($rights -band $writeModifyFlags) -ne 0) {
                            $critical.Add("CRITICAL_SECURITY_FAILURE: DLL '$dllPath' allows write/modify access to non-admin identity '$($rule.IdentityReference.Value)' ($sid).")
                        }
                    }
                }
            }
        }
        catch {
            $ex = $_.Exception
            $isUnauthorized = $false
            if ($null -ne $ex) {
                if ($ex -is [System.UnauthorizedAccessException] -or $ex -is [System.Security.SecurityException]) {
                    $isUnauthorized = $true
                } elseif ($null -ne $ex.InnerException -and ($ex.InnerException -is [System.UnauthorizedAccessException] -or $ex.InnerException -is [System.Security.SecurityException])) {
                    $isUnauthorized = $true
                }
            }
            if (-not $isUnauthorized -and $_.ToString() -match "unauthorized|permission|access") {
                $isUnauthorized = $true
            }

            if ($isUnauthorized) {
                $notices.Add("Notice: Skipping DLL ACL validation because the current user does not have permission to read ACLs (expected for standard users).")
            } else {
                $critical.Add("CRITICAL_SECURITY_FAILURE: Failed to query/verify ACL for DLL '$dllPath': $_")
            }
        }
    }

    if (Test-Path -Path $regPath) {
        try {
            $acl = Get-Acl -Path $regPath
            foreach ($rule in $acl.Access) {
                if ($rule.AccessControlType -eq 'Allow') {
                    $sid = $null
                    try {
                        $sid = $rule.IdentityReference.Translate([System.Security.Principal.SecurityIdentifier]).Value
                    } catch {
                        $sid = $rule.IdentityReference.Value
                    }
                    if ($sid -ne 'S-1-5-18' -and $sid -ne 'S-1-5-32-544') {
                        $rights = $rule.RegistryRights
                        # Check for specific write/modify rights to avoid false positives with ReadKey
                        $writeModifyFlags = [System.Security.AccessControl.RegistryRights]::SetValue -bor
                                            [System.Security.AccessControl.RegistryRights]::CreateSubKey -bor
                                            [System.Security.AccessControl.RegistryRights]::CreateLink -bor
                                            [System.Security.AccessControl.RegistryRights]::Delete -bor
                                            [System.Security.AccessControl.RegistryRights]::ChangePermissions -bor
                                            [System.Security.AccessControl.RegistryRights]::TakeOwnership
                        if (($rights -band $writeModifyFlags) -ne 0) {
                            $critical.Add("CRITICAL_SECURITY_FAILURE: Registry key '$regPath' allows write/modify access to non-admin identity '$($rule.IdentityReference.Value)' ($sid).")
                        }
                    }
                }
            }
        }
        catch {
            $ex = $_.Exception
            $isUnauthorized = $false
            if ($null -ne $ex) {
                if ($ex -is [System.UnauthorizedAccessException] -or $ex -is [System.Security.SecurityException]) {
                    $isUnauthorized = $true
                } elseif ($null -ne $ex.InnerException -and ($ex.InnerException -is [System.UnauthorizedAccessException] -or $ex.InnerException -is [System.Security.SecurityException])) {
                    $isUnauthorized = $true
                }
            }
            if (-not $isUnauthorized -and $_.ToString() -match "unauthorized|permission|access") {
                $isUnauthorized = $true
            }

            if ($isUnauthorized) {
                $notices.Add("Notice: Skipping registry ACL validation because the current user does not have permission to read ACLs (expected for standard users).")
            } else {
                $critical.Add("CRITICAL_SECURITY_FAILURE: Failed to query/verify ACL for registry key '$regPath': $_")
            }
        }
    }
}

$warnings = New-Object System.Collections.Generic.List[string]
$critical = New-Object System.Collections.Generic.List[string]
$notices = New-Object System.Collections.Generic.List[string]

$printConfig = $null
$healthConfig = $null
$printTaskState = Get-TaskState 'printRxer'
$healthTaskState = Get-TaskState 'HealthMailer'
if (Test-Path -LiteralPath $PrintRxerConfig) {
    $printConfig = Get-Content -LiteralPath $PrintRxerConfig | ConvertFrom-Json
} else {
    if ($printTaskState -eq 'NotInstalled') {
        $notices.Add("printRxer is not installed; config not found: $PrintRxerConfig")
    } else {
        $critical.Add("printRxer config not found: $PrintRxerConfig")
    }
}

if (Test-Path -LiteralPath $HealthMailerConfig) {
    $healthConfig = Get-Content -LiteralPath $HealthMailerConfig | ConvertFrom-Json
} else {
    if ($healthTaskState -eq 'NotInstalled') {
        $notices.Add("HealthMailer is not installed; config not found: $HealthMailerConfig")
    } else {
        $critical.Add("HealthMailer config not found: $HealthMailerConfig")
    }
}

if ($printTaskState -eq 'NotInstalled' -and $healthTaskState -eq 'NotInstalled') {
    $notices.Add("No printRxer or HealthMailer scheduled tasks are installed. Preserved local data may remain after standard uninstall.")
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

Test-PrintRxerPortMonitorSecurity

$result = [ordered]@{
    Status = if ($critical.Count -gt 0) { 'Critical' } elseif ($warnings.Count -gt 0) { 'Warning' } elseif ($printTaskState -eq 'NotInstalled' -and $healthTaskState -eq 'NotInstalled') { 'NotInstalled' } else { 'Healthy' }
    PrintRxerConfig = $PrintRxerConfig
    HealthMailerConfig = $HealthMailerConfig
    PrintRxerTask = $printTaskState
    HealthMailerTask = $healthTaskState
    PrintRxerPendingCount = $printPendingCount
    PrintRxerOldestPendingAgeMinutes = $printPendingAge
    HealthMailerReadyCount = $healthReadyCount
    HealthMailerOldestReadyAgeMinutes = $healthReadyAge
    HealthMailerFailedCount = $failedCount
    HealthMailerQuarantineCount = $quarantineCount
    Notices = @($notices)
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
    foreach ($item in $notices) { Write-Host "NOTICE: $item" }
    foreach ($item in $warnings) { Write-Warning $item }
    foreach ($item in $critical) { Write-Error $item -ErrorAction Continue }
}

if ($critical.Count -gt 0) { exit 2 }
if ($warnings.Count -gt 0) { exit 1 }
exit 0
