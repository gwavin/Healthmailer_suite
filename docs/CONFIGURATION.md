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
| `SentPrescriptionRetentionDays` | Days to keep `prescription.pdf` in successful `sent` archives. `0` keeps sent PDFs indefinitely. Audit files remain. | `14` |
| `WriteHtmlSummary` | Enables self-contained `summary.html`. | `false` |
| `SendMail` | Sends through Outlook when true and live sending is explicitly approved. Dry-run/no-send when false. | `false` |
| `ConfigCreatedByInstaller` | Marker written by HealthMailer setup for installed configurations. | `false` |
| `LiveSendingApproved` | Required marker for live Outlook sending. Quiet install sets this only when `--send-mail true` is explicitly requested. | `false` |
| `AllowedRecipientDomains` | Final HealthMailer send-boundary recipient domain allow-list. | `healthmail.ie`, `hse.ie`, `nmh.ie`, `rotunda.ie` |
| `Logging.MaxLogBytes` | Active log size cap before rotation. | `10485760` |
| `Logging.MaxLogFiles` | Number of rotated logs to keep. | `5` |
| `ChartCopy.Enabled` | Enables post-mail chart/ViewPoint copy. | `false` |
| `ChartCopy.DestinationRoot` | Chart/ViewPoint import folder. | empty |
| `ChartCopy.FileNameTemplate` | PDF filename template for chart copy. | `Rx-{MRN}-{PackageId}.pdf` |
| `ChartCopy.RequireMrn` | Fails chart copy if MRN is unavailable. | `true` |

Derived paths under `LocalRoot`:

```text
sent
validated-no-send
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
  "SentPrescriptionRetentionDays": 14,
  "WriteHtmlSummary": false,
  "SendMail": false,
  "ConfigCreatedByInstaller": true,
  "LiveSendingApproved": false,
  "AllowedRecipientDomains": [
    "healthmail.ie",
    "hse.ie",
    "nmh.ie",
    "rotunda.ie"
  ],
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
  "SentPrescriptionRetentionDays": 14,
  "WriteHtmlSummary": true,
  "SendMail": true,
  "ConfigCreatedByInstaller": true,
  "LiveSendingApproved": true,
  "AllowedRecipientDomains": [
    "healthmail.ie",
    "hse.ie",
    "nmh.ie",
    "rotunda.ie"
  ],
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

Successful `sent` archives keep small audit files, but `prescription.pdf` is removed after `SentPrescriptionRetentionDays` days. The installer default is 14 days. Set `SentPrescriptionRetentionDays` to `0` only when governance approves retaining successful sent prescription PDFs indefinitely.

Automatic deletion of failed and quarantine archives is disabled by default. Failed and quarantine packages are not cleaned up during normal processing because they require explicit review.

printRxer has its own config at:

```text
C:\ProgramData\printRxer\config\printRxer.settings.json
```

Key fields include `IncomingRoot`, `ProcessedRoot`, `DeferredRoot`, `LocalOutboxRoot`, `PublishedRoot`, `FailedRoot`, `LogsRoot`, `HandoffRoot`, `PayloadStableSeconds`, `RequireJobOwnerMatch`, `AllowMissingSubmittingSid`, `RetryIntervalSeconds`, `MaxLogBytes`, and `MaxLogFiles`. The `HandoffRoot` value must match the folder watched by HealthMailer.

## Validate

```powershell
HealthMailer.exe --validate --config C:\ProgramData\HealthMailer\healthmailer.settings.json
```

If `SendMail=false`, validation does not require Outlook COM registration and processed packages are archived under `validated-no-send` with `MailSent=false`. If `SendMail=true`, `LiveSendingApproved=true` is required and validation checks Outlook COM registration.

## Process Once

```powershell
HealthMailer.exe --process-once --config C:\ProgramData\HealthMailer\healthmailer.settings.json
```

Quiet install can set sent prescription PDF retention explicitly:

```powershell
.\HealthMailerSetup.exe --quiet --handoff-root "\\server\HealthMailerDrop$\incoming" --send-mail true --sent-prescription-retention-days 14
```

## Scripted IT Configuration

For managed deployment, write `healthmailer.settings.json` using standard configuration management, then install the task:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Install-HealthMailerTask.ps1 `
  -ExePath 'C:\ProgramData\HealthMailer\app\HealthMailer.exe' `
  -ConfigPath 'C:\ProgramData\HealthMailer\healthmailer.settings.json'
```

