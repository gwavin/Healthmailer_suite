# Release Checklist

## Build

- [ ] `dotnet test .\PrintRxerSuite.slnx`
- [ ] Publish HealthMailer.
- [ ] Publish PrintRxerV3.
- [ ] Confirm version/build artifacts are from a clean checkout.

## Install

- [ ] Clean install on a test workstation.
- [ ] PrintRxerV3 installed alone with local handoff folder.
- [ ] PrintRxerV3 installed alone with UNC handoff folder.
- [ ] HealthMailer installed alone watching local folder.
- [ ] HealthMailer installed alone watching UNC folder.
- [ ] Same-machine local handoff test.
- [ ] UNC handoff folder test.
- [ ] Scheduled task starts silently.
- [ ] Watchdog trigger restarts after process kill.
- [ ] PrintRxerV3 task install.
- [ ] PrintRxerV3 `--process-once`.
- [ ] PrintRxerV3 watcher.

## HealthMailer Validation

- [ ] `SendMail=false` dry-run validation does not require Outlook.
- [ ] `SendMail=true` validation checks Outlook COM registration.
- [ ] `--process-once` processes waiting packages.
- [ ] Missing `READY` is ignored.
- [ ] Bad PDF hash is quarantined.
- [ ] Duplicate package is quarantined and not resent.
- [ ] Stale lock is retried.
- [ ] Fresh lock is left alone.
- [ ] Mail failure does not copy to chart/ViewPoint.
- [ ] Chart-copy failure after mail is recorded as `ChartCopyFailed`.
- [ ] `result.json` and `summary.txt` are present for terminal outcomes.
- [ ] Optional `summary.html` has no scripts or external resources.
- [ ] HealthMailer log rotation caps `healthmailer.log` and old logs.
- [ ] PrintRxerV3 log rotation caps `printrxer_v3.log` and old logs.
- [ ] HealthMailer starts and keeps polling when watched UNC is temporarily unavailable.
- [ ] HealthMailer does not send partial `.uploading-*` package.
- [ ] Fresh `.healthmailer.lock` prevents claim.
- [ ] Stale `.healthmailer.lock` permits claim.
- [ ] Invalid lock content falls back to lock file timestamp.
- [ ] Concurrent claim attempt allows only one processor to claim.
- [ ] Ledger detects duplicates by package ID and completed package hash.
- [ ] Ledger cache reloads after external ledger append.
- [ ] Malformed ledger line is ignored without breaking duplicate checks.
- [ ] Sent, failed, and quarantine archives are not deleted during normal processing.
- [ ] Local ACL hardening skips UNC paths and applies restricted rules to archives, logs, config, and ledger.

## PrintRxerV3 Validation

- [ ] Captured payload missing is ignored until grace expires.
- [ ] Captured payload zero bytes is ignored until grace expires.
- [ ] Recently written payload is ignored until stable.
- [ ] Stable payload is eligible for picker/package creation.
- [ ] Matching `submittingUserSid` can create a package.
- [ ] Different `submittingUserSid` defers as `JobOwnerMismatch`.
- [ ] Missing `submittingUserSid` defers by default.
- [ ] Explicit import/test override for missing SID is documented.
- [ ] Picker still requires explicit button action; double-click does not send.
- [ ] Network/share unavailable during PrintRxerV3 publish leaves package in local outbox.
- [ ] Pending local package later publishes after share returns.
- [ ] PrintRxerV3 watcher uses configured `RetryIntervalSeconds`.
- [ ] Duplicate publish attempt is safe/idempotent.

## Uninstall

- [ ] `Uninstall-HealthMailer.ps1 -PlanOnly`
- [ ] `Uninstall-PrintRxerV3.ps1 -PlanOnly`
- [ ] Standard uninstall removes task and process but preserves data.
- [ ] PrintRxerV3 uninstall/reinstall succeeds.
- [ ] Both apps uninstall independently.
- [ ] `Test-HealthMailerUninstallState.ps1` passes.
- [ ] `-RemoveData` removes local data only when explicitly requested.
- [ ] Reinstall succeeds after uninstall.

## Documentation

- [ ] README role links are current.
- [ ] Deployment guide matches scripts.
- [ ] Configuration guide matches `HealthMailerConfig`.
- [ ] Handoff contract matches code/tests.
- [ ] Security notes include known limits.
