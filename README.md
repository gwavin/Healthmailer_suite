# HealthMailer / printRxer Suite

This project contains the two workstation-side components for the printRxer workflow.

```text
printRxer printing machine
  print to local printRxer printer -> recipient picker -> validated handoff package

HealthMailer sending machine
  handoff watcher -> package validation -> local Outlook/Healthmail send -> archive evidence
```

`printRxer` creates validated handoff packages and does not send mail. `HealthMailer` watches the configured handoff folder, validates complete packages, sends through local Outlook/Healthmail, and archives the result.

## Preferred Deployment Note

Use [healthmailer_release_doc_cleaned.html](healthmailer_release_doc_cleaned.html) as the preferred external-facing release and deployment note for third-party installers, IT deployment teams, and governance reviewers.

The remaining Markdown files are internal/support/engineering references. They are useful for development, troubleshooting, and operations, but should not be presented as equally authoritative external deployment instructions.

## Components

- `apps/PrintRxerV3`: printRxer source path retained for compatibility; final user-facing product name is `printRxer`.
- `apps/HealthMailer`: handoff watcher, validation, Outlook/Healthmail send, and evidence archival.
- `native` and `assets/print-capture`: local Windows printer-capture layer.
- `installers/PrintRxerSuiteInstaller`: GUI-first suite launcher for install, validation, support bundle, logs, and advanced repair/uninstall.
- `assets/recipients`: bundled recipient fallback data.
- `tests`: HealthMailer and printRxer contract/regression tests.

## Release Model

Generated release EXEs are self-contained Windows x64 executables. Target install machines should not need:

- .NET SDK
- WDK
- Visual Studio
- C++ build tools
- separate .NET Desktop Runtime installation

A development/build machine still needs the .NET SDK.

Create a release bundle from the project root:

```powershell
dotnet run --project .\tests\HealthMailer.Tests\HealthMailer.ContractTests.csproj
dotnet run --project .\apps\PrintRxerV3\tests\PrintRxerV3.ContractTests.csproj
powershell -ExecutionPolicy Bypass -File .\tools\New-PrintRxerSuiteReleaseBundle.ps1
```

The generated ZIPs are written under `dist\`. For normal installation, use `printRxerSuite-<version>.zip`, extract it, and run `PrintRxerSuiteInstaller.exe`.

Support can run `PrintRxerSuiteInstaller.exe --smoke-test` from the extracted bundle folder to validate the bundle layout without installing anything. It writes `PrintRxerSuiteInstaller.smoke-test.log` beside the suite installer where possible.

## Short Enterprise Examples

Run commands from the extracted suite ZIP root when IT deployment tooling requires quiet mode.

```powershell
.\printRxerSetup.exe --quiet --handoff-root "\\server\HealthMailerDrop$\incoming"
.\printRxerSetup.exe --validate

.\HealthMailerSetup.exe --quiet --handoff-root "\\server\HealthMailerDrop$\incoming" --send-mail true --sent-prescription-retention-days 14
.\HealthMailerSetup.exe --validate
```

Standard uninstall preserves local data/logs/archives by default:

```powershell
.\printRxerSetup.exe --uninstall --quiet
.\HealthMailerSetup.exe --uninstall --quiet
```

Clean lab reset is explicit:

```powershell
.\printRxerSetup.exe --uninstall --quiet --remove-data
.\HealthMailerSetup.exe --uninstall --quiet --remove-data
```

## Safety Notes

- `READY` prevents half-written package processing.
- SHA256 validation prevents mismatched PDF/metadata sends.
- The duplicate ledger prevents repeat sends.
- `result.json` and `summary.txt` provide terminal evidence for sent, failed, and quarantined packages.
- printRxer installation/removal may require administrator/UAC approval because printer capture includes a port monitor, XPS driver, and local `printRxer` printer queue.
- printRxer is installed once per machine. Its watcher scheduled task is registered for all interactive Windows users so shared workstations start a user-scoped watcher when each user logs on.
- HealthMailer must be installed and run as the intended Outlook/Healthmail sender Windows user; Outlook COM automation depends on that signed-in user profile.

## Licensing Status

This project is currently maintained as a development/prototype repository. No public open-source licence has yet been applied. Do not redistribute or reuse without explicit permission from the repository owner.
