[CmdletBinding()]
param(
    [switch]$PlanOnly
)

$ErrorActionPreference = 'Stop'

function Write-Step {
    param([string]$Message)
    Write-Host "[PrintRxer capture printer uninstall] $Message"
}

$printer = Get-Printer -Name 'printRxer' -ErrorAction SilentlyContinue
$port = Get-PrinterPort -Name 'printrx:' -ErrorAction SilentlyContinue
$driver = Get-PrinterDriver -Name 'PrintRxer XPS Driver' -ErrorAction SilentlyContinue
$dllPath = Join-Path $env:WINDIR 'System32\PrintRxerPortMonitor.dll'
$monitorRegistryPath = 'HKLM:\SYSTEM\CurrentControlSet\Control\Print\Monitors\PrintRxer Port Monitor'

if ($PlanOnly) {
    Write-Step "Would remove printRxer printer queue: $([bool]$printer)"
    Write-Step "Would remove printrx: port: $([bool]$port)"
    Write-Step "Would remove PrintRxer XPS Driver: $([bool]$driver)"
    Write-Step "Would remove PrintRxer Port Monitor registry/DLL if present."
    return
}

if ($printer) {
    try {
        Get-PrintJob -PrinterName 'printRxer' -ErrorAction SilentlyContinue | Remove-PrintJob -ErrorAction SilentlyContinue
        Remove-Printer -Name 'printRxer' -ErrorAction Stop
        Write-Step "Removed printRxer printer queue."
    } catch {
        Write-Step "Could not remove printRxer printer queue automatically: $($_.Exception.Message)"
    }
}

if ($port) {
    try {
        Remove-PrinterPort -Name 'printrx:' -ErrorAction Stop
        Write-Step "Removed printrx: printer port."
    } catch {
        Write-Step "Could not remove printrx: printer port immediately; Windows may release it after restart: $($_.Exception.Message)"
    }
}

if ($driver) {
    try {
        Remove-PrinterDriver -Name 'PrintRxer XPS Driver' -ErrorAction Stop
        Write-Step "Removed PrintRxer XPS Driver."
    } catch {
        Write-Step "Could not remove PrintRxer XPS Driver immediately; Windows may release it after restart: $($_.Exception.Message)"
    }
}

Start-Sleep -Seconds 1
$driver = Get-PrinterDriver -Name 'PrintRxer XPS Driver' -ErrorAction SilentlyContinue
if ($driver) {
    try {
        Restart-Service -Name Spooler -Force -ErrorAction Stop
        Start-Sleep -Seconds 1
        Remove-PrinterDriver -Name 'PrintRxer XPS Driver' -ErrorAction Stop
        Write-Step "Removed PrintRxer XPS Driver after spooler restart."
    } catch {
        Write-Step "Could not remove PrintRxer XPS Driver after retry; Windows may release it after restart: $($_.Exception.Message)"
    }
}

$spoolerWasRunning = (Get-Service -Name Spooler).Status -eq 'Running'
if ($spoolerWasRunning) {
    try {
        Stop-Service -Name Spooler -Force -ErrorAction Stop
    } catch {
        Write-Step "Could not stop the Print Spooler service. Port monitor files may remain until restart: $($_.Exception.Message)"
        $spoolerWasRunning = $false
    }
}

try {
    try {
        Remove-Item -LiteralPath $monitorRegistryPath -Recurse -Force -ErrorAction Stop
        Write-Step "Removed PrintRxer Port Monitor registry entry."
    } catch {
        Write-Step "Could not remove PrintRxer Port Monitor registry entry automatically: $($_.Exception.Message)"
    }

    try {
        Remove-Item -LiteralPath $dllPath -Force -ErrorAction Stop
        Write-Step "Removed PrintRxer Port Monitor DLL."
    } catch {
        Write-Step "Could not remove PrintRxer Port Monitor DLL immediately; Windows may release it after restart: $($_.Exception.Message)"
    }
} finally {
    if ($spoolerWasRunning) {
        try {
            Start-Service -Name Spooler -ErrorAction Stop
            if (Test-Path -LiteralPath $dllPath) {
                try {
                    Remove-Item -LiteralPath $dllPath -Force -ErrorAction Stop
                    Write-Step "Removed PrintRxer Port Monitor DLL after spooler restart."
                } catch {
                    Write-Step "PrintRxer Port Monitor DLL remains on disk and can be removed after restart: $($_.Exception.Message)"
                }
            }
        } catch {
            Write-Step "Could not restart the Print Spooler service: $($_.Exception.Message)"
        }
    }
}

Write-Step "Capture printer uninstall completed."
