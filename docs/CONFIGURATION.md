# HealthMailer Configuration

HealthMailer configuration lives at:

```text
C:\ProgramData\HealthMailer\healthmailer.settings.json
```

The easiest configuration path is:

```powershell
HealthMailer.exe --install
```

The installer asks for the handoff folder and optional ViewPoint/chart folder, writes config, registers the scheduled task, and validates the result.

## Settings

| Setting | Purpose | Default |
| --- | --- | --- |
| `HandoffRoot` | Folder watched for printRxer packages. Can be local or UNC. | `C:\ProgramData\printRxer\handoff` |
| `LocalRoot` | HealthMailer config, logs, archives, quarantine, and ledger root. | `C:\ProgramData\HealthMailer` |
| `PollIntervalSeconds` | Fallback polling interval in addition to file watcher events. | `5` |
| `StaleLockMinutes` | Age after which `.healthmailer.lock` may be retried. | `30` |
| `WriteHtmlSummary` | Enables self-contained `summary.html`. | `false` |
| `SendMail` | Sends through Outlook when true. Dry-run/no-send when false. | `true` |
| `Logging.MaxLogBytes` | Active log size cap before rotation. | `10485760` |
| `Logging.MaxLogFiles` | Number of rotated logs to keep. | `5` |
| `ChartCopy.Enabled` | Enables post-mail chart/ViewPoint copy. | `false` |
| `ChartCopy.DestinationRoot` | Chart/ViewPoint import folder. | empty |
| `ChartCopy.FileNameTemplate` | PDF filename template for chart copy. | `Rx-{MRN}-{PackageId}.pdf` |
| `ChartCopy.RequireMrn` | Fails chart copy if MRN is unavailable. | `true` |

Derived paths under `LocalRoot`:

```text
sent
failed
quarantine
logs
processed-ledger.jsonl
```

## Local Example

```json
{
  "HandoffRoot": "C:\\ProgramData\\printRxer\\handoff",
  "LocalRoot": "C:\\ProgramData\\HealthMailer",
  "PollIntervalSeconds": 5,
  "StaleLockMinutes": 30,
  "WriteHtmlSummary": false,
  "SendMail": true,
  "Logging": {
    "MaxLogBytes": 10485760,
    "MaxLogFiles": 5
  },
  "ChartCopy": {
    "Enabled": false,
    "DestinationRoot": "",
    "FileNameTemplate": "Rx-{MRN}-{PackageId}.pdf",
    "RequireMrn": true
  }
}
```

## UNC Example

```json
{
  "HandoffRoot": "\\\\server\\HealthMailerDrop$\\incoming",
  "LocalRoot": "C:\\ProgramData\\HealthMailer",
  "PollIntervalSeconds": 5,
  "StaleLockMinutes": 30,
  "WriteHtmlSummary": true,
  "SendMail": true,
  "Logging": {
    "MaxLogBytes": 10485760,
    "MaxLogFiles": 5
  },
  "ChartCopy": {
    "Enabled": true,
    "DestinationRoot": "\\\\server\\ViewPointImport$",
    "FileNameTemplate": "Rx-{MRN}-{PackageId}.pdf",
    "RequireMrn": true
  }
}
```

UNC paths are preferred over mapped drive letters for scheduled tasks.

Local ACL hardening is best-effort and applies only to local NTFS paths. HealthMailer does not create or harden unavailable UNC handoff roots; UNC share and NTFS permissions must be configured server-side by IT.

Local HealthMailer archives, failed packages, quarantine, logs, config, and ledger are restricted evidence stores. The local handoff/drop folder is the only category that may intentionally allow broader local write access for same-machine printRxer to HealthMailer compatibility.

Automatic archive deletion is disabled by default. Sent, failed, and quarantine packages are not cleaned up during normal processing.

printRxer has its own config at:

```text
C:\ProgramData\printRxer\config\printRxer.settings.json
```

Key fields include `IncomingRoot`, `ProcessedRoot`, `DeferredRoot`, `LocalOutboxRoot`, `PublishedRoot`, `FailedRoot`, `LogsRoot`, `HandoffRoot`, `PayloadStableSeconds`, `RequireJobOwnerMatch`, `AllowMissingSubmittingSid`, `RetryIntervalSeconds`, `MaxLogBytes`, and `MaxLogFiles`. The `HandoffRoot` value must match the folder watched by HealthMailer.

## Validate

```powershell
HealthMailer.exe --validate --config C:\ProgramData\HealthMailer\healthmailer.settings.json
```

If `SendMail=false`, validation does not require Outlook COM registration.

## Process Once

```powershell
HealthMailer.exe --process-once --config C:\ProgramData\HealthMailer\healthmailer.settings.json
```

## Scripted IT Configuration

For managed deployment, write `healthmailer.settings.json` using standard configuration management, then install the task:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Install-HealthMailerTask.ps1 `
  -ExePath 'C:\ProgramData\HealthMailer\app\HealthMailer.exe' `
  -ConfigPath 'C:\ProgramData\HealthMailer\healthmailer.settings.json'
```

