# HealthMailer Handoff Package Contract

printRxer writes a local handoff package for HealthMailer. printRxer must not send mail.

```text
<packageId>/
  request.json
  prescription.pdf
  request.sha256
  summary.txt
  READY
```

Packages are first staged locally in a temporary `.writing-<packageId>-<suffix>` directory and then moved into the local outbox final `<packageId>` directory only after every file has been written. When publishing to a shared handoff folder, printRxer copies to `.uploading-<packageId>-<suffix>`, writes `READY` last, and then moves the upload folder to the final `<packageId>` directory. HealthMailer should ignore any directory whose name starts with `.` and should process only final package directories that contain `READY`.

`READY` is created last inside the staged directory. The final package directory appears only after that marker exists.

Before a package is marked ready, printRxer verifies that:

- `prescription.pdf` begins with `%PDF-`.
- `request.json` records the same SHA256 as the prepared PDF.
- `request.sha256` contains the PDF SHA256 and the literal filename `prescription.pdf`.

## Required Request Metadata

`request.json` must include:

- Windows user, domain user, SID, and session ID.
- Workstation name and workstation domain.
- Print job origin metadata where available.
- Picker selection details.
- Selected recipient name and email address.
- Subject and body prepared by the picker.
- PDF SHA256.
- Package ID.
- Created, prepared, and ready timestamps.
- Audit note.

The package provides local audit evidence for the workstation handoff. Documentation and UI text should use "audit evidence", not "non-repudiation".

## Boundaries

printRxer prepares packages only. HealthMailer owns any downstream mail transport, delivery policy, retry policy, and server-side audit trail.

HealthMailer should treat malformed packages as failed intake, not as sendable work. In particular, it should reject packages where the PDF signature or SHA256 does not match the request metadata.
