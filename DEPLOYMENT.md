# printRxer suite Deployment

The suite is deployed as two separate executables connected by a local or shared handoff folder.

```text
printRxer
  runs where users print prescriptions to the local printRxer printer
  includes the printRxer app, watcher task, port monitor, XPS driver, and printer queue
  creates request.json, prescription.pdf, request.sha256, summary.txt, READY
  does not send mail

HealthMailer
  runs where Outlook/Healthmail is installed and signed in
  watches the handoff folder
  validates packages, sends through local Outlook COM, optionally copies to chart/ViewPoint
```

## Deployment Scenarios

| Scenario | printRxer location | HealthMailer location | Handoff folder |
| --- | --- | --- | --- |
| Same machine pilot | Clinical workstation | Same workstation | Local folder |
| Two-machine pilot | Clinical workstation or Citrix host | Outlook-enabled workstation | Locked-down UNC |
| Shared cart without Outlook | Clinical cart | Approved Outlook sender machine | Locked-down UNC |
| Site-managed sender | Clinical workstation | Site-approved mailbox machine | Locked-down UNC |
| Developer test | Test PC | Same or second test PC | Local or test share |

Do not use an RDP/Citrix redirected `printRxer` queue for the print-capture path. Install printRxer where users print prescriptions to the local `printRxer` printer queue.

Machine A can run printRxer only. Machine B can run HealthMailer only. They must be configured with the same handoff folder, for example `\\server\HealthMailerDrop$\incoming`. printRxer does not require Outlook. HealthMailer does not require printRxer.

The supplied scheduled-task installers create per-user interactive tasks. HealthMailer must run as the Windows user whose Outlook/Healthmail profile is available and approved for sending. printRxer also runs per user deliberately: the picker is interactive, and the per-user task helps preserve the user/session boundary for capture processing.

## Prerequisites

- Windows x64 workstation or server capable of running Windows Desktop applications. The supplied release EXEs are self-contained and should not require a separate .NET Desktop Runtime installation.
- Outlook installed and signed in on the HealthMailer machine when `SendMail=true`.
- Local or UNC handoff folder agreed with IT.
- Folder ACLs configured before live PHI testing.
- Approved sender mailbox/account and Healthmail governance decision.
- Central recipient list location agreed with IT. By default this is derived from the same handoff folder as `<HandoffRoot>\recipients\recipients.csv`.

## Operational Protections

The suite preserves the lessons from the original printRxer testing:

- Outlook is not required on every printing workstation.
- F3/web-only Office shared carts can hand off to a HealthMailer machine.
- `READY` prevents half-package processing.
- SHA256 validation prevents mismatched PDF/metadata packages.
- The duplicate ledger prevents duplicate sends.
- Mail happens before chart/ViewPoint copy.
- Failed/quarantined packages include `result.json` and `summary.txt`.
- HealthMailer logs are capped and rotated.
- printRxer logs are capped and rotated.
- printRxer does not process another user's captured job by default.
- printRxer waits for capture payload stability before opening the picker.
- printRxer keeps a durable local outbox and retries handoff publication when a UNC share is unavailable.
- HealthMailer keeps running and polling when the watched UNC folder is temporarily unavailable.
- printRxer opens the recipient picker from memory, local cache, or bundled fallback; central recipient refresh happens in the background and does not block the picker.

## Build

From the repository root:

```powershell
dotnet test .\PrintRxerSuite.slnx
powershell -ExecutionPolicy Bypass -File .\tools\New-PrintRxerSuiteReleaseBundle.ps1
```

This creates `dist\printRxerSuite-<version>.zip` for normal installation. The target install machine should not need the SDK, WDK, Visual Studio, or C++ build tools.

## GUI-First Release Install

Normal user-facing install path:

1. Download the release ZIP.
2. Extract it.
3. Run `PrintRxerSuiteInstaller.exe`.
4. Choose one primary role: `Install printRxer printing machine`, `Install HealthMailer sending machine`, or `Same-machine pilot: install both`.
5. Use validation, logs, support bundle, and Advanced / repair actions as needed.

Do not ask normal users to run PowerShell scripts directly. Scripts in `payload\tools` are support internals used by the GUI or by instructed support sessions. Printer capture repair is an Advanced / repair action, not a separate normal install role.

Support can run `PrintRxerSuiteInstaller.exe --smoke-test` from the extracted ZIP to verify the bundle layout without installing any component. It writes `PrintRxerSuiteInstaller.smoke-test.log` to the extracted bundle folder where possible. Automation that needs the exit code should run it with `Start-Process -Wait -PassThru`.

Component ZIPs may still be published for targeted support, but the suite ZIP is the preferred release path.

## Enterprise Deployment Commands

This project does not provide a deployment platform and does not include Intune, SCCM, GPO, RMM, or code-signing logic. IT should deploy the extracted release bundle using existing local tooling. Target machines do not need the .NET SDK, WDK, Visual Studio, or C++ build tools.

Run commands from the extracted suite ZIP root. IT owns deployment tooling and must choose the correct Windows context.

For HealthMailer, run setup as the intended Outlook/Healthmail sender user. Do not assume a system-context install will work with Outlook COM, because the scheduled task and Outlook profile are user/session-specific.

For printRxer, quiet install may still need administrator rights because this release keeps app-file installation and printer capture in one component installer. Validation reports the scheduled task principal so IT can confirm ownership after install.

printRxer printing machine:

```powershell
.\printRxerSetup.exe --quiet --handoff-root "\\server\HealthMailerDrop$\incoming"
.\printRxerSetup.exe --validate
```

HealthMailer sending machine:

```powershell
.\HealthMailerSetup.exe --quiet --handoff-root "\\server\HealthMailerDrop$\incoming" --send-mail true
.\HealthMailerSetup.exe --validate
```

Same-machine pilot:

Use the same handoff folder for both commands. HealthMailer still needs to be configured under the intended Outlook/Healthmail sender user.

```powershell
.\printRxerSetup.exe --quiet --handoff-root "C:\ProgramData\printRxer\handoff"
.\HealthMailerSetup.exe --quiet --handoff-root "C:\ProgramData\printRxer\handoff" --send-mail false
.\printRxerSetup.exe --validate
.\HealthMailerSetup.exe --validate
```

Quiet uninstall:

```powershell
.\printRxerSetup.exe --uninstall --quiet
.\HealthMailerSetup.exe --uninstall --quiet
```

Clean lab reset, only when explicitly approved:

```powershell
.\printRxerSetup.exe --uninstall --quiet --remove-data
.\HealthMailerSetup.exe --uninstall --quiet --remove-data
```

Exit codes:

| Code | Meaning |
| --- | --- |
| 0 | Success |
| 1 | General failure |
| 2 | Missing required argument |
| 3 | Insufficient permissions |
| 4 | Handoff folder unavailable |
| 5 | Outlook/HealthMailer prerequisite failed |
| 6 | Printer capture install failed |
| 7 | Validation failed |
| 8 | Cancelled by user |

Quiet mode logs to:

```text
C:\ProgramData\printRxer\logs\printRxerInstaller.log
C:\ProgramData\HealthMailer\logs\HealthMailerInstaller.log
```

## Developer Publish Notes

printRxer can be built from its app project during development:

```powershell
dotnet publish .\apps\PrintRxerV3\app\PrintRxerV3.App.csproj -c Release -r win-x64 --self-contained true
```

## HealthMailer Install

Use the suite launcher from the release ZIP. It starts the HealthMailer component installer and asks for the handoff folder and optional ViewPoint/chart folder.

The wizard asks for:

1. The printRxer handoff folder.
2. Optional ViewPoint/chart import folder.

It writes:

```text
C:\ProgramData\HealthMailer\healthmailer.settings.json
```

and registers the scheduled task:

```text
HealthMailer
```

## Recipient List Deployment

The preferred recipient list is derived from the printRxer handoff folder:

```text
<HandoffRoot>\recipients\recipients.csv
```

During printRxer installation, the installer always places the bundled fallback at:

```text
C:\ProgramData\printRxer\data\recipients\bundled-recipients.csv
```

If the handoff folder is reachable and writable, the installer attempts to create `<HandoffRoot>\recipients` and seed `recipients.csv` from the bundled fallback only when the central file is missing. Existing central files are not overwritten. If the handoff folder is unavailable or read-only, installation can continue with local fallback; IT can create or update the central file later.

Runtime printRxer access to the central file is read-only. The runtime user should be able to write only local cache/status files under:

```text
C:\ProgramData\printRxer\data\recipients
```

See [docs/RECIPIENTS.md](docs/RECIPIENTS.md) for schema, ACLs, status files, and the IT update process.

## Validation

Dry-run/no-send validation:

```json
{
  "SendMail": false
}
```

```powershell
HealthMailer.exe --validate --config C:\ProgramData\HealthMailer\healthmailer.settings.json
HealthMailer.exe --process-once --config C:\ProgramData\HealthMailer\healthmailer.settings.json
```

Send-enabled validation requires Outlook COM registration:

```json
{
  "SendMail": true
}
```

## Rollback

Run `PrintRxerSuiteInstaller.exe`, choose `Advanced / repair`, and uninstall the relevant component. Component uninstall preserves local data by default. Remove local data only when explicitly approved by governance/support.


