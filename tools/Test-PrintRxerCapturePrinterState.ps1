[CmdletBinding()]
param(
    [switch]$Json
)

$printer = Get-Printer -Name 'printRxer' -ErrorAction SilentlyContinue
$port = Get-PrinterPort -Name 'printrx:' -ErrorAction SilentlyContinue
$driver = Get-PrinterDriver -Name 'PrintRxer XPS Driver' -ErrorAction SilentlyContinue
$dllPath = Join-Path $env:WINDIR 'System32\PrintRxerPortMonitor.dll'
$monitorRegistryPath = 'HKLM:\SYSTEM\CurrentControlSet\Control\Print\Monitors\PrintRxer Port Monitor'

$state = [pscustomobject]@{
    PrinterPresent = [bool]$printer
    PrinterName = $printer.Name
    DriverPresent = [bool]$driver
    DriverName = $driver.Name
    PortPresent = [bool]$port
    PortName = $port.Name
    PortMonitor = $port.PortMonitor
    PortMonitorDllPresent = Test-Path -LiteralPath $dllPath
    PortMonitorRegistryPresent = Test-Path -LiteralPath $monitorRegistryPath
}

if ($Json) {
    $state | ConvertTo-Json -Depth 3
} else {
    $state | Format-List
}

if ($state.PrinterPresent -and $state.DriverPresent -and $state.PortPresent -and $state.PortMonitorDllPresent -and $state.PortMonitorRegistryPresent) {
    exit 0
}

exit 1
