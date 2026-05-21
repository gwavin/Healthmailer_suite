# Operations Runbook

## Locations

| Item | Default path |
| --- | --- |
| Config | `C:\ProgramData\HealthMailer\healthmailer.settings.json` |
| Logs | `C:\ProgramData\HealthMailer\logs` |
| Sent archive | `C:\ProgramData\HealthMailer\sent` |
| Failed archive | `C:\ProgramData\HealthMailer\failed` |
| Quarantine | `C:\ProgramData\HealthMailer\quarantine` |
| Ledger | `C:\ProgramData\HealthMailer\processed-ledger.jsonl` |
| PrintRxerV3 config | `C:\ProgramData\printrxer_v3\config\printrxer_v3.settings.json` |
| PrintRxerV3 pending outbox | `C:\ProgramData\printrxer_v3\pending-outbox` |
| PrintRxerV3 published outbox | `C:\ProgramData\printrxer_v3\published` |
| PrintRxerV3 logs | `C:\ProgramData\printrxer_v3\logs` |

## Scheduled Task

```powershell
Get-ScheduledTask -TaskName HealthMailer
Get-ScheduledTaskInfo -TaskName HealthMailer
Start-ScheduledTask -TaskName HealthMailer
Stop-ScheduledTask -TaskName HealthMailer
```

## Validate Configuration

```powershell
HealthMailer.exe --validate --config C:\ProgramData\HealthMailer\healthmailer.settings.json
```

## Process Waiting Packages Once

```powershell
HealthMailer.exe --process-once --config C:\ProgramData\HealthMailer\healthmailer.settings.json
```

## Inspect Logs

```powershell
Get-Content C:\ProgramData\HealthMailer\logs\healthmailer.log -Tail 80
Get-ChildItem C:\ProgramData\HealthMailer\logs\healthmailer*.log
```

HealthMailer rotates logs as `healthmailer.log`, `healthmailer.1.log`, `healthmailer.2.log`, and so on. Defaults cap each log at 10 MB and keep five rotated files.

PrintRxerV3 rotates logs as `printrxer_v3.log`, `printrxer_v3.1.log`, `printrxer_v3.2.log`, and so on. Defaults cap each log at 5 MB and keep three rotated files.

## Evidence Retention

HealthMailer does not automatically delete `sent`, `failed`, or `quarantine` archives during normal processing. Treat these folders as operational evidence. Failed and quarantined packages should be reviewed explicitly before any manual removal.

Do not manually edit or truncate `processed-ledger.jsonl`. It is duplicate-send safety evidence. A single HealthMailer runtime should own one `LocalRoot` and ledger.

## Interpret Outcomes

| Outcome | Action |
| --- | --- |
| `Sent` | Normal terminal state. |
| `ValidationFailed` | Inspect `request.json`, `request.sha256`, PDF hash, and package generation source. |
| `Duplicate` | Do not resend without governance approval; inspect ledger and prior sent package. |
| `MailFailed` | Check Outlook is running/signed in, sender account policy, and COM registration. |
| `ChartCopyFailed` | Mail may already have been sent; check ViewPoint/chart folder ACLs and naming rules. |
| `Failed` | Collect logs and package evidence. |

## PrintRxerV3 Deferred Captures

If a captured print does not open the picker, check:

```powershell
Get-ChildItem C:\ProgramData\printrxer_v3\deferred -Recurse -Filter printrxer_v3_failure.txt | Select-Object -Last 5 | Get-Content
```

`JobOwnerMismatch` means the captured job SID did not match the current Windows user, or the capture omitted `submittingUserSid` without an explicit import override. `PayloadNotReady` means the XPS/OXPS payload was missing, empty, locked, or still changing after the metadata grace period.

If the handoff share is down, check `C:\ProgramData\printrxer_v3\pending-outbox`. Packages there are durable local packages waiting for publication retry. Once publication succeeds they move to `C:\ProgramData\printrxer_v3\published`.

Publication log outcomes are `PackageQueuedLocal`, `PackagePublished`, `PackagePublishDeferred`, and `PackagePublishFailed`.

## Support Bundle

Collect:

```text
healthmailer.settings.json
logs\healthmailer.log
PrintRxerV3 logs, if relevant
failed\<packageId>\request.json
failed\<packageId>\result.json
failed\<packageId>\summary.txt
quarantine\<packageId>\request.json
quarantine\<packageId>\result.json
processed-ledger.jsonl
```

Do not email PHI outside approved support channels.
