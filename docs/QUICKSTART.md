# Quickstart

This is the shortest local test path for the two-exe suite.

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

## 4. Install printRxer Watcher

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Install-printRxerTask.ps1
```

For a separate printRxer-only machine, provide the UNC handoff path:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Install-printRxerTask.ps1 `
  -HandoffRoot '\\server\HealthMailerDrop$\incoming'
```

## 5. Install The Local printRxer Printer

On the machine where users will print prescriptions, install the native capture printer:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Install-PrintRxerCapturePrinter.ps1
```

This creates the `printRxer` printer queue, the `printrx:` port, and the PrintRxer XPS driver. It requires Administrator/UAC approval. If you only import existing capture folders or PDFs for testing, this printer step can be skipped.

## 6. Install HealthMailer Watcher

Interactive:

```powershell
.\publish\HealthMailer\HealthMailer.exe --install
```

Scripted refresh:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Install-HealthMailerTask.ps1
```

For a separate HealthMailer-only machine, configure the same UNC handoff folder in `C:\ProgramData\HealthMailer\healthmailer.settings.json` or via `HealthMailer.exe --install`.

## 7. Run A Test Package

For a harmless first test, set `SendMail=false` in
`C:\ProgramData\HealthMailer\healthmailer.settings.json` unless Outlook sending
has already been approved for the test mailbox.

Create a printRxer preview package:

```powershell
.\publish\printRxer\printRxer.exe --output C:\ProgramData\printRxer\handoff
```

Or process one captured print job:

```powershell
.\publish\printRxer\printRxer.exe --process-once
```

## 8. Validate Results

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

## 9. Uninstall Both

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

