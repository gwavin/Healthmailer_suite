# printRxer Install

printRxer runs where prescriptions are printed or where captured PDF/XPS jobs are imported. It creates HealthMailer handoff packages and does not send mail.

## Publish

From the suite root:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Publish-printRxer.ps1
```

Default output:

```text
.\publish\printRxer\printRxer.exe
```

## Default Paths

```text
Incoming captures: C:\ProgramData\printRxer\work\incoming
Config: C:\ProgramData\printRxer\config\printRxer.settings.json
Processed captures: C:\ProgramData\printRxer\processed
Pending local outbox: C:\ProgramData\printRxer\pending-outbox
Published local outbox: C:\ProgramData\printRxer\published
Logs: C:\ProgramData\printRxer\logs
Handoff folder: C:\ProgramData\printRxer\handoff or \\server\HealthMailerDrop$\incoming
Recipient list: C:\ProgramData\printRxer\data\recipients\recipients.csv
Picker image: C:\ProgramData\printRxer\data\Images\mncms_400x400.jpg
```

## Install Watcher Task

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Install-printRxerTask.ps1
```

Custom paths:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Install-printRxerTask.ps1 `
  -ExePath 'C:\Program Files\printRxer\printRxer.exe' `
  -IncomingRoot 'C:\ProgramData\printRxer\work\incoming' `
  -DataRoot 'C:\ProgramData\printRxer' `
  -HandoffRoot '\\server\HealthMailerDrop$\incoming'
```

The script writes `printRxer.settings.json`, creates local data folders, and registers a task named `printRxer`. The task starts at user logon and has a one-minute watchdog trigger.

The installer also seeds the baseline recipient CSV and picker image from the repository `assets` folder if those files do not already exist. It does not overwrite an existing local recipients file, because that file is expected to be site-maintained.

## Install Live Print Capture

The watcher processes completed captures from `IncomingRoot`. To create those captures from normal Windows printing, install the local native capture printer on the printing workstation:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Install-PrintRxerCapturePrinter.ps1
```

This installs the `PrintRxer Port Monitor`, `printrx:` port, `PrintRxer XPS Driver`, and the visible `printRxer` printer queue. It requires Administrator/UAC approval because it changes Windows spooler components.

Check the printer state:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Test-PrintRxerCapturePrinterState.ps1
```

Interactive install is also available:

```powershell
printRxer.exe --install
```

It asks for the incoming capture folder, local printRxer data folder, and HealthMailer handoff folder. The handoff folder may be a UNC path.

For managed deployment or UNC paths that are easier to type than browse:

```powershell
printRxer.exe --install `
  --incoming 'C:\ProgramData\printRxer\work\incoming' `
  --data-root 'C:\ProgramData\printRxer' `
  --output '\\server\HealthMailerDrop$\incoming'
```

## Manual Commands

Watch continuously:

```powershell
printRxer.exe --watch
```

Process one ready captured job:

```powershell
printRxer.exe --process-once
```

Use explicit folders:

```powershell
printRxer.exe --watch `
  --incoming 'C:\ProgramData\printRxer\work\incoming' `
  --processed 'C:\ProgramData\printRxer\processed' `
  --output '\\server\HealthMailerDrop$\incoming'
```

Create a sample preview package:

```powershell
printRxer.exe --output C:\ProgramData\printRxer\handoff
```

## Notes

- `--incoming` is the folder containing captured `metadata.json` plus `job.xps` or `job.oxps`.
- `--processed` is where consumed captures are moved.
- `--output` is the HealthMailer handoff folder.
- Use UNC paths rather than mapped drives for shared handoff folders.
- printRxer creates packages in `LocalOutboxRoot` first. If the handoff folder is unavailable, the package remains queued locally and the watcher retries publication later.
- `RetryIntervalSeconds` controls the watcher retry/poll interval.
- Logs are written under `LogsRoot` and capped by `MaxLogBytes`/`MaxLogFiles`.


