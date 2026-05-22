# Troubleshooting

## Watcher Not Running

```powershell
Get-ScheduledTask -TaskName HealthMailer
Get-ScheduledTaskInfo -TaskName HealthMailer
Get-Process -Name HealthMailer -ErrorAction SilentlyContinue
```

Start it:

```powershell
Start-ScheduledTask -TaskName HealthMailer
```

Reinstall task:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Install-HealthMailerTask.ps1
```

## Packages Remain In Handoff Folder

Check for:

- missing `READY`
- existing fresh `.healthmailer.lock`
- malformed `request.json`
- PDF hash mismatch
- package already sent
- package still queued in printRxer local outbox because the share was unavailable

Inspect:

```powershell
Get-ChildItem C:\ProgramData\printRxer\handoff -Force
Get-Content C:\ProgramData\HealthMailer\logs\healthmailer.log -Tail 80
Get-ChildItem C:\ProgramData\printRxer\pending-outbox
Get-Content C:\ProgramData\printRxer\logs\printRxer.log -Tail 80
```

If HealthMailer logs that the handoff folder is unavailable, restore UNC/share access; the watcher should remain alive and retry by polling.

## Picker Does Not Open For A Captured Job

printRxer intentionally waits before opening the picker if the capture is not safe to process. Check:

- `metadata.json` exists.
- `job.xps` or `job.oxps` exists, is non-empty, and is no longer being written.
- `submittingUserSid` matches the current Windows user.
- The capture has not been moved to `C:\ProgramData\printRxer\deferred`.

Deferred captures include `printRxer_failure.txt` with outcomes such as `JobOwnerMismatch` or `PayloadNotReady`.

## Recipient List Is Missing Or Stale

The preferred recipient list is:

```text
<HandoffRoot>\recipients\recipients.csv
```

printRxer should still open the picker from local sources if the central share is slow or unavailable. Check:

```powershell
Get-ChildItem C:\ProgramData\printRxer\data\recipients -Force
Get-Content C:\ProgramData\printRxer\data\recipients\recipient-source-status.json
Get-Content C:\ProgramData\printRxer\logs\printRxer.log -Tail 80
```

Useful source files:

- `recipients.cache.csv`: last-known-good central list.
- `bundled-recipients.csv`: release-time fallback.
- `recipient-source-status.json`: current source, central path, validation status, and warning.

If the central file is invalid, printRxer rejects it and keeps using cache or bundled fallback. Fix the central CSV, then use `Refresh recipients` in the picker or restart printRxer.

The recipients CSV is address book/configuration data only. It must not contain patient names, MRNs, prescription details, or other patient-identifiable information.

## PrintRxer Package Queued Locally

If the user sees a local queue notification, the PDF package has been built but could not be copied to the HealthMailer handoff folder. Check:

- the configured `HandoffRoot`
- UNC reachability from the printRxer machine
- server-side ACLs
- `C:\ProgramData\printRxer\pending-outbox`

Do not delete pending packages unless support has confirmed they are no longer needed.

Relevant printRxer log outcomes:

- `PackageQueuedLocal`: package was created locally before publish.
- `PackagePublished`: package reached the configured handoff folder.
- `PackagePublishDeferred`: expected transient publish problem; the package remains queued.
- `PackagePublishFailed`: unexpected publish problem; the package remains queued for support/retry.

## Outlook Send Fails

Run:

```powershell
HealthMailer.exe --validate --config C:\ProgramData\HealthMailer\healthmailer.settings.json
```

Check:

- Outlook is installed.
- Outlook is signed in for the runtime user.
- The sender mailbox is locally approved.
- The HealthMailer scheduled task is running in the intended user session.

## Chart/ViewPoint Copy Fails

If `result.json` shows `ChartCopyFailed`, mail may already have been sent. Check:

- chart folder path
- UNC availability
- ACLs for HealthMailer runtime user
- MRN availability when `RequireMrn=true`
- local ViewPoint import naming rules

## Duplicate Package

If `result.json` shows `Duplicate`, HealthMailer found the package ID or completed package hash in:

```text
C:\ProgramData\HealthMailer\processed-ledger.jsonl
```

Do not manually resend without governance approval.

Only one HealthMailer runtime should use a given `LocalRoot` and `processed-ledger.jsonl`. The ledger cache detects external file changes, but multiple active senders against one ledger are still an unsupported operating model.

## Archive Cleanup

HealthMailer does not automatically delete sent, failed, or quarantine archives. If evidence storage is growing, escalate to the local governance/support process. Do not delete failed or quarantined packages without explicit review.

If a cleanup command is added later, it should be explicit and plan-only by default.

## Logs

HealthMailer logs are capped and rotated. If `healthmailer.log` is short, also inspect:

```powershell
Get-ChildItem C:\ProgramData\HealthMailer\logs\healthmailer*.log
Get-ChildItem C:\ProgramData\printRxer\logs\printRxer*.log
```

