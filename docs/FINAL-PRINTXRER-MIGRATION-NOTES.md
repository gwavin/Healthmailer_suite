# Final printRxer Migration Notes

This repository was created as the clean final home for the working HealthMailer / PrintRxer suite. The current code was copied from `gwavin/HealthMailer` into `gwavin/Healthmailer_suite` as a clean working-tree copy rather than a history-preserving migration.

## Product Direction

Final user-facing suite name: `printRxer suite`.

Final component language:

- `printRxer`: package creator that runs where prescription/PDF workflow happens. It creates validated handoff packages and does not send mail.
- `HealthMailer`: courier that runs where Outlook/Healthmail is installed and signed in. It validates handoff packages, sends through local Outlook COM, and archives evidence.

HealthMailer remains a named component. Do not rename HealthMailer to printRxer.

## Current Inventory

Current project and solution names:

- `PrintRxerSuite.slnx`
- `apps/PrintRxerV3/src/PrintRxerV3.csproj`
- `apps/PrintRxerV3/app/PrintRxerV3.App.csproj`
- `apps/PrintRxerV3/tests/PrintRxerV3.Tests.csproj`
- `apps/HealthMailer/HealthMailer.csproj`
- `tests/HealthMailer.Tests/HealthMailer.Tests.csproj`
- `installers/PrintRxerV3Installer/PrintRxerV3Installer.csproj`
- `installers/HealthMailerInstaller/HealthMailerInstaller.csproj`

Current executable names:

- legacy underscored v3 executable name
- `HealthMailer.exe`
- `PrintRxerV3Installer.exe`
- `HealthMailerInstaller.exe`

Current namespaces:

- `PrintRxerV3`
- `PrintRxerV3.App`
- `PrintRxerV3.Capture`
- `PrintRxerV3.Documents`
- `PrintRxerV3.Handoff`
- `PrintRxerV3.Metadata`
- `PrintRxerV3.Notifications`
- `PrintRxerV3.Packaging`
- `PrintRxerV3.Recipients`
- `HealthMailer`
- `PrintRxerV3Installer`
- `HealthMailerInstaller`

Current scheduled task names:

- `PrintRxerV3`
- `HealthMailer`

Current ProgramData paths:

- legacy underscored v3 ProgramData root and subfolders
- `C:\ProgramData\HealthMailer`
- `C:\ProgramData\HealthMailer\sent`
- `C:\ProgramData\HealthMailer\failed`
- `C:\ProgramData\HealthMailer\quarantine`
- `C:\ProgramData\HealthMailer\logs`

Current config file paths:

- legacy underscored v3 config path
- `C:\ProgramData\HealthMailer\healthmailer.settings.json`

Current log file paths:

- legacy underscored v3 log path
- `C:\ProgramData\HealthMailer\logs\healthmailer.log`

Current release bundle paths:

- `dist\PrintRxerV3-<version>.zip`
- `dist\HealthMailer-<version>.zip`
- `payload\publish\PrintRxerV3`
- `payload\publish\HealthMailer`
- `PrintRxerV3Setup.exe`
- `HealthMailerSetup.exe`

Current documentation headings and labels still use a mix of:

- `PrintRxer Suite`
- `PrintRxerV3`
- `PrintRxer v3`
- `PrintRxer_v3`
- legacy underscored v3 spelling
- `printRxer`
- `HealthMailer`

## Search Inventory

Initial search counts after copying and before broad rename:

- `PrintRxerV3`: 429
- `PrintRxer V3`: 0
- `PrintRxer_v3`: 2
- legacy underscored v3 spelling: 158
- `\bV3\b`: 0
- `\bv3\b`: 84
- `PrintRxerSuite`: 18
- `printRxer`: 75
- `HealthMailer`: 500

Search command used:

```powershell
rg -n "PrintRxerV3|PrintRxer V3|PrintRxer_v3|legacy underscored v3 spelling|\bV3\b|\bv3\b|PrintRxerSuite|printRxer|HealthMailer" -g '!bin/**' -g '!obj/**' -g '!publish/**' -g '!dist/**' -g '!tmp/**'
```

## Safe To Rename Now

These are user-facing or release-facing and should move toward final `printRxer` naming in the next pass:

- README and deployment language: `PrintRxerV3`, `PrintRxer v3`, `PrintRxer_v3`.
- GUI labels in printRxer installer and printRxer app.
- Publish output folder names under `publish\PrintRxerV3`.
- Release ZIP name `PrintRxerV3-<version>.zip`.
- Setup executable alias `PrintRxerV3Setup.exe`.
- Scheduled task name for new clean installs: `printRxer`.
- New install ProgramData root: `C:\ProgramData\printRxer`.
- New config file name: `printRxer.settings.json`.
- New log file name: `printRxer.log`.

## Retain Temporarily For Compatibility

These references should remain until an explicit compatibility/migration design is implemented:

- Existing pilot install path used the legacy underscored v3 ProgramData root.
- Existing pilot config file used the legacy underscored v3 settings filename.
- Existing scheduled task name `PrintRxerV3`.
- Existing process name used the legacy underscored v3 spelling when checking/removing old installs.
- Existing uninstall scripts and state checks that must remove or detect old pilot installations.
- Existing namespaces and project folder names, unless renamed in a coordinated code-level pass.
- Handoff package contract fields and package directory shape.

## Rename Decision

The first migration commit copied the working suite, documented the current naming surface, and confirmed build/test health in the new repository.

The first naming pass uses a clean final reset for new installs:

- New print-side executable: `printRxer.exe`.
- New print-side installer alias: `printRxerSetup.exe`.
- New publish folder: `publish\printRxer`.
- New release ZIP: `printRxer-<version>.zip`.
- New scheduled task for clean installs: `printRxer`.
- New ProgramData root: `C:\ProgramData\printRxer`.
- New config path: `C:\ProgramData\printRxer\config\printRxer.settings.json`.
- New log path: `C:\ProgramData\printRxer\logs\printRxer.log`.

The old `PrintRxerV3` / legacy underscored v3 names remain only where broad source-level renaming or old-pilot compatibility has not yet been intentionally changed:

- Source folder and namespace names under `apps/PrintRxerV3`.
- Installer project folder and namespace names under `installers/PrintRxerV3Installer`.
- Compatibility removal/detection of the old scheduled task `PrintRxerV3`.
- Compatibility process cleanup for old pilot process names.
- Historical planning notes and this migration note.

Recommended next step: add a suite-level GUI launcher/installer so the release ZIP can become one GUI-first entry point instead of two component ZIPs.
