# printRxer suite Deployment

The suite is deployed as two separate executables connected by a local or shared handoff folder.

```text
printRxer
  runs where the prescription print/PDF workflow happens
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

Do not use an RDP/Citrix redirected `printRxer` queue for the print-capture path. Install printRxer where the print job or imported PDF is created.

Machine A can run printRxer only. Machine B can run HealthMailer only. They must be configured with the same handoff folder, for example `\\server\HealthMailerDrop$\incoming`. printRxer does not require Outlook. HealthMailer does not require printRxer.

The supplied scheduled-task installers create per-user interactive tasks. HealthMailer must run as the Windows user whose Outlook/Healthmail profile is available and approved for sending. printRxer also runs per user deliberately: the picker is interactive, and the per-user task helps preserve the user/session boundary for capture processing.

## Prerequisites

- Windows workstation or server capable of running `.NET 8` Windows Desktop apps.
- Outlook installed and signed in on the HealthMailer machine when `SendMail=true`.
- Local or UNC handoff folder agreed with IT.
- Folder ACLs configured before live PHI testing.
- Approved sender mailbox/account and Healthmail governance decision.

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
4. Use the launcher to install printRxer, install HealthMailer, install printer capture, validate installation, open logs, create a support bundle, or start uninstall/repair.

Do not ask normal users to run PowerShell scripts directly. Scripts in `payload\tools` are support internals used by the GUI or by instructed support sessions.

Component ZIPs may still be published for targeted support, but the suite ZIP is the preferred release path.

## Developer Publish Notes

printRxer can be built from its app project during development:

```powershell
dotnet publish .\apps\printRxer\app\printRxer.App.csproj -c Release -r win-x64 --self-contained true
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

Run `PrintRxerSuiteInstaller.exe`, choose `Uninstall / repair`, and uninstall the relevant component. Component uninstall preserves local data by default. Remove local data only when explicitly approved by governance/support.


