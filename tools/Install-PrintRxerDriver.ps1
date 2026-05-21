[CmdletBinding()]
param(
    [switch]$CreateQueue = $true,
    [switch]$SetAsDefault,
    [string]$CertificateSubject = 'CN=PrintRxer Driver Test',
    [string]$OptionsPath,
    [switch]$UsePrebuiltPackage
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$powershellExe = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
$rundll32Exe = Join-Path $env:WINDIR 'System32\rundll32.exe'

$repoRoot = Split-Path -Parent $PSScriptRoot
$logDirectory = Join-Path $repoRoot 'bin\logs'
$logPath = Join-Path $logDirectory 'Install-PrintRxerDriver.log'

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

    $script:CreateQueue = [bool]$options.CreateQueue
    $script:SetAsDefault = [bool]$options.SetAsDefault
    $script:CertificateSubject = [string]$options.CertificateSubject
    $script:UsePrebuiltPackage = [bool]$options.UsePrebuiltPackage
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

if (-not ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $relaunchOptionsPath = New-RelaunchOptionsFile @{
        CreateQueue = [bool]$CreateQueue
        SetAsDefault = [bool]$SetAsDefault
        CertificateSubject = $CertificateSubject
        UsePrebuiltPackage = [bool]$UsePrebuiltPackage
    }

    $elevatedProcess = Start-Process -FilePath $powershellExe -Verb RunAs -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath, '-OptionsPath', $relaunchOptionsPath) -PassThru -Wait
    if ($null -eq $elevatedProcess) {
        throw 'Failed to launch elevated driver install process.'
    }
    if ($elevatedProcess.ExitCode -ne 0) {
        throw "Elevated driver install failed with exit code $($elevatedProcess.ExitCode)."
    }

    Write-Host 'Elevated driver install completed successfully.'
    exit 0
}

$buildScript = Join-Path $PSScriptRoot 'Build-PrintRxerDriverPackage.ps1'
$queueScript = Join-Path $PSScriptRoot 'Install-PrintRxerQueue.ps1'
$packageRoot = Join-Path $repoRoot 'assets\print-capture\driver-package\PrintRxer XPS Driver'
$infPath = Join-Path $packageRoot 'PrintRxerXpsDrv.inf'
$catalogPath = Join-Path $packageRoot 'printrxerxpsdrv.cat'
$certificatePath = Join-Path $packageRoot 'PrintRxerDriverTest.cer'
$driverName = 'PrintRxer XPS Driver'
$environment = 'Windows x64'
$UPDP_SILENT_UPLOAD = 0x00000001
$UPDP_UPLOAD_ALWAYS = 0x00000002
$IPDFP_COPY_ALL_FILES = 0x00000001

New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
Start-Transcript -Path $logPath -Force | Out-Null

function Write-Step {
    param([string]$Message)
    Write-Host "[PrintRxer.Driver] $Message"
}

function Format-HResult {
    param([int]$Value)
    return ('0x{0:X8}' -f ($Value -band 0xFFFFFFFFL))
}

function Get-FirstExistingToolPath {
    param(
        [string[]]$Patterns
    )

    foreach ($pattern in $Patterns) {
        $match = Get-ChildItem $pattern -File -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending |
            Select-Object -First 1 -ExpandProperty FullName
        if (-not [string]::IsNullOrWhiteSpace($match)) {
            return $match
        }
    }

    return $null
}

function Test-PrebuiltDriverPackageAvailable {
    return (
        (Test-Path -LiteralPath $infPath) -and
        (Test-Path -LiteralPath $catalogPath) -and
        (Test-Path -LiteralPath $certificatePath) -and
        (Test-Path -LiteralPath (Join-Path $packageRoot 'amd64\PrintRxerXpsDrv.gpd')) -and
        (Test-Path -LiteralPath (Join-Path $packageRoot 'amd64\PrintRxer-pipelineconfig.xml')) -and
        (Test-Path -LiteralPath (Join-Path $packageRoot 'amd64\PrintConfig.dll'))
    )
}

function Test-DriverBuildPrerequisitesAvailable {
    $gpdCheck = Get-FirstExistingToolPath -Patterns @(
        'C:\Program Files (x86)\Windows Kits\10\Tools\*\x64\gpdcheck.exe',
        'C:\Program Files\Windows Kits\10\Tools\*\x64\gpdcheck.exe'
    )
    $inf2Cat = Get-FirstExistingToolPath -Patterns @(
        'C:\Program Files (x86)\Windows Kits\10\bin\*\x86\Inf2Cat.exe',
        'C:\Program Files (x86)\Windows Kits\10\bin\*\x64\Inf2Cat.exe',
        'C:\Program Files\Windows Kits\10\bin\*\x86\Inf2Cat.exe',
        'C:\Program Files\Windows Kits\10\bin\*\x64\Inf2Cat.exe'
    )
    $signtool = Get-FirstExistingToolPath -Patterns @(
        'C:\Program Files (x86)\Windows Kits\10\bin\*\x64\signtool.exe',
        'C:\Program Files\Windows Kits\10\bin\*\x64\signtool.exe'
    )
    $printConfigDll = 'C:\Windows\System32\spool\drivers\x64\3\PrintConfig.dll'

    return (
        (-not [string]::IsNullOrWhiteSpace($gpdCheck)) -and
        (-not [string]::IsNullOrWhiteSpace($inf2Cat)) -and
        (-not [string]::IsNullOrWhiteSpace($signtool)) -and
        (Test-Path -LiteralPath $printConfigDll)
    )
}

try {
    if (-not $UsePrebuiltPackage -and -not (Test-DriverBuildPrerequisitesAvailable)) {
        if (Test-PrebuiltDriverPackageAvailable) {
            Write-Step 'Local Windows driver build tools were not found. Falling back to the prebuilt signed driver package.'
            $UsePrebuiltPackage = $true
        } else {
            throw 'The local Windows driver build tools were not found, and no prebuilt signed driver package is available. Use a prebuilt install bundle or rerun Install-PrintRxer.ps1 -UsePrebuiltArtifacts.'
        }
    }

    if ($UsePrebuiltPackage) {
        Write-Step 'Using prebuilt signed driver package.'
        if (-not (Test-Path -LiteralPath $infPath)) {
            throw "Prebuilt driver INF not found: $infPath"
        }
        if (-not (Test-Path -LiteralPath $catalogPath)) {
            throw "Prebuilt driver catalog not found: $catalogPath"
        }
        if (-not (Test-Path -LiteralPath $certificatePath)) {
            throw "Prebuilt signing certificate not found: $certificatePath"
        }

        Import-Certificate -FilePath $certificatePath -CertStoreLocation 'Cert:\LocalMachine\Root' | Out-Null
        Import-Certificate -FilePath $certificatePath -CertStoreLocation 'Cert:\LocalMachine\TrustedPublisher' | Out-Null

        $signature = Get-AuthenticodeSignature -LiteralPath $catalogPath
        if ($signature.Status -ne 'Valid') {
            throw "The prebuilt catalog did not validate successfully. Status: $($signature.Status)"
        }
    } else {
        Write-Step 'Building driver package scaffold and catalog.'
        & $buildScript -GenerateCatalog

        $signtool = Get-FirstExistingToolPath -Patterns @(
            'C:\Program Files (x86)\Windows Kits\10\bin\*\x64\signtool.exe',
            'C:\Program Files\Windows Kits\10\bin\*\x64\signtool.exe'
        )

        if (-not $signtool) {
            throw 'Could not locate signtool.exe in the Windows Kits bin directory.'
        }

        Write-Step 'Preparing local test-signing certificate.'
        $certificate = Get-ChildItem Cert:\LocalMachine\My | Where-Object { $_.Subject -eq $CertificateSubject } | Sort-Object NotAfter -Descending | Select-Object -First 1
        if (-not $certificate) {
            $certificate = New-SelfSignedCertificate `
                -Type CodeSigningCert `
                -Subject $CertificateSubject `
                -FriendlyName 'PrintRxer Driver Test Certificate' `
                -CertStoreLocation 'Cert:\LocalMachine\My' `
                -KeyExportPolicy Exportable `
                -KeyAlgorithm RSA `
                -KeyLength 2048 `
                -HashAlgorithm SHA256 `
                -NotAfter (Get-Date).AddYears(5)
        }

        Export-Certificate -Cert $certificate -FilePath $certificatePath -Force | Out-Null
        Import-Certificate -FilePath $certificatePath -CertStoreLocation 'Cert:\LocalMachine\Root' | Out-Null
        Import-Certificate -FilePath $certificatePath -CertStoreLocation 'Cert:\LocalMachine\TrustedPublisher' | Out-Null

        Write-Step 'Signing generated catalog.'
        & $signtool sign /v /fd SHA256 /sha1 $certificate.Thumbprint /sm /s My $catalogPath
        if ($LASTEXITCODE -ne 0) {
            throw 'signtool failed while signing the PrintRxer driver catalog.'
        }

        $signature = Get-AuthenticodeSignature -LiteralPath $catalogPath
        if ($signature.Status -ne 'Valid') {
            throw "The signed catalog did not validate successfully. Status: $($signature.Status)"
        }
    }

    if (-not ('PrintRxerDriverApi' -as [type])) {
        Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class PrintRxerDriverApi
{
    [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "UploadPrinterDriverPackageW")]
    public static extern int UploadPrinterDriverPackage(
        string pszServer,
        string pszInfPath,
        string pszEnvironment,
        uint dwFlags,
        IntPtr hwnd,
        StringBuilder pszDestInfPath,
        ref uint pcchDestInfPath);

    [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "InstallPrinterDriverFromPackageW")]
    public static extern int InstallPrinterDriverFromPackage(
        string pszServer,
        string pszInfPath,
        string pszDriverName,
        string pszEnvironment,
        uint dwFlags);
}
"@
    }

    $destInfPathBuffer = New-Object System.Text.StringBuilder 1024
    [uint32]$destInfPathLength = [uint32]$destInfPathBuffer.Capacity

    Write-Step 'Uploading signed package into the print server driver store.'
    $uploadFlags = [uint32]($UPDP_SILENT_UPLOAD -bor $UPDP_UPLOAD_ALWAYS)
    $uploadResult = [PrintRxerDriverApi]::UploadPrinterDriverPackage($null, $infPath, $environment, $uploadFlags, [IntPtr]::Zero, $destInfPathBuffer, [ref]$destInfPathLength)
    if ($uploadResult -ne 0) {
        throw "UploadPrinterDriverPackage failed with $(Format-HResult $uploadResult)."
    }

    $uploadedInfPath = $destInfPathBuffer.ToString()
    if ([string]::IsNullOrWhiteSpace($uploadedInfPath)) {
        throw 'UploadPrinterDriverPackage succeeded but did not return a destination INF path.'
    }

    Write-Step "Uploaded package to $uploadedInfPath"

    $driver = Get-PrinterDriver -Name $driverName -ErrorAction SilentlyContinue
    if (-not $driver) {
        Write-Step 'Registering printer driver with Add-PrinterDriver.'
        try {
            Add-PrinterDriver -Name $driverName -InfPath $uploadedInfPath -ErrorAction Stop
        } catch {
            Write-Warning "Add-PrinterDriver failed: $($_.Exception.Message). Trying InstallPrinterDriverFromPackage."
            try {
                $installResult = [PrintRxerDriverApi]::InstallPrinterDriverFromPackage($null, $uploadedInfPath, $driverName, $environment, [uint32]$IPDFP_COPY_ALL_FILES)
                if ($installResult -ne 0) {
                    throw "InstallPrinterDriverFromPackage returned $(Format-HResult $installResult)."
                }
            } catch {
                Write-Warning "InstallPrinterDriverFromPackage failed: $($_.Exception.Message). Falling back to PrintUIEntry."
                Start-Process -FilePath $rundll32Exe -ArgumentList @(
                    'printui.dll,PrintUIEntry',
                    '/ia',
                    '/m', $driverName,
                    '/h', 'x64',
                    '/v', 'Type 3 - User Mode',
                    '/f', $uploadedInfPath
                ) -Wait
            }
        }
    }

    $driver = Get-PrinterDriver -Name $driverName -ErrorAction SilentlyContinue
    if (-not $driver) {
        throw "The printer driver '$driverName' is still not installed after the package install attempt."
    }

    if ($CreateQueue) {
        Write-Step 'Creating or updating printRxer queue.'
        $arguments = @(
            '-ExecutionPolicy', 'Bypass',
            '-File', $queueScript
        )
        if ($SetAsDefault) {
            $arguments += '-SetAsDefault'
        }

        & $powershellExe $arguments
        if ($LASTEXITCODE -ne 0) {
            throw 'Queue installation failed after the driver package was installed.'
        }
    }

    Write-Host "Installed printer driver $driverName"
    Write-Host "Catalog: $catalogPath"
    Write-Host "Certificate: $certificatePath"
    Write-Host "Log: $logPath"
} finally {
    Stop-Transcript | Out-Null
}
