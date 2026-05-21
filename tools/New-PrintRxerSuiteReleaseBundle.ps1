param(
    [string]$Version = "",
    [string]$OutputRoot = ".\dist",
    [switch]$SkipTests,
    [switch]$SkipPublish,
    [switch]$SkipSmokeTest
)

$ErrorActionPreference = 'Stop'

function Write-Step {
    param([string]$Message)
    Write-Host "[PrintRxer Suite bundle] $Message"
}

function Copy-RequiredFile {
    param(
        [string]$Source,
        [string]$Destination
    )

    if (-not (Test-Path -LiteralPath $Source)) {
        throw "Required bundle file was not found: $Source"
    }

    $parent = Split-Path -Parent $Destination
    New-Item -ItemType Directory -Force -Path $parent | Out-Null
    Copy-Item -LiteralPath $Source -Destination $Destination -Force
}

function Copy-RequiredDirectory {
    param(
        [string]$Source,
        [string]$Destination
    )

    if (-not (Test-Path -LiteralPath $Source)) {
        throw "Required bundle directory was not found: $Source"
    }

    $parent = Split-Path -Parent $Destination
    New-Item -ItemType Directory -Force -Path $parent | Out-Null
    Copy-Item -LiteralPath $Source -Destination $Destination -Recurse -Force
}

function Write-Manifest {
    param([string]$Root)

    $rootWithSlash = $Root.TrimEnd('\') + '\'
    $hashLines = Get-ChildItem -LiteralPath $Root -Recurse -File |
        Sort-Object FullName |
        ForEach-Object {
            $relative = $_.FullName.Substring($rootWithSlash.Length).Replace('\', '/')
            $hash = Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256
            $hash.Hash.ToLowerInvariant() + "  " + $relative
        }

    Set-Content -LiteralPath (Join-Path $Root 'SHA256SUMS.txt') -Value $hashLines -Encoding ASCII
}

function New-ZipFromFolder {
    param(
        [string]$Folder,
        [string]$ZipPath
    )

    if (Test-Path -LiteralPath $ZipPath) {
        Remove-Item -LiteralPath $ZipPath -Force
    }

    Write-Manifest -Root $Folder
    Compress-Archive -Path (Join-Path $Folder '*') -DestinationPath $ZipPath -Force
    Write-Step "Created $ZipPath"
}

function Write-Text {
    param(
        [string]$Path,
        [string]$Content
    )

    $parent = Split-Path -Parent $Path
    New-Item -ItemType Directory -Force -Path $parent | Out-Null
    Set-Content -LiteralPath $Path -Value $Content -Encoding UTF8
}

function Test-SuiteZipSmoke {
    param(
        [string]$ZipPath,
        [string]$StagingRoot
    )

    if (-not (Test-Path -LiteralPath $ZipPath)) {
        throw "Suite ZIP was not found for smoke test: $ZipPath"
    }

    $smokeRoot = Join-Path $StagingRoot '_suite-smoke-test'
    if (Test-Path -LiteralPath $smokeRoot) {
        Remove-Item -LiteralPath $smokeRoot -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path $smokeRoot | Out-Null
    Expand-Archive -LiteralPath $ZipPath -DestinationPath $smokeRoot -Force

    $installerPath = Join-Path $smokeRoot 'PrintRxerSuiteInstaller.exe'
    if (-not (Test-Path -LiteralPath $installerPath)) {
        throw "Suite ZIP smoke test did not find PrintRxerSuiteInstaller.exe."
    }

    $process = Start-Process -FilePath $installerPath -ArgumentList '--smoke-test' -Wait -PassThru -NoNewWindow
    if ($process.ExitCode -ne 0) {
        throw "Suite installer smoke test failed with exit code $($process.ExitCode)."
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    if ([string]::IsNullOrWhiteSpace($Version)) {
        $gitVersion = git describe --tags --always --dirty 2>$null
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($gitVersion)) {
            $Version = $gitVersion.Trim()
        } else {
            $Version = "local-" + (Get-Date).ToString("yyyyMMdd-HHmmss")
        }
    }

    $safeVersion = $Version -replace '[^A-Za-z0-9_.-]', '-'
    $outputRootFull = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputRoot)
    New-Item -ItemType Directory -Force -Path $outputRootFull | Out-Null

    $stagingRoot = Join-Path $outputRootFull ("printRxerSuite-release-" + $safeVersion)
    if (Test-Path -LiteralPath $stagingRoot) {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path $stagingRoot | Out-Null

    if (-not $SkipTests) {
        Write-Step "Running tests."
        dotnet test .\PrintRxerSuite.slnx
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet test failed with exit code $LASTEXITCODE"
        }
    }

    $publishBuildRoot = Join-Path $stagingRoot '_publish'
    $printRxerPublish = Join-Path $publishBuildRoot 'printRxer'
    $healthMailerPublish = Join-Path $publishBuildRoot 'HealthMailer'
    $installerPublish = Join-Path $publishBuildRoot 'printRxerInstaller'
    $healthMailerInstallerPublish = Join-Path $publishBuildRoot 'HealthMailerInstaller'
    $suiteInstallerPublish = Join-Path $publishBuildRoot 'PrintRxerSuiteInstaller'

    if (-not $SkipPublish) {
        Write-Step "Publishing printRxer."
        & .\tools\Publish-printRxer.ps1 -Output $printRxerPublish -DoNotStopRunningWatcher

        Write-Step "Publishing HealthMailer."
        & .\tools\Publish-HealthMailer.ps1 -Output $healthMailerPublish

        Write-Step "Publishing printRxer installer."
        dotnet publish .\installers\PrintRxerV3Installer\PrintRxerV3Installer.csproj `
            -c Release `
            -r win-x64 `
            --self-contained true `
            -p:PublishSingleFile=true `
            -p:IncludeNativeLibrariesForSelfExtract=true `
            -o $installerPublish
        if ($LASTEXITCODE -ne 0) {
            throw "printRxer installer publish failed with exit code $LASTEXITCODE"
        }

        Write-Step "Publishing HealthMailer installer."
        dotnet publish .\installers\HealthMailerInstaller\HealthMailerInstaller.csproj `
            -c Release `
            -r win-x64 `
            --self-contained true `
            -p:PublishSingleFile=true `
            -p:IncludeNativeLibrariesForSelfExtract=true `
            -o $healthMailerInstallerPublish
        if ($LASTEXITCODE -ne 0) {
            throw "HealthMailer installer publish failed with exit code $LASTEXITCODE"
        }

        Write-Step "Publishing printRxer suite installer."
        dotnet publish .\installers\PrintRxerSuiteInstaller\PrintRxerSuiteInstaller.csproj `
            -c Release `
            -r win-x64 `
            --self-contained true `
            -p:PublishSingleFile=true `
            -p:IncludeNativeLibrariesForSelfExtract=true `
            -o $suiteInstallerPublish
        if ($LASTEXITCODE -ne 0) {
            throw "printRxer suite installer publish failed with exit code $LASTEXITCODE"
        }
    }

    $suiteRoot = Join-Path $stagingRoot ("printRxerSuite-" + $safeVersion)
    $printRxerRoot = Join-Path $stagingRoot ("printRxer-" + $safeVersion)
    $healthMailerRoot = Join-Path $stagingRoot ("HealthMailer-" + $safeVersion)
    foreach ($root in @($suiteRoot, $printRxerRoot, $healthMailerRoot)) {
        New-Item -ItemType Directory -Force -Path $root | Out-Null
    }

    Write-Step "Creating suite bundle."
    Copy-RequiredFile (Join-Path $suiteInstallerPublish 'PrintRxerSuiteInstaller.exe') (Join-Path $suiteRoot 'PrintRxerSuiteInstaller.exe')
    Copy-RequiredFile (Join-Path $installerPublish 'printRxerInstaller.exe') (Join-Path $suiteRoot 'payload\installers\printRxer\printRxerSetup.exe')
    Copy-RequiredFile (Join-Path $healthMailerInstallerPublish 'HealthMailerInstaller.exe') (Join-Path $suiteRoot 'payload\installers\HealthMailer\HealthMailerSetup.exe')
    Copy-RequiredDirectory $printRxerPublish (Join-Path $suiteRoot 'payload\publish\printRxer')
    Copy-RequiredDirectory $healthMailerPublish (Join-Path $suiteRoot 'payload\publish\HealthMailer')
    Copy-RequiredDirectory '.\assets' (Join-Path $suiteRoot 'payload\assets')
    Copy-RequiredDirectory '.\docs' (Join-Path $suiteRoot 'docs')
    foreach ($tool in @(
        'Install-PrintRxerCapturePrinter.ps1',
        'Install-PrintRxerPortMonitor.ps1',
        'Install-PrintRxerDriver.ps1',
        'Install-PrintRxerQueue.ps1',
        'Uninstall-PrintRxerCapturePrinter.ps1',
        'Test-PrintRxerSuiteHealth.ps1',
        'New-PrintRxerSupportBundle.ps1'
    )) {
        Copy-RequiredFile (Join-Path '.\tools' $tool) (Join-Path $suiteRoot ('payload\tools\' + $tool))
    }
    Get-ChildItem -LiteralPath (Join-Path $suiteRoot 'payload\publish') -Filter '*.pdb' -File -Recurse -ErrorAction SilentlyContinue |
        Remove-Item -Force

    Write-Text (Join-Path $suiteRoot 'INSTALL-BUNDLE-README.txt') @"
printRxer suite release bundle
Version: $Version

Install:
  1. Extract this ZIP.
  2. Run PrintRxerSuiteInstaller.exe.

Do not build from source for a normal install.
Do not run PowerShell scripts directly unless instructed by support.

The GUI can install printRxer, install HealthMailer, install printer capture, validate the installation, open logs, create a support bundle, and start uninstall/repair actions.

Support smoke test:
  PrintRxerSuiteInstaller.exe --smoke-test
  This checks the release bundle layout without installing anything.
  Automation that needs an exit code should run it with Start-Process -Wait -PassThru.

Safety notes:
  printRxer creates validated handoff packages and does not send mail.
  HealthMailer sends through local Outlook/Healthmail on the sender machine.
  The support bundle excludes PDF payloads by default. Review logs before sharing them outside approved support channels.
"@

    Write-Step "Creating printRxer-only bundle."
    Copy-RequiredFile (Join-Path $installerPublish 'printRxerInstaller.exe') (Join-Path $printRxerRoot 'printRxerSetup.exe')
    Copy-RequiredDirectory $printRxerPublish (Join-Path $printRxerRoot 'payload\publish\printRxer')
    Copy-RequiredDirectory '.\assets\branding' (Join-Path $printRxerRoot 'payload\assets\branding')
    Copy-RequiredDirectory '.\assets\recipients' (Join-Path $printRxerRoot 'payload\assets\recipients')
    Copy-RequiredDirectory '.\assets\print-capture' (Join-Path $printRxerRoot 'payload\assets\print-capture')
    foreach ($tool in @(
        'Install-PrintRxerCapturePrinter.ps1',
        'Install-PrintRxerPortMonitor.ps1',
        'Install-PrintRxerDriver.ps1',
        'Install-PrintRxerQueue.ps1',
        'Uninstall-PrintRxerCapturePrinter.ps1'
    )) {
        Copy-RequiredFile (Join-Path '.\tools' $tool) (Join-Path $printRxerRoot ('payload\tools\' + $tool))
    }
    Copy-RequiredFile '.\docs\PrintRxer_HealthMailer_IT_QRG_v2.docx' (Join-Path $printRxerRoot 'printRxer-Install-Guide.docx')
    Get-ChildItem -LiteralPath (Join-Path $printRxerRoot 'payload\publish\printRxer') -Filter '*.pdb' -File -ErrorAction SilentlyContinue |
        Remove-Item -Force

    Write-Text (Join-Path $printRxerRoot 'INSTALL-UNINSTALL.txt') @"
printRxer install bundle
Version: $Version

Purpose:
  Install only the printing/capture side. This machine does not need Outlook.

Install from this folder:
  printRxerSetup.exe

Uninstall:
  printRxerSetup.exe
  Then choose Uninstall.
  If the GUI does not appear, run:
  printRxerSetup.exe --uninstall --quiet
  For a clean lab reset, run:
  printRxerSetup.exe --uninstall --remove-data --quiet

Notes:
  The installer asks for the handoff folder. Use the default local folder for same-machine testing, or choose/type a UNC path for a shared HealthMailer handoff.
  Installing or removing the printRxer printer requires administrator approval.
  ProgramData files are preserved by default. Use --remove-data only for a clean lab reset.
  Full guidance: printRxer-Install-Guide.docx
"@

    Write-Step "Creating HealthMailer-only bundle."
    Copy-RequiredFile (Join-Path $healthMailerInstallerPublish 'HealthMailerInstaller.exe') (Join-Path $healthMailerRoot 'HealthMailerSetup.exe')
    Copy-RequiredDirectory $healthMailerPublish (Join-Path $healthMailerRoot 'payload\publish\HealthMailer')
    Copy-RequiredDirectory '.\assets\branding' (Join-Path $healthMailerRoot 'payload\assets\branding')
    Copy-RequiredFile '.\docs\PrintRxer_HealthMailer_IT_QRG_v2.docx' (Join-Path $healthMailerRoot 'HealthMailer-Install-Guide.docx')
    Get-ChildItem -LiteralPath (Join-Path $healthMailerRoot 'payload\publish\HealthMailer') -Filter '*.pdb' -File -ErrorAction SilentlyContinue |
        Remove-Item -Force

    Write-Text (Join-Path $healthMailerRoot 'INSTALL-UNINSTALL.txt') @"
HealthMailer install bundle
Version: $Version

Purpose:
  Install only the Outlook/Healthmail sending side. This machine does not need printRxer.

Install from this folder:
  HealthMailerSetup.exe

Uninstall:
  HealthMailerSetup.exe
  Then choose Uninstall.
  If the GUI does not appear, run:
  HealthMailerSetup.exe --uninstall --quiet
  For a clean lab reset, run:
  HealthMailerSetup.exe --uninstall --remove-data --quiet

Notes:
  Outlook must be installed and signed in as the approved sender user if SendMail=true.
  The installer asks for the handoff folder. Use the same folder configured for printRxer.
  ProgramData files are preserved by default. Use --remove-data only for a clean lab reset.
  Full guidance: HealthMailer-Install-Guide.docx
"@

    $printRxerZip = Join-Path $outputRootFull ("printRxer-" + $safeVersion + ".zip")
    $healthMailerZip = Join-Path $outputRootFull ("HealthMailer-" + $safeVersion + ".zip")
    $suiteZip = Join-Path $outputRootFull ("printRxerSuite-" + $safeVersion + ".zip")

    New-ZipFromFolder $suiteRoot $suiteZip
    New-ZipFromFolder $printRxerRoot $printRxerZip
    New-ZipFromFolder $healthMailerRoot $healthMailerZip

    if (-not $SkipSmokeTest) {
        Write-Step "Running suite installer smoke test."
        Test-SuiteZipSmoke -ZipPath $suiteZip -StagingRoot $stagingRoot
    }

    Write-Step "Bundles created:"
    Write-Output $suiteZip
    Write-Output $printRxerZip
    Write-Output $healthMailerZip
} finally {
    Pop-Location
}
