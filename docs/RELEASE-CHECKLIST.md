# Release Checklist

## Build

- [ ] `dotnet build .\PrintRxerSuite.slnx`
- [ ] `dotnet run --project .\tests\HealthMailer.Tests\HealthMailer.ContractTests.csproj`
- [ ] `dotnet run --project .\apps\PrintRxerV3\tests\PrintRxerV3.ContractTests.csproj`
- [ ] `powershell -ExecutionPolicy Bypass -File .\tools\New-PrintRxerSuiteReleaseBundle.ps1 -Version <version>`
- [ ] Confirm `dist\printRxerSuite-<version>.zip` exists.
- [ ] Extract `dist\printRxerSuite-<version>.zip` to a temporary folder.
- [ ] Run `PrintRxerSuiteInstaller.exe --smoke-test` from the extracted folder using `Start-Process -Wait -PassThru`.
- [ ] Confirm `PrintRxerSuiteInstaller.exe`, `INSTALL-BUNDLE-README.txt`, and `SHA256SUMS.txt` are present at the ZIP root.
- [ ] Confirm component ZIPs exist for support/internal use.
- [ ] Confirm `PrintRxerSuite-Install-Guide.html`, `healthmailer_release_doc.html`, and `printRxer_HealthMailer_User_Guide.html` are present at the suite and component ZIP roots.
- [ ] Confirm bundled Markdown support docs include `RECIPIENTS.md`, `TROUBLESHOOTING.md`, `OPERATIONS-RUNBOOK.md`, `HANDOFF-CONTRACT.md`, `HANDOFF-FOLDER-SETUP.md`, and `CONFIGURATION.md`.
- [ ] Confirm no Word-document files are included in release bundles.
- [ ] Confirm component ZIPs contain `SHA256SUMS.txt` and exclude `.pdb`, `bin`, `obj`, `publish`, `tmp`, and staging folders.
- [ ] Confirm version/build artifacts are from a clean checkout.

## Install

- [ ] Clean install on a test workstation.
- [ ] Download/extract the suite ZIP rather than building from source on the target machine.
- [ ] Open `PrintRxerSuiteInstaller.exe`.
- [ ] `Validate installation` runs before install and reports expected missing/not-installed items without crashing.
- [ ] `HealthMailerSetup.exe --validate` invokes installed HealthMailer runtime validation and reports runtime failures as validation exit code `7`.
- [ ] Live-send validation fails when Outlook COM is unavailable or live sending is not installer-approved.
- [ ] `C:\ProgramData\HealthMailer\app` ACL does not grant ordinary users write, modify, delete, permission-change, ownership-change, or full-control rights.
- [ ] `Install printRxer printing machine` explains that printer capture may require administrator approval and opens the printRxer component installer.
- [ ] printRxer install creates/repairs the native port monitor, PrintRxer XPS driver, and local printer queue named `printRxer`.
- [ ] `Install HealthMailer sending machine` opens the HealthMailer component installer as the current Outlook/Healthmail sender user.
- [ ] HealthMailer install runs as the intended Outlook/Healthmail sender user; HealthMailer uninstall may request administrator approval to remove the protected application folder.
- [ ] `Same-machine pilot: install both` starts the printRxer printing installer and HealthMailer sending installer.
- [ ] `Open logs folder` opens an existing component log folder, or reports clearly that no log folder exists yet.
- [ ] `Create support bundle` creates a ZIP and excludes PDF payloads by default, with README wording that warns package metadata/log/result evidence may still contain PHI and must stay within approved HSE support/governance channels.
- [ ] `Advanced / repair` offers printer-capture repair plus separate printRxer and HealthMailer actions, with elevation requested only for printRxer/printer-capture paths that require it.
- [ ] printRxer installed alone with local handoff folder.
- [ ] printRxer installed alone with UNC handoff folder.
- [ ] HealthMailer installed alone watching local folder.
- [ ] HealthMailer installed alone watching UNC folder.
- [ ] Same-machine local handoff test.
- [ ] UNC handoff folder test.
- [ ] Scheduled task starts silently.
- [ ] Watchdog trigger restarts after process kill.
- [ ] printRxer task install.
- [ ] printRxer `--process-once`.
- [ ] printRxer watcher.

## HealthMailer Validation

- [ ] `SendMail=false` dry-run validation does not require Outlook.
- [ ] `SendMail=true` requires explicit live-sending approval in config and checks Outlook COM registration.
- [ ] `--process-once` processes waiting packages.
- [ ] Missing `READY` is ignored.
- [ ] Bad PDF hash is quarantined.
- [ ] Duplicate package is quarantined and not resent.
- [ ] Lock containing a dead PID is reclaimed immediately.
- [ ] Lock owned by an active HealthMailer process is left alone.
- [ ] Confirm current release docs and config do not present removed/deferred chart/ViewPoint copy as available.
- [ ] `result.json` and `summary.txt` are present for terminal outcomes.
- [ ] Optional `summary.html` has no scripts or external resources.
- [ ] HealthMailer log rotation caps `healthmailer.log` and old logs.
- [ ] printRxer log rotation caps `printRxer.log` and old logs.
- [ ] HealthMailer starts and keeps polling when watched UNC is temporarily unavailable.
- [ ] HealthMailer does not send partial `.uploading-*` package.
- [ ] `.healthmailer.lock` records the claiming HealthMailer process ID.
- [ ] Invalid or unreadable lock ownership is left alone fail-closed.
- [ ] Concurrent claim attempt allows only one processor to claim.
- [ ] Ledger detects duplicates by package ID and completed package hash.
- [ ] Ledger cache reloads after external ledger append.
- [ ] Malformed ledger line is ignored without breaking duplicate checks.
- [ ] Timestamped sent-ledger entries older than 30 days are excluded from the active duplicate cache.
- [ ] Legacy sent-ledger entries without a valid timestamp remain in the active duplicate cache.
- [ ] Sent, failed, and quarantine archives are not deleted during normal processing.
- [ ] Local ACL hardening skips UNC paths and applies restricted rules to archives, logs, config, and ledger.

## printRxer Validation

- [ ] Captured payload missing is ignored until grace expires.
- [ ] Captured payload zero bytes is ignored until grace expires.
- [ ] Recently written payload is ignored until stable.
- [ ] Stable payload is eligible for picker/package creation.
- [ ] Matching `submittingUserSid` can create a package.
- [ ] Different `submittingUserSid` defers as `JobOwnerMismatch`.
- [ ] Missing `submittingUserSid` defers by default.
- [ ] Explicit import/test override for missing SID is documented.
- [ ] Picker still requires explicit button action; double-click does not send.
- [ ] `Preview document` opens the controlled in-app viewer with only page navigation and close controls; it provides no print, save, external-viewer, prepare, or send action.
- [ ] Network/share unavailable during printRxer publish leaves package in local outbox.
- [ ] Pending local package later publishes after share returns.
- [ ] printRxer watcher uses configured `RetryIntervalSeconds`.
- [ ] Duplicate publish attempt is safe/idempotent.
- [ ] Verify `RecipientCsvValidator` required, alias, and search-column lookups match current production Healthmail and Outlook CSV schemas.
- [ ] Confirm repository documentation does not reference unsupported legacy three-column recipient schemas.

## Uninstall

- [ ] `Uninstall-HealthMailer.ps1 -PlanOnly`
- [ ] `Uninstall-printRxer.ps1 -PlanOnly`
- [ ] Standard uninstall removes task and process but preserves data.
- [ ] printRxer uninstall/reinstall succeeds.
- [ ] Both apps uninstall independently.
- [ ] `Test-HealthMailerUninstallState.ps1` passes.
- [ ] `-RemoveData` removes local data only when explicitly requested.
- [ ] Reinstall succeeds after uninstall.

## Documentation

- [ ] README role links are current.
- [ ] Review current IT guidance in `docs/PrintRxerSuite-Install-Guide.html`.
- [ ] Use `README.md`, `SECURITY.md`, `DEPLOYMENT.md`, `docs/OPERATIONS-RUNBOOK.md`, and this checklist as current release guidance.
- [ ] Deployment guide matches scripts.
- [ ] Configuration guide matches `HealthMailerConfig`.
- [ ] Handoff contract matches code/tests.
- [ ] Security notes include known limits.

