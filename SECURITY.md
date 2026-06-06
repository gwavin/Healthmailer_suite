# Security

PrintRxer Suite is local-first.

## Transport Boundary

The active runtime does not introduce:

- project-operated SMTP
- Microsoft Graph send
- backend relay
- external upload service
- embedded mail credentials

printRxer creates packages only. HealthMailer owns delivery and hands mail to the locally signed-in Outlook profile through COM automation.

This split directly addresses the original printRxer operational pain point where every printing workstation needed a usable Outlook posture. Shared carts, F3/web-only Office users, and Citrix-style printing workstations can now hand off to a separate approved HealthMailer machine.

## Package Controls

HealthMailer sends only packages that pass all checks:

- package folder is not a staging folder
- `READY` exists
- `request.json`, `prescription.pdf`, and `request.sha256` exist
- `prescription.pdf` starts with `%PDF-`
- actual PDF SHA256 matches `request.json`
- actual PDF SHA256 matches `request.sha256`
- selected recipient email is present
- duplicate-send ledger does not already contain the package ID or completed package hash

The `READY` marker prevents half-written packages from being processed. SHA256 validation prevents mismatched PDF/metadata packages. Mail send happens before optional chart/ViewPoint copy, so chart import is not attempted for unsent mail unless the implementation is deliberately changed later.

## Print Capture Controls

printRxer does not process another user's captured job by default. If `submittingUserSid` is present in `metadata.json`, it must match the current Windows user SID before the picker opens. If the SID is missing, the capture is deferred by default unless an explicit import/test override is used.

printRxer also waits for the captured payload to be stable before opening the picker. The payload must exist, be non-empty, be old enough to satisfy the stability window, and be readable. This avoids picker prompts for partially written XPS/OXPS files.

## Audit Evidence

HealthMailer writes `result.json` for terminal outcomes and preserves packages in `sent`, `failed`, or `quarantine`.

Successful `sent` archives keep audit evidence, but `prescription.pdf` is removed after the configured `SentPrescriptionRetentionDays` period. The installer default is 14 days. Failed and quarantined packages require explicit review and must not be silently deleted by background processing.

This is audit evidence, not legal non-repudiation. Local governance still owns:

- approved sender mailbox
- Outlook profile policy
- Healthmail account approval
- workstation access policy
- shared-folder ACLs

## Duplicate Protection

`processed-ledger.jsonl` records sent package IDs and completed package hashes. If the same package ID or completed package hash is seen again, HealthMailer quarantines it rather than sending.

The ledger is treated as safety-critical duplicate-send evidence. HealthMailer reloads its in-memory ledger cache when the ledger file timestamp or length changes, and appends ledger records under an exclusive file lock. Operationally, one HealthMailer runtime should still own a given `LocalRoot` and ledger.

## Failure Handling

Validation failures and duplicates are quarantined. Mail failures and chart-copy failures are archived to `failed` with distinct result outcomes. HealthMailer fails closed: malformed packages are not sendable work.

Failed and quarantined packages include `result.json` and `summary.txt` where a terminal HealthMailer outcome is reached. printRxer deferred captures include a readable `printRxer_failure.txt` reason.

## Log Retention

HealthMailer caps and rotates `healthmailer.log` using the `Logging` config. Defaults keep the active log plus five rotated logs at 10 MB each, limiting local log growth without adding PHI beyond existing operational metadata.

Support bundles and ProgramData archives are audit-support evidence and may contain patient-identifiable information even when PDF payload files are excluded. Package metadata, `result.json`, `summary.txt`, logs, failed/quarantine evidence, recipient details, MRNs/patient hints, package IDs, hashes, and audit metadata may be present. Keep this evidence on HSE-controlled machines or approved HSE storage, restrict access to administrators and approved support/audit personnel, and do not email or transfer it outside approved HSE support/governance channels. Standard uninstall preserves ProgramData evidence by default so audit/support evidence is not accidentally removed.

## Folder Access

Ordinary users should not browse, edit, or delete HealthMailer archives, logs, config, or ledger. Shared handoff folders should be configured by IT and restricted to the minimum required identities.

Local ACL hardening applies only to local NTFS folders. It does not secure remote UNC shares; UNC share and NTFS permissions must be configured on the file server by IT.

Local folder hardening is role-specific:

- local handoff/drop folder: SYSTEM and Administrators Full Control, runtime user Modify, Builtin Users Modify only for intentional same-machine drop compatibility
- HealthMailer local root, sent, failed, quarantine, logs, and ledger: SYSTEM and Administrators Full Control, runtime user Modify, no generic Builtin Users rule
- config file: SYSTEM and Administrators Full Control, runtime user Read/ReadAndExecute after install, no generic Builtin Users rule
- printRxer local outbox, published, failed, logs, temp, and config folders: restricted to SYSTEM, Administrators, and the runtime user; local handoff/drop is the only printRxer folder that may allow broader local write access

## Chart/ViewPoint Copy

Chart/ViewPoint copy may create both a PDF and a sidecar JSON file. The sidecar can contain patient identifiers such as MRN and patient name, plus package ID and PDF hash. Enable this only for an approved local or server-side chart import folder with appropriate access controls.

## Known Limits

- Outlook ultimately controls onward mail transmission.
- Shared folder ACLs cannot be fully enforced by application code.
- PDF import workflows may have weaker original print-job identity metadata than native print capture.
- ViewPoint/chart import naming must be confirmed locally before clinical use.
