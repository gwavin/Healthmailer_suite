param(
    [string]$OutputRoot = ".\dist\support-bundles"
)

$ErrorActionPreference = 'Stop'

function Copy-IfPresent {
    param(
        [string]$Source,
        [string]$Destination
    )

    if (-not (Test-Path -LiteralPath $Source)) {
        return
    }

    $parent = Split-Path -Parent $Destination
    New-Item -ItemType Directory -Force -Path $parent | Out-Null

    $item = Get-Item -LiteralPath $Source
    if ($item -is [System.IO.DirectoryInfo]) {
        $sourceRoot = $item.FullName.TrimEnd('\') + '\'
        Get-ChildItem -LiteralPath $item.FullName -Recurse -File -ErrorAction SilentlyContinue |
            Where-Object { $_.Extension -ne '.pdf' } |
            ForEach-Object {
                $relative = $_.FullName.Substring($sourceRoot.Length)
                $target = Join-Path $Destination $relative
                New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
                Copy-Item -LiteralPath $_.FullName -Destination $target -Force
            }
    } else {
        Copy-Item -LiteralPath $Source -Destination $Destination -Force
    }
}

function Write-CommandOutput {
    param(
        [string]$Path,
        [scriptblock]$Command
    )

    try {
        & $Command | Out-String | Set-Content -LiteralPath $Path -Encoding UTF8
    } catch {
        ("Command failed: " + $_.Exception.Message) | Set-Content -LiteralPath $Path -Encoding UTF8
    }
}

$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$outputRootFull = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputRoot)
$bundleRoot = Join-Path $outputRootFull ("printRxer-support-" + $timestamp)
$zipPath = $bundleRoot + ".zip"

if (Test-Path -LiteralPath $bundleRoot) {
    Remove-Item -LiteralPath $bundleRoot -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $bundleRoot | Out-Null

$notes = @"
printRxer support bundle
Created: $(Get-Date -Format o)

This support bundle may contain patient-identifiable information. PDF payload files are excluded by default, but package metadata, result.json, summary.txt, logs, failed/quarantine evidence, recipient details, MRNs/patient hints, package IDs, hashes, and audit metadata may still be present.

The bundle is HSE-machine/admin-restricted audit-support evidence for troubleshooting, clinical communication evidence, and governance review. Store it only on HSE-controlled machines or approved HSE storage. Limit access to administrators and approved support/audit personnel.

Do not email or transfer this bundle except through approved HSE support/governance channels. ProgramData evidence should be preserved by default for audit and support unless an approved retention process says otherwise.
"@
Set-Content -LiteralPath (Join-Path $bundleRoot 'README.txt') -Value $notes -Encoding UTF8

Copy-IfPresent 'C:\ProgramData\printRxer\config' (Join-Path $bundleRoot 'printRxer\config')
Copy-IfPresent 'C:\ProgramData\printRxer\logs' (Join-Path $bundleRoot 'printRxer\logs')
Copy-IfPresent 'C:\ProgramData\printRxer\failed' (Join-Path $bundleRoot 'printRxer\failed')

Copy-IfPresent 'C:\ProgramData\HealthMailer\config' (Join-Path $bundleRoot 'HealthMailer\config')
Copy-IfPresent 'C:\ProgramData\HealthMailer\healthmailer.settings.json' (Join-Path $bundleRoot 'HealthMailer\healthmailer.settings.json')
Copy-IfPresent 'C:\ProgramData\HealthMailer\logs' (Join-Path $bundleRoot 'HealthMailer\logs')
Copy-IfPresent 'C:\ProgramData\HealthMailer\failed' (Join-Path $bundleRoot 'HealthMailer\failed')
Copy-IfPresent 'C:\ProgramData\HealthMailer\quarantine' (Join-Path $bundleRoot 'HealthMailer\quarantine')
Copy-IfPresent (Join-Path (Get-Location) 'SHA256SUMS.txt') (Join-Path $bundleRoot 'release-SHA256SUMS.txt')

Write-CommandOutput (Join-Path $bundleRoot 'scheduled-tasks.txt') {
    Get-ScheduledTask -TaskName 'printRxer','HealthMailer' -ErrorAction SilentlyContinue |
        Select-Object TaskName, State, TaskPath
    Get-ScheduledTaskInfo -TaskName 'printRxer' -ErrorAction SilentlyContinue
    Get-ScheduledTaskInfo -TaskName 'HealthMailer' -ErrorAction SilentlyContinue
}

Write-CommandOutput (Join-Path $bundleRoot 'printer-status.txt') {
    Get-Service -Name Spooler -ErrorAction SilentlyContinue | Select-Object Name, Status
    Get-Printer -Name 'printRxer' -ErrorAction SilentlyContinue | Format-List *
    Get-PrinterPort -Name 'printrx:' -ErrorAction SilentlyContinue | Format-List *
    Get-PrinterDriver -Name 'PrintRxer XPS Driver' -ErrorAction SilentlyContinue | Format-List *
}

Write-CommandOutput (Join-Path $bundleRoot 'processes.txt') {
    Get-Process -Name 'printRxer','HealthMailer' -ErrorAction SilentlyContinue |
        Select-Object ProcessName, Id, Path, StartTime
}

$bundleRootWithSlash = $bundleRoot.TrimEnd('\') + '\'
$hashLines = Get-ChildItem -LiteralPath $bundleRoot -Recurse -File |
    Sort-Object FullName |
    ForEach-Object {
        $relative = $_.FullName.Substring($bundleRootWithSlash.Length).Replace('\', '/')
        $hash = Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256
        $hash.Hash.ToLowerInvariant() + "  " + $relative
    }

Set-Content -LiteralPath (Join-Path $bundleRoot 'SHA256SUMS.txt') -Value $hashLines -Encoding ASCII

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

Compress-Archive -Path (Join-Path $bundleRoot '*') -DestinationPath $zipPath -Force
Write-Output $zipPath
