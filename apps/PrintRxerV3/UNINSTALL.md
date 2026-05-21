# printRxer Uninstall

The default uninstall removes the scheduled watcher and leaves local package/capture evidence intact.

Preserved by default:

```text
C:\ProgramData\printRxer\handoff
C:\ProgramData\printRxer\processed
C:\ProgramData\printRxer\deferred
```

## Plan Only

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Uninstall-printRxer.ps1 -PlanOnly
```

## Standard Uninstall

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Uninstall-printRxer.ps1
```

This removes the `printRxer` scheduled task and stops running `printRxer.exe` processes.

## Remove Published Runtime

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Uninstall-printRxer.ps1 `
  -PublishedRuntime 'C:\Program Files\printRxer'
```

## Remove Local Data

Only use after confirming local handoff/capture data is no longer required:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Uninstall-printRxer.ps1 -RemoveData
```

## Post-Uninstall Check

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Test-printRxerUninstallState.ps1
```

