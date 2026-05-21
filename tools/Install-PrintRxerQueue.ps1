[CmdletBinding()]
param(
    [switch]$EnsurePortMonitor,
    [switch]$SetAsDefault,
    [string]$OptionsPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$powershellExe = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'

function Import-RelaunchOptions {
    param(
        [string]$Path
    )

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

    $script:EnsurePortMonitor = [bool]$options.EnsurePortMonitor
    $script:SetAsDefault = [bool]$options.SetAsDefault
}

function New-RelaunchOptionsFile {
    param(
        [hashtable]$Options
    )

    $path = New-TemporaryFile
    [pscustomobject]$Options | Export-Clixml -LiteralPath $path
    return $path
}

Import-RelaunchOptions -Path $OptionsPath

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $relaunchOptionsPath = New-RelaunchOptionsFile @{
        EnsurePortMonitor = [bool]$EnsurePortMonitor
        SetAsDefault = [bool]$SetAsDefault
    }

    $elevatedProcess = Start-Process -FilePath $powershellExe -Verb RunAs -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath, '-OptionsPath', $relaunchOptionsPath) -PassThru -Wait
    if ($null -eq $elevatedProcess) {
        throw 'Failed to launch elevated queue install process.'
    }
    if ($elevatedProcess.ExitCode -ne 0) {
        throw "Elevated queue install failed with exit code $($elevatedProcess.ExitCode)."
    }

    Write-Host 'Elevated queue install completed successfully.'
    exit 0
}

$printerName = 'printRxer'
$driverName = 'PrintRxer XPS Driver'
$portName = 'printrx:'

if (-not (Get-PrinterPort -Name $portName -ErrorAction SilentlyContinue)) {
    if ($EnsurePortMonitor) {
        & (Join-Path $PSScriptRoot 'Install-PrintRxerPortMonitor.ps1')
    }
}

if (-not (Get-PrinterPort -Name $portName -ErrorAction SilentlyContinue)) {
    throw "Printer port '$portName' is not installed yet. Run .\\tools\\Install-PrintRxerPortMonitor.ps1 first."
}

$printerPort = Get-PrinterPort -Name $portName
$driver = Get-PrinterDriver -Name $driverName -ErrorAction SilentlyContinue
if (-not $driver) {
    throw "Printer driver '$driverName' is not installed. The native monitor is ready, but the next phase needs a custom v3 driver package for printRxer."
}

if ($driver.MajorVersion -ge 4) {
    throw "Printer driver '$driverName' is a v$($driver.MajorVersion) driver. Windows blocks v4/inbox drivers on non-inbox port monitors like '$($printerPort.PortMonitor)', so printRxer needs a custom v3 driver package instead."
}

$existingPrinter = Get-Printer -Name $printerName -ErrorAction SilentlyContinue
$existingPrinterCim = Get-CimInstance -ClassName Win32_Printer -Filter ("Name='{0}'" -f ($printerName -replace "'", "''")) -ErrorAction SilentlyContinue
$wasDefault = $false
if ($null -ne $existingPrinterCim) {
    $wasDefault = [bool]$existingPrinterCim.Default
}

if ($existingPrinter -and ($existingPrinter.DriverName -ne $driverName -or $existingPrinter.PortName -ne $portName)) {
    $existingJobs = Get-PrintJob -PrinterName $printerName -ErrorAction SilentlyContinue
    if ($existingJobs) {
        throw "Printer '$printerName' has queued jobs. Clear the queue before replacing it."
    }

    Remove-Printer -Name $printerName
    $existingPrinter = $null
}

if (-not $existingPrinter) {
    Add-Printer -Name $printerName -DriverName $driverName -PortName $portName | Out-Null
}

if ($SetAsDefault -or $wasDefault) {
    $network = New-Object -ComObject WScript.Network
    $network.SetDefaultPrinter($printerName)
}

Get-Printer -Name $printerName | Format-List Name, DriverName, PortName
