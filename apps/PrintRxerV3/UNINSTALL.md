# PrintRxerV3 Uninstall

The default uninstall removes the scheduled watcher and leaves local package/capture evidence intact.

Preserved by default:

```text
C:\ProgramData\printrxer_v3\handoff
C:\ProgramData\printrxer_v3\processed
C:\ProgramData\printrxer_v3\deferred
```

## Plan Only

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Uninstall-PrintRxerV3.ps1 -PlanOnly
```

## Standard Uninstall

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Uninstall-PrintRxerV3.ps1
```

This removes the `PrintRxerV3` scheduled task and stops running `printrxer_v3.exe` processes.

## Remove Published Runtime

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Uninstall-PrintRxerV3.ps1 `
  -PublishedRuntime 'C:\Program Files\PrintRxerV3'
```

## Remove Local Data

Only use after confirming local handoff/capture data is no longer required:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Uninstall-PrintRxerV3.ps1 -RemoveData
```

## Post-Uninstall Check

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Test-PrintRxerV3UninstallState.ps1
```
