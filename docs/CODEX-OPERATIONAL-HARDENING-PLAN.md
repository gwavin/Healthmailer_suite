# Codex Operational Hardening Plan

This is the next conservative work package for the PrintRxer Suite. It records planned operational-hardening work without changing the current two-app architecture.

## Current Architecture

PrintRxerV3 runs where users print prescriptions to the local `printRxer` printer. It captures the print job, checks payload readiness and job ownership, opens the recipient picker, creates a durable local package, and publishes or retries publication to the configured handoff folder.

HealthMailer runs where the approved Outlook/Healthmail profile is available. It watches the configured handoff folder, validates complete READY packages, prevents duplicate sends, sends through local Outlook COM when enabled, optionally copies to chart/ViewPoint, writes result/audit records, and archives packages.

## Implemented Now

- Internal `--status` handlers exist, but the apps are built as silent Windows executables for watcher mode. Do not advertise direct `exe --status` usage to IT/users until a console-friendly wrapper or separate support CLI is added.
- PrintRxerV3 creates packages locally before publishing to the handoff folder.
- PrintRxerV3 retains unpublished packages in the local outbox when publication is deferred.
- HealthMailer requires complete packages with `READY`, `request.json`, `prescription.pdf`, and `request.sha256`.
- HealthMailer ignores `.uploading-*` folders.
- HealthMailer prevents duplicate sends using the processed ledger.
- PrintRxerV3 recipient picker includes an on-demand preview button.
- PrintRxerV3 extracts patient name, MRN, and prescriber-related glyph text where available.
- PrintRxerV3 keeps job/user visible in the picker and does not show printer name in the clinical metadata panel.
- `tools/Test-PrintRxerSuiteHealth.ps1` exists as a monitoring script.

## Planned Commands

These commands are not implemented yet unless noted above:

- `printRxer.exe --support-bundle --output <zip>`
- `HealthMailer.exe --support-bundle --output <zip>`
- `printRxer.exe --list-pending`
- `printRxer.exe --retry-pending`
- `printRxer.exe --clean-published --older-than-days <days> --plan-only`
- `HealthMailer.exe --list-ready`
- `HealthMailer.exe --list-failed`
- `HealthMailer.exe --list-quarantine`
- `HealthMailer.exe --validate-all`

## Work Package

1. Print queue completion fix
   - Confirm successful captures flush and close payload handles before commit.
   - Confirm the native port monitor returns success to the Windows spooler.
   - Confirm completed jobs disappear from the Windows print queue.
   - Document whether spooler restart, port monitor reinstall, printer reinstall, or reboot is required.

2. Status commands
   - Add console-friendly status commands or wrapper scripts for IT/support use.
   - Include PrintRxerV3 queue-age warnings, disk free checks, active log size, and clearer exit codes.
   - Include HealthMailer READY package age, failed/quarantine counts, Outlook registration state, disk free checks, and clearer exit codes.
   - Preserve `--json` support for both apps.

3. Queue-age and failure warnings
   - Add configurable thresholds for pending outbox age, READY package age, failed/quarantine counts, and minimum free disk.
   - Treat ageing work as an operational safety warning even when no exception is present.

4. Support bundle commands
   - Add support bundles for both apps.
   - Include config, latest logs and rotated logs, status JSON, counts, summaries/results, version/build information, and scheduled task state where practical.
   - Exclude prescription PDFs by default.
   - Require an explicit `--include-packages` option for package contents that may include patient-identifiable data.

5. Recovery and list commands
   - Add PrintRxerV3 pending list and retry commands.
   - Add plan-only published cleanup.
   - Add HealthMailer ready/failed/quarantine listing and validate-all.
   - Do not add automatic resend unless it is duplicate-ledger-aware and explicitly reviewed.

6. Wrong-recipient controls
   - Confirm no arbitrary free-text email entry in normal mode.
   - Confirm recipients come from an approved allow-list.
   - Confirm recipient display and final confirmation show name and email.
   - Validate recipient email and domain policy before package creation.
   - Record selected recipient in request/result summaries.

7. Backpressure and disk safety
   - Warn on high pending outbox count, old pending package age, high published archive size, failed/quarantine accumulation, and low disk free.
   - Never silently delete unsent packages.
   - Make cleanup plan-only by default or require an explicit destructive option.

8. Monitoring
   - Keep improving `tools/Test-PrintRxerSuiteHealth.ps1` as the operational check entry point.
   - Ensure it works when one app is not installed on the current machine.
   - Return clear healthy/warning/critical exit codes.

9. Documentation updates
   - Update `docs/OPERATIONS-RUNBOOK.md`, `docs/TROUBLESHOOTING.md`, `docs/RELEASE-CHECKLIST.md`, `docs/FAILURE-MODES.md`, `docs/MONITORING.md`, `apps/PrintRxerV3/CONFIGURATION.md`, and `apps/HealthMailer/README.md`.
   - Include what to do when the shared folder is down, Outlook is unavailable, packages are ageing, the print queue does not clear, or packages are failed/quarantined.

10. Tests and rehearsal
    - Add tests for status warnings, support bundle exclusion of PDFs, pending retry/list commands, HealthMailer list/validate-all commands, wrong-recipient controls, and disk/backpressure warnings.
    - Rehearse on a clean Windows machine or VM:
      - Install PrintRxerV3 only.
      - Configure local handoff.
      - Capture one test prescription.
      - Confirm print queue clears.
      - Install HealthMailer only.
      - Run with `SendMail=false`.
      - Confirm validation/archive behavior.
      - Configure UNC handoff.
      - Simulate UNC unavailable and restored.
      - Run uninstall plan-only and uninstall for both apps.

## Acceptance Check

Before completing this work package:

```powershell
dotnet test .\PrintRxerSuite.slnx
```

Record files changed, behavior changes, test output, clean-machine rehearsal results, and remaining limitations.
