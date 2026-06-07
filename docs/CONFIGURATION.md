# HealthMailer Configuration

HealthMailer configuration lives at:

```text
C:\ProgramData\HealthMailer\healthmailer.settings.json
```

The easiest configuration path is:

```powershell
HealthMailer.exe --install
```

The installer asks for the handoff folder, writes config, registers the scheduled task, and validates the result.

Chart/ViewPoint copy is removed/deferred in the current release. It is not a supported configuration option.

## Settings

| Setting | Purpose | Default |
| --- | --- | --- |
| `HandoffRoot` | Folder watched for printRxer packages. Can be local or UNC. | `C:\ProgramData\printRxer\handoff` |
| `LocalRoot` | HealthMailer config, logs, archives, quarantine, and ledger root. | `C:\ProgramData\HealthMailer` |
| `PollIntervalSeconds` | Fallback polling interval in addition to file watcher events. | `5` |
| `StaleLockMinutes` | Legacy compatibility field retained in existing config files. Current package claims use the PID stored in `.healthmailer.lock`; this value no longer controls lock expiry. | `30` |
| `SentPrescriptionRetentionDays` | Days to keep `prescription.pdf` in successful `sent` archives. `0` keeps sent PDFs indefinitely. Audit files remain. | `14` |
| `WriteHtmlSummary` | Enables self-contained `summary.html`. | `false` |
| `SendMail` | Sends through Outlook when true and live sending is explicitly approved. Dry-run/no-send when false. | `false` |
| `ConfigCreatedByInstaller` | Marker written by HealthMailer setup for installed configurations. | `false` |
| `LiveSendingApproved` | Required marker for live Outlook sending. Quiet install sets this only when `--send-mail true` is explicitly requested. | `false` |
| `AllowedRecipientDomains` | Final HealthMailer send-boundary recipient domain allow-list. | `healthmail.ie`, `hse.ie`, `nmh.ie`, `rotunda.ie` |
| `Logging.MaxLogBytes` | Active log size cap before rotation. | `10485760` |
| `Logging.MaxLogFiles` | Number of rotated logs to keep. | `5` |

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
  }
}
```

Mapped drives are not supported for scheduled-task handoff paths. Use UNC paths directly.

## Local Security Boundaries

Security hardening is built into installer and runtime setup paths. printRxer protected application binaries are held under `%ProgramFiles%`, while SYSTEM-loaded print-capture assets are held under `%SystemRoot%\System32`. For this release, HealthMailer application binaries remain at `C:\ProgramData\HealthMailer\app`; the installer treats that specific folder as a protected application-binary boundary and fails installation if its restrictive ACL cannot be applied and verified. Other `%ProgramData%` locations hold mutable local data, logs, archives, configuration, ledger, recipient cache, and outbox material.

The printRxer installer validates that its protected application workspace resolves under Program Files and that its data directories resolve under ProgramData. These roots must remain separate and non-nested. For printRxer local recipient/cache paths and SYSTEM-loaded print-capture components, ACL hardening is part of the installation safety boundary. Environments that prevent explicit restriction and verification of those permissions fail installation with a `FatalSecurityException`.

HealthMailer applies local NTFS hardening to its ProgramData evidence, config, log, archive, and ledger folders where possible. UNC handoff shares are not secured by application code. Server-side share and NTFS permissions must be configured and verified by IT before live PHI testing.

Group Policy and endpoint security controls must not prevent installer ACL application on local ProgramData or System32 print-capture assets. Any exception must be investigated rather than bypassed.

Local HealthMailer archives, failed packages, quarantine, logs, config, and ledger are restricted evidence stores. The local handoff/drop folder is the only category that may intentionally allow broader local write access for same-machine printRxer to HealthMailer compatibility.

Successful `sent` archives keep small audit files, but `prescription.pdf` is removed after `SentPrescriptionRetentionDays` days. The installer default is 14 days. Set `SentPrescriptionRetentionDays` to `0` only when governance approves retaining successful sent prescription PDFs indefinitely.

Automatic deletion of failed and quarantine archives is disabled by default. Failed and quarantine packages are not cleaned up during normal processing because they require explicit review.

HealthMailer writes its current process ID into `.healthmailer.lock`. A lock owned by an active process named `HealthMailer` is left alone; a dead PID or PID now owned by another process is reclaimed immediately. Malformed or unreadable lock ownership is treated as active and left for support review.

`processed-ledger.jsonl` remains the full audit record and must not be manually truncated. To bound memory use, the active duplicate cache loads valid timestamped sent records from the previous 30 days. Legacy records without a valid timestamp remain in the cache fail-closed.

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
.\payload\setup\HealthMailerSetup.exe --quiet --handoff-root "\\server\HealthMailerDrop$\incoming" --send-mail true --sent-prescription-retention-days 14
```

## Scripted IT Configuration

For managed deployment, write `healthmailer.settings.json` using standard configuration management, then install the task:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Install-HealthMailerTask.ps1 `
  -ExePath 'C:\ProgramData\HealthMailer\app\HealthMailer.exe' `
  -ConfigPath 'C:\ProgramData\HealthMailer\healthmailer.settings.json'
```

