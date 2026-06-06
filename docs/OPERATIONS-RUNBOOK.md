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
| printRxer config | `C:\ProgramData\printRxer\config\printRxer.settings.json` |
| printRxer pending outbox | `C:\ProgramData\printRxer\pending-outbox` |
| printRxer published outbox | `C:\ProgramData\printRxer\published` |
| printRxer logs | `C:\ProgramData\printRxer\logs` |
| printRxer bundled recipients | `C:\ProgramData\printRxer\data\recipients\bundled-recipients.csv` |
| printRxer recipient cache | `C:\ProgramData\printRxer\data\recipients\recipients.cache.csv` |
| printRxer recipient status | `C:\ProgramData\printRxer\data\recipients\recipient-source-status.json` |

## Post-install Audit Matrix

Run from the extracted suite ZIP root:

```powershell
# Smoke-test extracted bundle before install
$p = Start-Process -FilePath .\PrintRxerSuiteInstaller.exe -ArgumentList "--smoke-test" -Wait -PassThru
$p.ExitCode

# Validate installed components
$p = Start-Process -FilePath .\payload\setup\printRxerSetup.exe -ArgumentList "--validate" -Wait -PassThru
$p.ExitCode

$p = Start-Process -FilePath .\payload\setup\HealthMailerSetup.exe -ArgumentList "--validate" -Wait -PassThru
$p.ExitCode

# Verify SYSTEM-loaded print-capture ACLs
Get-Acl "HKLM:\SYSTEM\CurrentControlSet\Control\Print\Monitors\PrintRxer Port Monitor" | Format-List AccessToString
Get-Acl "$env:WINDIR\System32\PrintRxerPortMonitor.dll" | Format-List AccessToString

# Verify protected HealthMailer application binaries
Get-Acl "C:\ProgramData\HealthMailer\app" | Format-List AccessToString

# Verify all-users printRxer watcher task
$task = Get-ScheduledTask -TaskName printRxer
$task.Principal | Select-Object UserId, GroupId, RunLevel, LogonType
$task.Settings | Select-Object MultipleInstances
```

Compliance checks:

- If `BUILTIN\Users`, `Authenticated Users`, or ordinary domain user groups have Write, Modify, FullControl, SetValue, ChangePermissions, or TakeOwnership permissions on `PrintRxerPortMonitor.dll` or the PrintRxer port monitor registry key, the installation is outside compliance bounds and must be re-evaluated.
- Expected native component control is SYSTEM and Administrators full control, with only required service read/execute access where applicable.
- The printRxer scheduled task should be an all-users logon task with limited run level and parallel instances so shared workstations do not bind the watcher to the installing administrator account.
- Confirm two different non-admin test users can log on, print to the local `printRxer` printer, and receive only their own picker session.

## Scheduled Task

```powershell
Get-ScheduledTask -TaskName HealthMailer
Get-ScheduledTaskInfo -TaskName HealthMailer
Start-ScheduledTask -TaskName HealthMailer
Stop-ScheduledTask -TaskName HealthMailer
```

printRxer is installed once per workstation. Its watcher task should be an all-users logon task with `BUILTIN\Users` as the principal, `Limited` run level, and `Parallel` multiple-instance policy so each interactive Windows user can have their own watcher session on a shared machine. It should not be bound to the IT/admin account used for installation.

Shared workstation validation:

```powershell
$task = Get-ScheduledTask -TaskName printRxer
$task.Principal | Format-List UserId,GroupId,RunLevel,LogonType
$task.Settings | Format-List MultipleInstances
$task.Triggers | Format-List *
```

Confirm two different non-admin test users can log on, print to the local `printRxer` printer, and open their own picker without another user receiving the capture. Owner/SID matching must remain enabled.

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

printRxer rotates logs as `printRxer.log`, `printRxer.1.log`, `printRxer.2.log`, and so on. Defaults cap each log at 5 MB and keep three rotated files.

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
| `ChartCopyFailed` | Legacy/deferred compatibility outcome from older evidence; chart copy is not active in the current release. Confirm mail state before handling. |
| `Failed` | Collect logs and package evidence. |

## printRxer Deferred Captures

If a captured print does not open the picker, check:

```powershell
Get-ChildItem C:\ProgramData\printRxer\deferred -Recurse -Filter printRxer_failure.txt | Select-Object -Last 5 | Get-Content
```

`JobOwnerMismatch` means the captured job SID did not match the current Windows user, or the capture omitted `submittingUserSid` without an explicit import override. `PayloadNotReady` means the XPS/OXPS payload was missing, empty, locked, or still changing after the metadata grace period.

If the handoff share is down, check `C:\ProgramData\printRxer\pending-outbox`. Packages there are durable local packages waiting for publication retry. Once publication succeeds they move to `C:\ProgramData\printRxer\published`.

Publication log outcomes are `PackageQueuedLocal`, `PackagePublished`, `PackagePublishDeferred`, and `PackagePublishFailed`.

## Recipient List Operations

The preferred recipient list lives under the configured handoff folder:

```text
<HandoffRoot>\recipients\recipients.csv
```

printRxer reads this central file in the background and writes only local cache/status files during normal runtime. Ordinary printRxer users should not need write access to `<HandoffRoot>\recipients`.

Recommended central ACLs:

- IT / authorised maintainers: read/write.
- printRxer runtime users or workstation identities: read-only.
- HealthMailer: no access required for normal package processing.
- Broad ordinary user groups: avoid write access.

Update process:

1. Prepare and validate a new `recipients.csv`.
2. Copy it to `<HandoffRoot>\recipients\recipients.csv.tmp`.
3. Rename it to `recipients.csv`.
4. Keep a backup of the previous file.
5. Ask users/support to use `Refresh recipients` or wait for the next background refresh.

The recipients CSV must not contain patient names, MRNs, prescription details, or other patient-identifiable information.

## Support Bundle

Preferred path:

1. Run `PrintRxerSuiteInstaller.exe` from the extracted release ZIP.
2. Choose `Create support bundle`.
3. Review the generated bundle before sending it outside approved support channels.

The support bundle excludes PDF payloads by default and includes available configs, logs, recent failed/quarantine evidence, scheduled task status, printer status, process status, and a SHA256 manifest. PDF exclusion does not mean the bundle is free of PHI: package metadata, `result.json`, `summary.txt`, logs, failed/quarantine evidence, recipient details, MRNs/patient hints, package IDs, hashes, and audit metadata may still be present.

Treat support bundles as HSE-machine/admin-restricted audit-support evidence for troubleshooting, clinical communication evidence, and governance review. Store them only on HSE-controlled machines or approved HSE storage, limit access to administrators and approved support/audit personnel, and do not email or transfer them outside approved HSE support/governance channels. Preserve ProgramData evidence by default for audit and support unless an approved retention process says otherwise.

If collecting manually, gather:

```text
healthmailer.settings.json
logs\healthmailer.log
printRxer logs, if relevant
failed\<packageId>\request.json
failed\<packageId>\result.json
failed\<packageId>\summary.txt
quarantine\<packageId>\request.json
quarantine\<packageId>\result.json
processed-ledger.jsonl
```

Do not email PHI outside approved support channels.

