param(
    [string]$Version = "",
    [string]$OutputRoot = ".\dist",
    [switch]$SkipTests,
    [switch]$SkipPublish,
    [switch]$SkipSmokeTest,
    [switch]$CleanOutputRoot
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

    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    $sourceRoot = (Get-Item -LiteralPath $Source).FullName.TrimEnd('\') + '\'

    Get-ChildItem -LiteralPath $Source -Recurse -Directory -Force | ForEach-Object {
        $relative = $_.FullName.Substring($sourceRoot.Length)
        New-Item -ItemType Directory -Force -Path (Join-Path $Destination $relative) | Out-Null
    }

    Get-ChildItem -LiteralPath $Source -Recurse -File -Force | ForEach-Object {
        $relative = $_.FullName.Substring($sourceRoot.Length)
        $target = Join-Path $Destination $relative
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
        Copy-Item -LiteralPath $_.FullName -Destination $target -Force
    }
}

function Assert-SelfContainedExecutable {
    param(
        [string]$Path,
        [string]$Name
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "$Name executable was not found: $Path"
    }

    $file = Get-Item -LiteralPath $Path
    $minimumSelfContainedBytes = 50MB
    if ($file.Length -lt $minimumSelfContainedBytes) {
        throw "$Name appears to be framework-dependent because it is only $($file.Length) bytes. Release EXEs must be self-contained and must not require a separate .NET Desktop Runtime install."
    }
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

function Get-GitValue {
    param([string[]]$Arguments)

    $value = & git @Arguments 2>$null
    if ($LASTEXITCODE -ne 0) {
        return "unknown"
    }

    return (($value | Out-String).Trim())
}

function Write-BuildMetadata {
    param(
        [string]$Folder,
        [string]$Version,
        [string]$BuildTimeUtc,
        [string]$GitCommit,
        [string]$GitRef,
        [string]$DirtyState
    )

    Write-Text (Join-Path $Folder 'BUILD-METADATA.txt') @"
Version: $Version
BuildTimeUtc: $BuildTimeUtc
GitCommit: $GitCommit
GitRef: $GitRef
DirtyState: $DirtyState
"@
}

function Write-LatestArtifacts {
    param(
        [string]$OutputRoot,
        [string[]]$ZipPaths
    )

    $lines = foreach ($zipPath in $ZipPaths) {
        $item = Get-Item -LiteralPath $zipPath
        $hash = Get-FileHash -LiteralPath $zipPath -Algorithm SHA256
        "$($item.FullName)`t$($item.Length)`t$($item.LastWriteTimeUtc.ToString('O'))`t$($hash.Hash.ToLowerInvariant())"
    }

    Set-Content -LiteralPath (Join-Path $OutputRoot 'LATEST_RELEASE_ARTIFACTS.txt') -Value $lines -Encoding UTF8
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

function Copy-ReleaseDocumentation {
    param([string]$Destination)

    Copy-RequiredFile '.\healthmailer_release_doc_cleaned.html' (Join-Path $Destination 'healthmailer_release_doc.html')
    Copy-RequiredFile '.\printRxer_HealthMailer_User_Guide.html' (Join-Path $Destination 'printRxer_HealthMailer_User_Guide.html')

    foreach ($doc in @(
        'RECIPIENTS.md',
        'TROUBLESHOOTING.md',
        'OPERATIONS-RUNBOOK.md',
        'HANDOFF-CONTRACT.md',
        'HANDOFF-FOLDER-SETUP.md',
        'CONFIGURATION.md'
    )) {
        Copy-RequiredFile (Join-Path '.\docs' $doc) (Join-Path $Destination ('docs\' + $doc))
    }
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

    if ($CleanOutputRoot) {
        Write-Step "Cleaning old release ZIPs from output root."
        Get-ChildItem -LiteralPath $outputRootFull -File -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -like 'printRxerSuite-*.zip' -or $_.Name -like 'printRxer-*.zip' -or $_.Name -like 'HealthMailer-*.zip' } |
            Remove-Item -Force
    }

    $buildTimeUtc = (Get-Date).ToUniversalTime().ToString("O")
    $gitCommit = Get-GitValue @('rev-parse', 'HEAD')
    $gitRef = Get-GitValue @('branch', '--show-current')
    if ([string]::IsNullOrWhiteSpace($gitRef)) {
        $gitRef = Get-GitValue @('rev-parse', '--abbrev-ref', 'HEAD')
    }
    $dirtyState = if ([string]::IsNullOrWhiteSpace((git status --porcelain))) { "clean" } else { "dirty" }

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

        Assert-SelfContainedExecutable (Join-Path $printRxerPublish 'printRxer.exe') 'printRxer runtime'
        Assert-SelfContainedExecutable (Join-Path $healthMailerPublish 'HealthMailer.exe') 'HealthMailer runtime'
        Assert-SelfContainedExecutable (Join-Path $installerPublish 'printRxerInstaller.exe') 'printRxer installer'
        Assert-SelfContainedExecutable (Join-Path $healthMailerInstallerPublish 'HealthMailerInstaller.exe') 'HealthMailer installer'
        Assert-SelfContainedExecutable (Join-Path $suiteInstallerPublish 'PrintRxerSuiteInstaller.exe') 'printRxer suite installer'
    }

    $suiteRoot = Join-Path $stagingRoot ("printRxerSuite-" + $safeVersion)
    $printRxerRoot = Join-Path $stagingRoot ("printRxer-" + $safeVersion)
    $healthMailerRoot = Join-Path $stagingRoot ("HealthMailer-" + $safeVersion)
    foreach ($root in @($suiteRoot, $printRxerRoot, $healthMailerRoot)) {
        New-Item -ItemType Directory -Force -Path $root | Out-Null
    }

    Write-Step "Creating suite bundle."
    Copy-RequiredFile (Join-Path $suiteInstallerPublish 'PrintRxerSuiteInstaller.exe') (Join-Path $suiteRoot 'PrintRxerSuiteInstaller.exe')
    Copy-RequiredFile (Join-Path $installerPublish 'printRxerInstaller.exe') (Join-Path $suiteRoot 'payload\setup\printRxerSetup.exe')
    Copy-RequiredFile (Join-Path $healthMailerInstallerPublish 'HealthMailerInstaller.exe') (Join-Path $suiteRoot 'payload\setup\HealthMailerSetup.exe')
    Copy-RequiredDirectory $printRxerPublish (Join-Path $suiteRoot 'payload\publish\printRxer')
    Copy-RequiredDirectory $healthMailerPublish (Join-Path $suiteRoot 'payload\publish\HealthMailer')
    Copy-RequiredDirectory '.\assets' (Join-Path $suiteRoot 'payload\assets')
    Copy-ReleaseDocumentation $suiteRoot
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

The GUI can install a printRxer printing machine, install a HealthMailer sending machine, install both for a same-machine pilot, validate the installation, open logs, create a support bundle, and start Advanced / repair actions.
Printer capture is included in the normal printRxer printing-machine install. Printer-only actions are for repair/support.
The suite installer is the intended front door for IT handoff. Component setup EXEs are kept under payload\setup for the suite to run internally and for approved automation/support use.

Support smoke test:
  PrintRxerSuiteInstaller.exe --smoke-test
  This checks the release bundle layout without installing anything.
  Automation that needs an exit code should run it with Start-Process -Wait -PassThru.

Enterprise deployment examples:
  Run these from the extracted ZIP root. IT owns deployment tooling and must choose the correct Windows context.

  printRxer printing machine:
    Run in an administrator-capable context because printRxer installs the port monitor, XPS driver, and local printRxer queue.
  payload\setup\printRxerSetup.exe --quiet --handoff-root "\\server\HealthMailerDrop$\incoming"
  payload\setup\printRxerSetup.exe --validate

  HealthMailer sending machine:
    Run as the intended Outlook/Healthmail sender user. Do not assume a system-context install will work with Outlook COM.
  payload\setup\HealthMailerSetup.exe --quiet --handoff-root "\\server\HealthMailerDrop$\incoming" --send-mail true
  payload\setup\HealthMailerSetup.exe --validate

  Same-machine pilot:
    Use the same handoff folder for both commands. HealthMailer still needs to be configured for the Outlook/Healthmail sender user.

  Uninstall:
  payload\setup\printRxerSetup.exe --uninstall --quiet
  payload\setup\HealthMailerSetup.exe --uninstall --quiet

IT owns deployment tooling. Target machines do not need the SDK, WDK, Visual Studio, or C++ build tools.
The supplied EXEs are self-contained for Windows x64. Target machines should not need a separate .NET Desktop Runtime installation.

Safety notes:
  printRxer creates validated handoff packages and does not send mail.
  HealthMailer sends through local Outlook/Healthmail on the sender machine.
  The support bundle excludes PDF payloads by default, but package metadata, result evidence, logs, recipient details, MRNs/patient hints, package IDs, hashes, and audit metadata may still contain PHI. Keep support bundles on HSE-controlled machines or approved HSE storage, restrict them to administrators and approved support/audit personnel, and do not email or transfer them outside approved HSE support/governance channels.
"@
    Write-BuildMetadata $suiteRoot $Version $buildTimeUtc $gitCommit $gitRef $dirtyState

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
    Copy-RequiredFile '.\healthmailer_release_doc_cleaned.html' (Join-Path $printRxerRoot 'healthmailer_release_doc.html')
    Get-ChildItem -LiteralPath (Join-Path $printRxerRoot 'payload\publish\printRxer') -Filter '*.pdb' -File -ErrorAction SilentlyContinue |
        Remove-Item -Force

    Write-Text (Join-Path $printRxerRoot 'INSTALL-UNINSTALL.txt') @"
printRxer install bundle
Version: $Version

Purpose:
  Install the printRxer printing-machine side. This machine is where users print prescriptions to the local printRxer printer and does not need Outlook.

Install from this folder:
  printRxerSetup.exe

Quiet install:
  printRxerSetup.exe --quiet --handoff-root "\\server\HealthMailerDrop$\incoming"

Validate:
  printRxerSetup.exe --validate

Uninstall:
  printRxerSetup.exe
  Then choose Uninstall.
  If the GUI does not appear, run:
  printRxerSetup.exe --uninstall --quiet
  For a clean lab reset, run:
  printRxerSetup.exe --uninstall --remove-data --quiet

Notes:
  The installer asks for the handoff folder. Use the default local folder for same-machine testing, or choose/type a UNC path for a shared HealthMailer handoff.
  printRxer includes the application, watcher task, recipient cache handling, native port monitor, PrintRxer XPS driver, and local printer queue named printRxer.
  Installing or removing printRxer printer capture requires administrator approval. In this release, the printRxer component installer still runs as an administrator because app-file installation and printer capture are coupled; validation reports the scheduled task principal so IT can confirm task ownership.
  ProgramData files are preserved by default as audit/support evidence. Use --remove-data only for a clean lab reset.
  Full guidance: healthmailer_release_doc.html
"@
    Write-BuildMetadata $printRxerRoot $Version $buildTimeUtc $gitCommit $gitRef $dirtyState

    Write-Step "Creating HealthMailer-only bundle."
    Copy-RequiredFile (Join-Path $healthMailerInstallerPublish 'HealthMailerInstaller.exe') (Join-Path $healthMailerRoot 'HealthMailerSetup.exe')
    Copy-RequiredDirectory $healthMailerPublish (Join-Path $healthMailerRoot 'payload\publish\HealthMailer')
    Copy-RequiredDirectory '.\assets\branding' (Join-Path $healthMailerRoot 'payload\assets\branding')
    Copy-RequiredFile '.\healthmailer_release_doc_cleaned.html' (Join-Path $healthMailerRoot 'healthmailer_release_doc.html')
    Get-ChildItem -LiteralPath (Join-Path $healthMailerRoot 'payload\publish\HealthMailer') -Filter '*.pdb' -File -ErrorAction SilentlyContinue |
        Remove-Item -Force

    Write-Text (Join-Path $healthMailerRoot 'INSTALL-UNINSTALL.txt') @"
HealthMailer install bundle
Version: $Version

Purpose:
  Install only the Outlook/Healthmail sending side. This machine does not need printRxer.

Install from this folder:
  HealthMailerSetup.exe

Quiet install:
  HealthMailerSetup.exe --quiet --handoff-root "\\server\HealthMailerDrop$\incoming" --send-mail true

Validate:
  HealthMailerSetup.exe --validate

Uninstall:
  HealthMailerSetup.exe
  Then choose Uninstall.
  If the GUI does not appear, run:
  HealthMailerSetup.exe --uninstall --quiet
  For a clean lab reset, run:
  HealthMailerSetup.exe --uninstall --remove-data --quiet

Notes:
  Run setup as the intended Outlook/Healthmail sender user so the scheduled task and Outlook COM automation use the correct Windows profile.
  Outlook must be installed and signed in as the approved sender user if SendMail=true. Live sending also requires explicit installer-created approval in the HealthMailer configuration.
  The installer asks for the handoff folder. Use the same folder configured for printRxer.
  ProgramData files are preserved by default as audit/support evidence. Use --remove-data only for a clean lab reset.
  Full guidance: healthmailer_release_doc.html
"@
    Write-BuildMetadata $healthMailerRoot $Version $buildTimeUtc $gitCommit $gitRef $dirtyState

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

    $createdZips = @($suiteZip, $printRxerZip, $healthMailerZip)
    Write-LatestArtifacts -OutputRoot $outputRootFull -ZipPaths $createdZips

    Write-Step "Bundles created with SHA256:"
    foreach ($zip in $createdZips) {
        $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
        Write-Output "$zip  $hash"
    }
} finally {
    Pop-Location
}
