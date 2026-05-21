# PrintRxerV3 Configuration

PrintRxerV3 currently uses command-line options rather than a persistent JSON config file.

## Options

| Option | Purpose | Default |
| --- | --- | --- |
| `--watch` | Continuously watch for captured jobs. | off |
| `--process-once` | Process one ready captured job and exit. | off |
| `--incoming <path>` | Folder containing captured print jobs. | `C:\ProgramData\printrxer_v3\work\incoming` |
| `--processed <path>` | Folder where consumed captures are moved. | `C:\ProgramData\printrxer_v3\processed` |
| `--output <path>` | HealthMailer handoff folder. | `C:\ProgramData\printrxer_v3\handoff` |
| `--config <path>` | Persistent PrintRxerV3 config file. | `C:\ProgramData\printrxer_v3\config\printrxer_v3.settings.json` |
| `--install` | Interactive setup wizard for PrintRxerV3 only. | off |
| `--data-root <path>` | Non-interactive install data root; used with `--install`. | `C:\ProgramData\printrxer_v3` |
| `--no-picker` | Create a package without opening the recipient picker; for testing only. | off |
| `--no-job-owner-match` | Disable submitting-user SID enforcement; for controlled support/import use only. | off |
| `--allow-missing-job-owner` | Allow captures without `submittingUserSid`; for controlled support/import use only. | off |
| `--payload-stable-seconds <seconds>` | Minimum payload age before picker/package creation. | `2` |
| `--metadata-grace-seconds <seconds>` | Time to wait for a usable payload before deferring the capture. | `60` |

## Captured Job Shape

PrintRxerV3 expects each incoming capture directory to contain:

```text
metadata.json
job.xps or job.oxps
```

`metadata.json` may include:

```json
{
  "source": "port-monitor",
  "portName": "printRxer",
  "printerName": "printRxer",
  "documentName": "Prescription",
  "jobId": 42,
  "submittingUser": "DOMAIN\\user",
  "submittingUserSid": "S-1-5-...",
  "capturedAtUtc": "2026-05-11T09:00:00Z",
  "payloadFile": "job.xps"
}
```

## Capture Safety

By default, PrintRxerV3 enforces the original printRxer owner/session lesson:

- If `submittingUserSid` is present, it must match the current Windows user SID.
- If `submittingUserSid` is missing, the capture is deferred safely.
- Use `--allow-missing-job-owner` only for explicit test/import work.
- Use `--no-job-owner-match` only when locally approved for support diagnostics.

SID matching is the enforced boundary because it is stable across the per-user interactive scheduled task and the captured metadata available today. Session ID is retained in package metadata where available, but import and PDF paths do not always provide a reliable source session; the per-user task plus SID check is the conservative default.

PrintRxerV3 also waits for payload readiness before opening the picker. The payload must exist, be non-zero length, be older than `--payload-stable-seconds`, be readable, and remain unchanged across a short probe. If the payload is still not usable after `--metadata-grace-seconds`, the capture is moved to deferred with a readable reason.

## Handoff Output

PrintRxerV3 writes packages to `LocalOutboxRoot` first, then publishes them to `HandoffRoot`. If the handoff folder is unavailable, the package remains in the local outbox and is retried by the watcher.

The watcher retry/poll interval comes from `RetryIntervalSeconds`. Operational logs are written to `LogsRoot` and rotated using `MaxLogBytes` and `MaxLogFiles`.

Publish outcomes are logged as `PackageQueuedLocal` when a local package is created, `PackagePublished` when publication succeeds, `PackagePublishDeferred` for expected transient share/permission/IO failures, and `PackagePublishFailed` for unexpected publication failures. In all failed/deferred publication cases, the local package remains in `LocalOutboxRoot`.

The `--output` folder receives HealthMailer packages:

```text
<packageId>\
  request.json
  prescription.pdf
  request.sha256
  summary.txt
  READY
```

## Scheduled Task

The installer script registers a task named `PrintRxerV3`:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Install-PrintRxerV3Task.ps1 `
  -IncomingRoot 'C:\ProgramData\printrxer_v3\work\incoming' `
  -ProcessedRoot 'C:\ProgramData\printrxer_v3\processed' `
  -HandoffRoot 'C:\ProgramData\printrxer_v3\handoff'
```

For two-machine deployments, set `-HandoffRoot` to the secured UNC folder watched by HealthMailer.

