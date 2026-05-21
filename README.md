# printRxer suite

This repository contains the two workstation-side components for the printRxer workflow.

```text
printRxer machine
  print/PDF capture -> picker -> validated handoff package

HealthMailer machine
  handoff watcher -> Outlook COM send -> optional chart-folder copy
```

The applications are intentionally built and deployed as separate EXEs. `printRxer` can be installed by itself on the machine where prescriptions are printed and does not require Outlook. `HealthMailer` can be installed by itself on the machine/account that is signed in to Outlook/Healthmail and does not require printRxer.

## Licensing Status

This project is currently maintained as a development/prototype repository.
No public open-source licence has yet been applied.
Do not redistribute or reuse without explicit permission from the repository owner.

## Projects

- `apps/PrintRxerV3`: currently retained source path for the printRxer package creator. It does not send mail.
- `apps/HealthMailer`: consumes ready handoff packages, validates them, sends via local Outlook COM, and archives them.
- `native` and `assets/print-capture`: the Windows print-capture layer that creates the local `printRxer` printer queue and writes captured jobs into the printRxer incoming folder.
- `installers/PrintRxerSuiteInstaller`: the GUI-first release launcher for normal installs, validation, support bundles, and uninstall/repair entry points.
- `assets/recipients` and `assets/branding`: baseline picker data copied into ProgramData during printRxer install if local files do not already exist.
- `tests/HealthMailer.Tests`: HealthMailer contract and processing tests.
- `apps/PrintRxerV3/tests`: printRxer tests.

## Start By Role

Pilot owner or governance reviewer:

- [docs/QUICKSTART.md](docs/QUICKSTART.md)
- [DEPLOYMENT.md](DEPLOYMENT.md)
- [SECURITY.md](SECURITY.md)
- [docs/HANDOFF-CONTRACT.md](docs/HANDOFF-CONTRACT.md)
- [docs/HANDOFF-FOLDER-SETUP.md](docs/HANDOFF-FOLDER-SETUP.md)

Desktop engineering or installer reviewer:

- [DEPLOYMENT.md](DEPLOYMENT.md)
- [UNINSTALL.md](UNINSTALL.md)
- [apps/PrintRxerV3/INSTALL.md](apps/PrintRxerV3/INSTALL.md)
- [apps/PrintRxerV3/UNINSTALL.md](apps/PrintRxerV3/UNINSTALL.md)
- [docs/CONFIGURATION.md](docs/CONFIGURATION.md)
- [docs/RELEASE-CHECKLIST.md](docs/RELEASE-CHECKLIST.md)

Support analyst:

- [docs/OPERATIONS-RUNBOOK.md](docs/OPERATIONS-RUNBOOK.md)
- [docs/TROUBLESHOOTING.md](docs/TROUBLESHOOTING.md)
- [UNINSTALL.md](UNINSTALL.md)

Security reviewer:

- [SECURITY.md](SECURITY.md)
- [docs/HANDOFF-FOLDER-SETUP.md](docs/HANDOFF-FOLDER-SETUP.md)
- [docs/HANDOFF-CONTRACT.md](docs/HANDOFF-CONTRACT.md)

Developer:

- [PrintRxerSuite.slnx](PrintRxerSuite.slnx)
- [apps/HealthMailer/README.md](apps/HealthMailer/README.md)
- [apps/PrintRxerV3/README.md](apps/PrintRxerV3/README.md)
- [apps/PrintRxerV3/CONFIGURATION.md](apps/PrintRxerV3/CONFIGURATION.md)

## Shared Contract

The two executables communicate through a local or shared folder containing package directories with:

```text
request.json
prescription.pdf
request.sha256
summary.txt
READY
```

HealthMailer processes only non-staging package directories that contain `READY`, and it validates the PDF signature and SHA256 before sending. Each terminal package receives `result.json` plus `summary.txt`; optional HTML summaries are self-contained. HealthMailer also keeps `processed-ledger.jsonl` to prevent duplicate sends by package ID or completed package hash.

## Build And Test

The suite targets `net8.0-windows`, which is the preferred deployable runtime for hospital IT environments. Project files allow major-version roll-forward so local development can still run on newer installed Windows Desktop runtimes.

```powershell
dotnet test .\PrintRxerSuite.slnx
```

Publish HealthMailer:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Publish-HealthMailer.ps1
```

Publish printRxer:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Publish-printRxer.ps1
```

Create a prebuilt install bundle for IT:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\New-PrintRxerSuiteReleaseBundle.ps1
```

The generated ZIPs are written under `dist\`:

- `printRxerSuite-<version>.zip`: GUI-first release bundle. Extract it and run `PrintRxerSuiteInstaller.exe`.
- `printRxer-<version>.zip`: minimum files to install/uninstall the print/picker side.
- `HealthMailer-<version>.zip`: minimum files to install/uninstall the Outlook sending side.

Generated app EXEs stay out of git; use GitHub Releases for distribution. Normal installs should use the suite ZIP and GUI launcher. Component ZIPs and PowerShell scripts remain support/internal paths unless support explicitly instructs otherwise.

## Release Bundles

For install-only handoff to IT, publish a GitHub Release rather than asking the target machine to rebuild from source. Pushing a tag such as `v0.1.0` runs `.github/workflows/release-bundle.yml`, builds/tests/publishes the suite, creates the suite ZIP plus the two role-specific ZIPs, and attaches them to the release.

The release bundle is intended for machines without Visual Studio, the .NET SDK, or WDK. Rebuilding native printer components still requires the appropriate build tools on a development machine.

## Install Both Apps

HealthMailer and printRxer may be installed on the same machine or on different machines. Both installers/configs ask for the same handoff folder, which may be local or UNC. Use UNC paths directly for two-machine deployments; do not rely on mapped drives.

Normal install path:

1. Download `printRxerSuite-<version>.zip` from the GitHub Release.
2. Extract the ZIP.
3. Run `PrintRxerSuiteInstaller.exe`.
4. Use the GUI to install printRxer, install HealthMailer, install the printer capture component, and validate the installation.

Support can run `PrintRxerSuiteInstaller.exe --smoke-test` from the extracted ZIP to verify the release bundle layout without installing anything. Automation that needs the exit code should run it with `Start-Process -Wait -PassThru`.

This step requires Administrator/UAC approval because it installs a native Windows port monitor, a local XPS driver, and the `printRxer` printer queue. The watcher and the printer layer are separate deliberately: printRxer can be tested with imported captures, but live printing requires the capture printer.

printRxer writes `C:\ProgramData\printRxer\config\printRxer.settings.json`, builds packages in a durable local outbox first, then publishes complete `READY` packages to the configured handoff folder. If the share is down, packages remain local and are retried at the configured interval.

## Uninstall Both Apps

Use `PrintRxerSuiteInstaller.exe`, then choose `Uninstall / repair`. Standard uninstall preserves local data/logs/archives by default.

## Deployment Shape

Deploy `printRxer` where the user prints and selects the recipient. Deploy `HealthMailer` where the Outlook profile is available and authorised to send Healthmail. The handoff folder can be local or shared, but if shared it must be locked down by server-side ACLs to the minimum necessary writer, watcher, and support identities.

