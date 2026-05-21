[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$sourcePath = Join-Path $repoRoot 'native\PrintRxer.PortMonitor\PrintRxerPortMonitor.c'
$outputDirectory = Join-Path $repoRoot 'assets\print-capture\native\x64'
$dllPath = Join-Path $outputDirectory 'PrintRxerPortMonitor.dll'
$cmdExe = Join-Path $env:WINDIR 'System32\cmd.exe'

function Get-VisualStudioDevCommandPath {
    $vswherePath = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path -LiteralPath $vswherePath) {
        $installationPath = & $vswherePath -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath 2>$null | Select-Object -First 1
        if (-not [string]::IsNullOrWhiteSpace($installationPath)) {
            $candidate = Join-Path $installationPath 'Common7\Tools\VsDevCmd.bat'
            if (Test-Path -LiteralPath $candidate) {
                return $candidate
            }
        }
    }

    foreach ($pattern in @(
        'C:\Program Files\Microsoft Visual Studio\*\*\Common7\Tools\VsDevCmd.bat',
        'C:\Program Files (x86)\Microsoft Visual Studio\*\*\Common7\Tools\VsDevCmd.bat'
    )) {
        $match = Get-ChildItem $pattern -File -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending |
            Select-Object -First 1 -ExpandProperty FullName
        if (-not [string]::IsNullOrWhiteSpace($match)) {
            return $match
        }
    }

    return $null
}

if (-not (Test-Path -LiteralPath $sourcePath)) {
    throw "Native monitor source not found: $sourcePath"
}

$vsDevCmd = Get-VisualStudioDevCommandPath

if (-not $vsDevCmd) {
    throw 'Could not locate VsDevCmd.bat. Install Visual Studio Build Tools or Visual Studio with C++ support, or use the prebuilt native monitor DLL from a packaged install bundle.'
}

New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

$command = 'call "{0}" -no_logo -host_arch=x64 -arch=x64 >nul && cl /nologo /LD /W4 /DUNICODE /D_UNICODE "{1}" /link /OUT:"{2}" advapi32.lib winspool.lib /EXPORT:InitializePrintMonitor2' -f $vsDevCmd, $sourcePath, $dllPath
& $cmdExe /c $command

if ($LASTEXITCODE -ne 0) {
    throw "Native monitor build failed with exit code $LASTEXITCODE."
}

Write-Host "Built $dllPath"
