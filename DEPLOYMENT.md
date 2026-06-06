# printRxer Suite Deployment Playbook

Use the extracted `printRxerSuite-<version>.zip` as the normal IT deployment entry point. Do not deploy by building from source on target workstations. Do not use mapped drives for handoff paths.

## 1. Pre-flight Verification

Verify release ZIPs against the supplied `SHA256SUMS.txt`, extract the suite ZIP, open PowerShell in its root, and run:

```powershell
$p = Start-Process -FilePath .\PrintRxerSuiteInstaller.exe -ArgumentList "--smoke-test" -Wait -PassThru
$p.ExitCode
```

If the smoke test returns a non-zero exit code, stop deployment. Do not provision either component from that bundle.

The component installers inside the suite ZIP are:

```text
.\payload\setup\printRxerSetup.exe
.\payload\setup\HealthMailerSetup.exe
```

Confirm before live PHI testing:

- The approved handoff path is available and secured by IT share and NTFS ACLs.
- Outlook is installed and signed in as the approved sender on the HealthMailer machine.
- HealthMailer setup will run as the intended Outlook/Healthmail sender Windows user.
- printRxer setup has administrator approval because it installs the port monitor, XPS driver, port, queue, application files, and watcher task.

## 2. Silent Provisioning

Use one handoff-root value for both roles. Use a UNC path directly, never a mapped drive:

```powershell
$handoffRoot = "\\server\HealthMailerDrop$\incoming"
```

Install printRxer:

```powershell
$p = Start-Process -FilePath .\payload\setup\printRxerSetup.exe -ArgumentList @(
  "--quiet",
  "--handoff-root",
  $handoffRoot
) -Wait -PassThru
$p.ExitCode
```

Install HealthMailer in dry-run mode first:

```powershell
$p = Start-Process -FilePath .\payload\setup\HealthMailerSetup.exe -ArgumentList @(
  "--quiet",
  "--handoff-root",
  $handoffRoot,
  "--send-mail",
  "false"
) -Wait -PassThru
$p.ExitCode
```

After dry-run validation and governance approval, enable live sending:

```powershell
$p = Start-Process -FilePath .\payload\setup\HealthMailerSetup.exe -ArgumentList @(
  "--quiet",
  "--handoff-root",
  $handoffRoot,
  "--send-mail",
  "true",
  "--sent-prescription-retention-days",
  "14"
) -Wait -PassThru
$p.ExitCode
```

For a same-machine pilot, use the same local value for both installers:

```powershell
$handoffRoot = "C:\ProgramData\printRxer\handoff"
```

The GUI-first equivalent is to run `PrintRxerSuiteInstaller.exe` and choose the required machine role. Scripts under `payload\tools` are support internals, not the normal deployment entry point.

## 3. Post-install Validation

Validate both installed components:

```powershell
$p = Start-Process -FilePath .\payload\setup\printRxerSetup.exe -ArgumentList "--validate" -Wait -PassThru
$p.ExitCode

$p = Start-Process -FilePath .\payload\setup\HealthMailerSetup.exe -ArgumentList "--validate" -Wait -PassThru
$p.ExitCode
```

Stop deployment and investigate any non-zero validation result. Complete the [post-install audit matrix](docs/OPERATIONS-RUNBOOK.md#post-install-audit-matrix) before live testing.

## 4. Operational Constants

- `RequireJobOwnerMatch=true` and `AllowMissingSubmittingSid=false` are production operational constants, not tuning options.
- printRxer does not send mail. It creates validated handoff packages.
- HealthMailer sends through the local Outlook/Healthmail profile of its Windows user.
- HealthMailer must run as the intended Outlook/Healthmail sender Windows user.
- The handoff folder must be identical for both roles.
- Do not use mapped drives. Scheduled tasks may not see user-mapped drive letters.
- Do not manually manipulate active handoff packages.
- Chart/ViewPoint copy is removed/deferred.

Default local locations and security boundaries are documented in [Configuration](docs/CONFIGURATION.md). Operational checks and evidence handling are documented in the [Operations Runbook](docs/OPERATIONS-RUNBOOK.md).

## 5. Recipient List Deployment

The central recipient list is:

```text
<HandoffRoot>\recipients\recipients.csv
```

IT or authorised maintainers require read/write access. Runtime printRxer users require read-only access. Existing central files are not overwritten by installation. The local bundled fallback and cache reside under:

```text
C:\ProgramData\printRxer\data\recipients
```

See [docs/RECIPIENTS.md](docs/RECIPIENTS.md) for schema, validation, ACLs, and the controlled update procedure.

## 6. Support Bundle

From the extracted suite ZIP, run `PrintRxerSuiteInstaller.exe` and choose `Create support bundle`.

The support bundle excludes PDF payloads by default, but logs, metadata, results, recipient details, identifiers, hashes, and audit evidence may still contain PHI. Keep support bundles on approved HSE-controlled storage and review them before transfer.

## 7. Rollback and Uninstall

Standard uninstall preserves ProgramData evidence:

```powershell
$p = Start-Process -FilePath .\payload\setup\printRxerSetup.exe -ArgumentList @("--uninstall", "--quiet") -Wait -PassThru
$p.ExitCode

$p = Start-Process -FilePath .\payload\setup\HealthMailerSetup.exe -ArgumentList @("--uninstall", "--quiet") -Wait -PassThru
$p.ExitCode
```

Use `--remove-data` only for an explicitly approved clean lab reset:

```powershell
$p = Start-Process -FilePath .\payload\setup\printRxerSetup.exe -ArgumentList @("--uninstall", "--quiet", "--remove-data") -Wait -PassThru
$p.ExitCode

$p = Start-Process -FilePath .\payload\setup\HealthMailerSetup.exe -ArgumentList @("--uninstall", "--quiet", "--remove-data") -Wait -PassThru
$p.ExitCode
```

## 8. Exit Codes

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

Installer logs:

```text
C:\ProgramData\printRxer\logs\printRxerInstaller.log
C:\ProgramData\HealthMailer\logs\HealthMailerInstaller.log
```
