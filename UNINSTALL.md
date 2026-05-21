# HealthMailer Uninstall

The default uninstall removes the watcher and leaves local evidence intact.

Preserved by default:

```text
C:\ProgramData\HealthMailer\healthmailer.settings.json
C:\ProgramData\HealthMailer\processed-ledger.jsonl
C:\ProgramData\HealthMailer\logs
C:\ProgramData\HealthMailer\sent
C:\ProgramData\HealthMailer\failed
C:\ProgramData\HealthMailer\quarantine
```

## Plan Only

Preview changes without modifying the machine:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Uninstall-HealthMailer.ps1 -PlanOnly
```

## Standard Uninstall

Remove the scheduled task and stop any running HealthMailer process:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Uninstall-HealthMailer.ps1
```

## Remove Published Runtime

If the runtime was copied to a managed location:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Uninstall-HealthMailer.ps1 `
  -PublishedRuntime 'C:\Program Files\HealthMailer'
```

## Remove Local Data

Only use this after confirming archives, logs, and ledger are no longer required:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Uninstall-HealthMailer.ps1 -RemoveData
```

## Post-Uninstall Checks

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Test-HealthMailerUninstallState.ps1
```

Expected state after standard uninstall:

- Scheduled task absent.
- No running `HealthMailer.exe`.
- Local data may still be present.

Expected state after `-RemoveData`:

- Scheduled task absent.
- No running `HealthMailer.exe`.
- `C:\ProgramData\HealthMailer` absent.
