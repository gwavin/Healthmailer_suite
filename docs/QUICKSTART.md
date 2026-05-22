# Quickstart

This is the shortest local test path for the two-exe suite. Normal users should use the release ZIP and `PrintRxerSuiteInstaller.exe`; the commands here are developer/support notes.

## 1. Build And Test

```powershell
dotnet test .\PrintRxerSuite.slnx
```

## 2. Publish Apps

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Publish-printRxer.ps1
powershell -ExecutionPolicy Bypass -File .\tools\Publish-HealthMailer.ps1
```

## 3. Configure Local Handoff

Use the same handoff folder in both app configs. For a same-machine test:

```text
C:\ProgramData\printRxer\handoff
```

HealthMailer default config also watches that folder.

For two machines, use a UNC path directly, for example:

```text
\\server\HealthMailerDrop$\incoming
```

## 4. Install printRxer Printing Machine

On the machine where users will print prescriptions, install printRxer printing. The normal GUI installer installs the application, scheduled watcher task, recipient cache handling, native port monitor, PrintRxer XPS driver, and local printer queue named `printRxer`.

For support/developer command-line installation, install the watcher and capture printer with the same handoff path:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Install-printRxerTask.ps1 `
  -HandoffRoot '\\server\HealthMailerDrop$\incoming'
powershell -ExecutionPolicy Bypass -File .\tools\Install-PrintRxerCapturePrinter.ps1
```

The printer capture step creates the `printRxer` printer queue, the `printrx:` port, and the PrintRxer XPS driver. It requires Administrator/UAC approval and is part of printRxer, not an optional normal install component.

## 5. Install HealthMailer Sending Machine

Interactive support path:

```powershell
.\publish\HealthMailer\HealthMailer.exe --install
```

Scripted refresh:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Install-HealthMailerTask.ps1
```

For a separate HealthMailer sending machine, configure the same UNC handoff folder in `C:\ProgramData\HealthMailer\healthmailer.settings.json` or via `HealthMailer.exe --install`. HealthMailer does not install printer capture.

## 6. Run A Test Package

For a harmless first test, set `SendMail=false` in
`C:\ProgramData\HealthMailer\healthmailer.settings.json` unless Outlook sending
has already been approved for the test mailbox.

Create a printRxer preview package:

```powershell
.\publish\printRxer\printRxer.exe --output C:\ProgramData\printRxer\handoff
```

Or print to the local `printRxer` printer and process one captured print job:

```powershell
.\publish\printRxer\printRxer.exe --process-once
```

## 7. Validate Results

Check HealthMailer terminal folders:

```powershell
Get-ChildItem C:\ProgramData\HealthMailer\sent
Get-ChildItem C:\ProgramData\HealthMailer\failed
Get-ChildItem C:\ProgramData\HealthMailer\quarantine
```

Inspect audit records:

```powershell
Get-ChildItem C:\ProgramData\HealthMailer\sent -Recurse -Filter result.json | Select-Object -Last 1 | Get-Content
```

## 8. Uninstall Both

Preview:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Uninstall-printRxer.ps1 -PlanOnly
powershell -ExecutionPolicy Bypass -File .\tools\Uninstall-HealthMailer.ps1 -PlanOnly
```

Remove tasks/processes while preserving data:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Uninstall-printRxer.ps1
powershell -ExecutionPolicy Bypass -File .\tools\Uninstall-HealthMailer.ps1
```

