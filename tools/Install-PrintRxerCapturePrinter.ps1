[CmdletBinding()]
param(
    [string]$RuntimeUser,
    [switch]$SetAsDefault,
    [string]$OptionsPath,
    [switch]$PlanOnly
)

$ErrorActionPreference = 'Stop'
$powershellExe = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'

function Import-RelaunchOptions {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return
    }

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Relaunch options file not found: $Path"
    }

    try {
        $options = Import-Clixml -LiteralPath $Path
    } finally {
        Remove-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
    }

    if ($null -eq $options) {
        return
    }

    $script:RuntimeUser = [string]$options.RuntimeUser
    $script:SetAsDefault = [bool]$options.SetAsDefault
    $script:PlanOnly = [bool]$options.PlanOnly
}

function New-RelaunchOptionsFile {
    param([hashtable]$Options)

    $path = New-TemporaryFile
    [pscustomobject]$Options | Export-Clixml -LiteralPath $path
    return $path
}

function Write-Step {
    param([string]$Message)
    Write-Host "[PrintRxer capture printer] $Message"
}

Import-RelaunchOptions -Path $OptionsPath

if ($PlanOnly) {
    Write-Step "Would install native PrintRxer Port Monitor, PrintRxer XPS Driver, and printRxer printer queue."
    Write-Step "This step requires Administrator/UAC approval because it installs Windows spooler components."
    return
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $relaunchOptionsPath = New-RelaunchOptionsFile @{
        RuntimeUser = $RuntimeUser
        SetAsDefault = [bool]$SetAsDefault
        PlanOnly = [bool]$PlanOnly
    }

    Write-Step "Requesting administrator approval for printer installation."
    $elevatedProcess = Start-Process -FilePath $powershellExe -Verb RunAs -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath, '-OptionsPath', $relaunchOptionsPath) -PassThru -Wait
    if ($null -eq $elevatedProcess) {
        throw 'Failed to launch elevated capture-printer install process.'
    }
    if ($elevatedProcess.ExitCode -ne 0) {
        throw "Elevated capture-printer install failed with exit code $($elevatedProcess.ExitCode)."
    }

    Write-Step 'Elevated capture-printer install completed successfully.'
    exit 0
}

$portArgs = @('-ExecutionPolicy', 'Bypass', '-File', (Join-Path $PSScriptRoot 'Install-PrintRxerPortMonitor.ps1'), '-SkipBuild')
if (-not [string]::IsNullOrWhiteSpace($RuntimeUser)) {
    $portArgs += @('-RuntimeUser', $RuntimeUser)
}

Write-Step "Installing native port monitor."
& powershell @portArgs
if ($LASTEXITCODE -ne 0) {
    throw "Port monitor install failed with exit code $LASTEXITCODE."
}

$driverArgs = @('-ExecutionPolicy', 'Bypass', '-File', (Join-Path $PSScriptRoot 'Install-PrintRxerDriver.ps1'), '-UsePrebuiltPackage')
if ($SetAsDefault) {
    $driverArgs += '-SetAsDefault'
}

Write-Step "Installing printer driver and queue."
& powershell @driverArgs
if ($LASTEXITCODE -ne 0) {
    throw "Driver/queue install failed with exit code $LASTEXITCODE."
}

$printer = Get-Printer -Name 'printRxer' -ErrorAction SilentlyContinue
if (-not $printer) {
    throw "The native capture printer install completed, but the printRxer queue is not present."
}

Write-Step "Installed printer '$($printer.Name)' on port '$($printer.PortName)' with driver '$($printer.DriverName)'."
