# PrintRxerV3 Minimal Installer Package Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a minimal PrintRxerV3 handoff package with one installer EXE, one uninstaller EXE, and one short guide, without requiring IT to run PowerShell scripts directly.

**Architecture:** Add a small self-contained WinForms installer/uninstaller project for PrintRxerV3 that embeds or locates the existing published runtime, native print-capture assets, recipients, and branding assets. The installer owns folder selection, config writing, scheduled task creation, and printer capture installation by invoking Windows APIs/PowerShell internally with hidden windows. The release bundle script emits the minimal PrintRxerV3 package first; HealthMailer follows later.

**Tech Stack:** .NET 8 Windows WinForms, existing PrintRxerV3 app/runtime, existing native print-capture assets, PowerShell invoked internally for Windows printer/driver operations where direct managed APIs are impractical.

---

### Task 1: Add PrintRxerV3 installer project shell

**Files:**
- Create: `installers/PrintRxerV3Installer/PrintRxerV3Installer.csproj`
- Create: `installers/PrintRxerV3Installer/Program.cs`
- Modify: `PrintRxerSuite.slnx`

- [ ] Create a WinForms executable project targeting `net8.0-windows`, self-contained publishable as `PrintRxerV3Installer.exe`.
- [ ] Add command mode support: default install UI, `--uninstall`, and `--help`.
- [ ] Add the project to the solution.
- [ ] Verify `dotnet build .\installers\PrintRxerV3Installer\PrintRxerV3Installer.csproj` succeeds.

### Task 2: Add installer asset staging contract

**Files:**
- Create: `installers/PrintRxerV3Installer/InstallerPaths.cs`
- Modify: `tools/New-PrintRxerSuiteReleaseBundle.ps1`

- [ ] Define expected bundle layout beside the installer EXE: `payload\publish\PrintRxerV3`, `payload\assets`, and `payload\docs`.
- [ ] Update the bundle script so PrintRxerV3 ZIP contains `PrintRxerV3Installer.exe`, `PrintRxerV3Uninstall.exe`, the guide, and hidden/internal `payload` files only.
- [ ] Keep scripts out of the minimal IT-facing root.
- [ ] Verify the ZIP root is visually minimal.

### Task 3: Implement install GUI

**Files:**
- Create: `installers/PrintRxerV3Installer/InstallForm.cs`
- Create: `installers/PrintRxerV3Installer/InstallOptions.cs`

- [ ] Build a simple form with default local handoff `C:\ProgramData\printRxer\handoff`.
- [ ] Add buttons: `Continue with default` and `Choose folder...`.
- [ ] Allow UNC folder text to be typed or pasted if folder browser is awkward.
- [ ] Show a final confirmation before install starts.
- [ ] Ensure normal users see clear admin-rights messaging for printer install.

### Task 4: Implement install operations

**Files:**
- Create: `installers/PrintRxerV3Installer/PrintRxerInstaller.cs`
- Create: `installers/PrintRxerV3Installer/ProcessRunner.cs`

- [ ] Copy `payload\publish\PrintRxerV3` to `C:\Program Files\PrintRxerV3`.
- [ ] Create `C:\ProgramData\printRxer` config/data/work/log folders.
- [ ] Seed recipients and image only if missing.
- [ ] Write `C:\ProgramData\printRxer\config\printRxer.settings.json` with selected handoff root.
- [ ] Register the per-user `PrintRxerV3` scheduled task.
- [ ] Install the native capture printer by running the existing internal scripts from payload/tools or by inlining their logic.
- [ ] Run hidden; show success/failure on the form.

### Task 5: Implement uninstaller EXE

**Files:**
- Create: `installers/PrintRxerV3Installer/UninstallForm.cs`
- Create: `installers/PrintRxerV3Installer/PrintRxerUninstaller.cs`

- [ ] Support `PrintRxerV3Uninstall.exe` by publishing the same app and renaming/copying it, or by handling `--uninstall` behind the wrapper.
- [ ] Remove scheduled task/process.
- [ ] Remove printRxer printer, port, driver, and port monitor.
- [ ] Preserve ProgramData by default.
- [ ] Include optional explicit remove-data checkbox labelled as lab reset only.

### Task 6: Add minimal PrintRxer guide

**Files:**
- Create: `docs/PrintRxerV3_Minimal_Install_Guide.md`

- [ ] Explain what the two EXEs do.
- [ ] Explain default local handoff and UNC option.
- [ ] Explain admin requirement for printer installation.
- [ ] Explain uninstall preserves ProgramData by default.
- [ ] Keep it short enough to fit on one printed page.

### Task 7: Verify package and tests

**Files:**
- Modify: `tools/New-PrintRxerSuiteReleaseBundle.ps1`

- [ ] Run `dotnet test .\PrintRxerSuite.slnx`.
- [ ] Run `powershell -ExecutionPolicy Bypass -File .\tools\New-PrintRxerSuiteReleaseBundle.ps1 -Version installer-test -SkipTests`.
- [ ] Inspect `PrintRxerV3-installer-test.zip` root and confirm only `PrintRxerV3Installer.exe`, `PrintRxerV3Uninstall.exe`, guide, and payload/manifest are present.
- [ ] Commit and push.

---

Self-review:
- Scope is limited to PrintRxerV3 package first, as requested.
- HealthMailer equivalent is intentionally deferred until PrintRxerV3 package shape is proven.
- The plan keeps scripts available internally but removes them from the IT-facing install command surface.
