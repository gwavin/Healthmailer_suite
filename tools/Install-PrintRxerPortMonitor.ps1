[CmdletBinding()]
param(
    [switch]$RestartSpoolerAfterInstall = $true,
    [string]$RuntimeUser,
    [switch]$SkipBuild,
    [string]$OptionsPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$buildScript = Join-Path $PSScriptRoot 'Build-PrintRxerPortMonitor.ps1'
$builtDllPath = Join-Path $repoRoot 'assets\print-capture\native\x64\PrintRxerPortMonitor.dll'
$systemDllPath = Join-Path $env:WINDIR 'System32\PrintRxerPortMonitor.dll'
$monitorName = 'PrintRxer Port Monitor'
$portName = 'printrx:'
$workingRoot = Join-Path (Join-Path $env:ProgramData 'printrxer_v3') 'work'
$monitorRegistryPath = "HKLM:\SYSTEM\CurrentControlSet\Control\Print\Monitors\$monitorName"
$powershellExe = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
$icaclsExe = Join-Path $env:WINDIR 'System32\icacls.exe'
$logDirectory = Join-Path $repoRoot 'bin\logs'
$logPath = Join-Path $logDirectory 'Install-PrintRxerPortMonitor.log'
$bootstrapLogPath = Join-Path $logDirectory 'Install-PrintRxerPortMonitor.bootstrap.log'

function Write-BootstrapTrace {
    param(
        [string]$Message
    )

    try {
        New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
        Add-Content -LiteralPath $bootstrapLogPath -Value ((Get-Date -Format 's') + ' ' + $Message)
    }
    catch {
    }
}

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

    $script:RestartSpoolerAfterInstall = [bool]$options.RestartSpoolerAfterInstall
    $script:RuntimeUser = [string]$options.RuntimeUser
    $script:SkipBuild = [bool]$options.SkipBuild
}

function New-RelaunchOptionsFile {
    param(
        [hashtable]$Options
    )

    $path = New-TemporaryFile
    [pscustomobject]$Options | Export-Clixml -LiteralPath $path
    return $path
}

function Test-PortMonitorBuildToolchainAvailable {
    $vswherePath = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path -LiteralPath $vswherePath) {
        $installationPath = & $vswherePath -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath 2>$null | Select-Object -First 1
        if (-not [string]::IsNullOrWhiteSpace($installationPath)) {
            $candidate = Join-Path $installationPath 'Common7\Tools\VsDevCmd.bat'
            if (Test-Path -LiteralPath $candidate) {
                return $true
            }
        }
    }

    foreach ($pattern in @(
        'C:\Program Files\Microsoft Visual Studio\*\*\Common7\Tools\VsDevCmd.bat',
        'C:\Program Files (x86)\Microsoft Visual Studio\*\*\Common7\Tools\VsDevCmd.bat'
    )) {
        if (Get-ChildItem $pattern -File -ErrorAction SilentlyContinue | Select-Object -First 1) {
            return $true
        }
    }

    return $false
}

function Resolve-PrintRxerRuntimeUser {
    param(
        [string]$UserName
    )

    $candidateNames = New-Object 'System.Collections.Generic.List[string]'
    if (-not [string]::IsNullOrWhiteSpace($UserName)) {
        $candidateNames.Add($UserName)
        if (-not $UserName.Contains('\') -and -not $UserName.Contains('@')) {
            $candidateNames.Add("$env:COMPUTERNAME\$UserName")
        }
    } else {
        $candidateNames.Add([Security.Principal.WindowsIdentity]::GetCurrent().Name)
    }

    foreach ($candidate in $candidateNames | Select-Object -Unique) {
        try {
            ([Security.Principal.NTAccount]$candidate).Translate([Security.Principal.SecurityIdentifier]) | Out-Null
            return $candidate
        } catch {
        }
    }

    throw "Runtime user '$UserName' could not be resolved. Use DOMAIN\User or user@domain."
}

function Set-PrintRxerDirectoryAcl {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$UserName
    )

    New-Item -ItemType Directory -Path $Path -Force | Out-Null

    $directories = @((Get-Item -LiteralPath $Path))
    $directories += @(Get-ChildItem -LiteralPath $Path -Recurse -Directory -Force -ErrorAction SilentlyContinue)
    foreach ($directory in $directories | Select-Object -Unique) {
        & $icaclsExe $directory.FullName /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)(F)' '*S-1-5-32-544:(OI)(CI)(F)' ('{0}:(OI)(CI)(M)' -f $UserName) /Q | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to secure directory ACLs on $($directory.FullName)"
        }
    }

    $files = Get-ChildItem -LiteralPath $Path -Recurse -File -Force -ErrorAction SilentlyContinue
    foreach ($file in $files) {
        & $icaclsExe $file.FullName /inheritance:r /grant:r '*S-1-5-18:(F)' '*S-1-5-32-544:(F)' ('{0}:(M)' -f $UserName) /Q | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to secure file ACLs on $($file.FullName)"
        }
    }
}

Write-BootstrapTrace 'Script start.'
Import-RelaunchOptions -Path $OptionsPath
$resolvedRuntimeUser = Resolve-PrintRxerRuntimeUser -UserName $RuntimeUser
Write-BootstrapTrace ("Resolved runtime user: " + $resolvedRuntimeUser)

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-BootstrapTrace 'Entering non-admin relaunch branch.'
    $relaunchOptionsPath = New-RelaunchOptionsFile @{
        RestartSpoolerAfterInstall = [bool]$RestartSpoolerAfterInstall
        RuntimeUser = $resolvedRuntimeUser
        SkipBuild = [bool]$SkipBuild
    }

    $elevatedProcess = Start-Process -FilePath $powershellExe -Verb RunAs -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath, '-OptionsPath', $relaunchOptionsPath) -PassThru -Wait
    Write-BootstrapTrace ("Elevated child exit code: " + $elevatedProcess.ExitCode)
    if ($null -eq $elevatedProcess) {
        throw 'Failed to launch elevated port-monitor install process.'
    }
    if ($elevatedProcess.ExitCode -ne 0) {
        throw "Elevated port-monitor install failed with exit code $($elevatedProcess.ExitCode)."
    }

    Write-Host 'Elevated port-monitor install completed successfully.'
    exit 0
}

if ($SkipBuild) {
    Write-BootstrapTrace 'Running in skip-build mode.'
    if (-not (Test-Path -LiteralPath $builtDllPath)) {
        throw "Prebuilt native monitor DLL not found: $builtDllPath"
    }
} else {
    if (Test-PortMonitorBuildToolchainAvailable) {
        & $buildScript
    } elseif (Test-Path -LiteralPath $builtDllPath) {
        Write-Warning "Visual Studio C++ build tools were not found. Using the prebuilt native monitor DLL at '$builtDllPath'."
    } else {
        throw "Could not locate Visual Studio C++ build tooling and no prebuilt native monitor DLL was found at '$builtDllPath'. Use a prebuilt install bundle or rerun with packaged artifacts."
    }
}

function Wait-ServiceStatus {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [ValidateSet('Running', 'Stopped')]
        [string]$Status,

        [int]$TimeoutSeconds = 20
    )

    $service = Get-Service -Name $Name -ErrorAction Stop
    $service.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::$Status, [TimeSpan]::FromSeconds($TimeoutSeconds))
}

function Get-PrintRxerFileHash {
    param(
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return $null
    }

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
Start-Transcript -Path $logPath -Force | Out-Null

try {
    foreach ($directory in @(
        $workingRoot,
        (Join-Path $workingRoot 'spool'),
        (Join-Path $workingRoot 'incoming')
    )) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }
    Set-PrintRxerDirectoryAcl -Path $workingRoot -UserName $resolvedRuntimeUser

    $spoolerWasRunning = (Get-Service -Name Spooler).Status -eq 'Running'
    if ($spoolerWasRunning) {
        Stop-Service -Name Spooler -Force
        Wait-ServiceStatus -Name Spooler -Status Stopped
    }

    $sourceHash = Get-PrintRxerFileHash -Path $builtDllPath
    $destinationHash = Get-PrintRxerFileHash -Path $systemDllPath
    if ($sourceHash -and $destinationHash -and $sourceHash -eq $destinationHash) {
        Write-Host "System DLL already matches the staged native monitor DLL."
    } else {
        Copy-Item -LiteralPath $builtDllPath -Destination $systemDllPath -Force
    }

    Start-Service -Name Spooler
    Wait-ServiceStatus -Name Spooler -Status Running

    if (-not ('PrintRxerNativeMethods' -as [type])) {
        Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public static class PrintRxerNativeMethods
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITOR_INFO_2
    {
        public string pName;
        public string pEnvironment;
        public string pDLLName;
    }

    [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "AddMonitorW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AddMonitor(string pName, uint Level, ref MONITOR_INFO_2 pMonitor);
}
"@
    }

    if (-not (Test-Path -LiteralPath $monitorRegistryPath)) {
        $info = New-Object PrintRxerNativeMethods+MONITOR_INFO_2
        $info.pName = $monitorName
        $info.pEnvironment = $null
        $info.pDLLName = [System.IO.Path]::GetFileName($systemDllPath)

        if (-not [PrintRxerNativeMethods]::AddMonitor($null, 2, [ref]$info)) {
            $lastError = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
            throw "AddMonitor failed with Win32 error $lastError."
        }
    }

    if ($RestartSpoolerAfterInstall) {
        Restart-Service -Name Spooler -Force
    }

    $port = Get-PrinterPort -Name $portName -ErrorAction SilentlyContinue
    if (-not $port) {
        throw "The monitor installed, but printer port '$portName' is not being enumerated yet."
    }

    Write-Host "Installed monitor $monitorName"
    Write-Host "System DLL: $systemDllPath"
    Write-Host "Port: $portName"
    Write-Host "Runtime user: $resolvedRuntimeUser"
    Write-Host "Log: $logPath"
}
finally {
    Stop-Transcript | Out-Null
}
