# Handoff Contract

printRxer creates a package folder for HealthMailer.

## Package Structure

```text
<packageId>\
  request.json
  prescription.pdf
  request.sha256
  summary.txt
  READY
```

HealthMailer ignores directories beginning with `.` and processes only final folders containing `READY`.

## Operational Boundary

The shared handoff folder is an automated, cryptographically validated pipeline. Do not manually create, edit, repair, rename, or delete active package files except under an approved support procedure.

Manual manipulation of `request.json`, `request.sha256`, `prescription.pdf`, or `READY` will normally cause validation failure, quarantine, duplicate detection, or loss of audit clarity.

HealthMailer cross-checks that:

- the final folder is not dot-prefixed;
- `READY` exists;
- required files exist;
- the PDF starts with `%PDF-`;
- the PDF hash matches `request.json`;
- the PDF hash matches `request.sha256`;
- the recipient domain is allowed; and
- the package ID and completed package hash are not already in the ledger.

These controls reject packages that fail validation and reduce spoofing and accidental-ingestion risk. The ledger prevents duplicate sends by package ID and completed package hash. The handoff folder should normally be empty or contain only active package folders.

## `request.json`

Required fields include:

```json
{
  "packageId": "20260511-090000000-abcdef123456",
  "createdAt": "2026-05-11T09:00:00Z",
  "preparedAt": "2026-05-11T09:00:01Z",
  "readyAt": "2026-05-11T09:00:02Z",
  "selectedRecipientName": "Example Pharmacy",
  "selectedRecipientEmail": "pharmacy@healthmail.ie",
  "documentKind": "Prescription",
  "documentName": "Prescription",
  "attachmentDisplayName": "MRN123_prescription_20260511_0900.pdf",
  "subject": "Prescription",
  "body": "Please see attached.",
  "pdfSha256": "lowercase-hex-sha256",
  "mrn": "MRN123",
  "workstationIdentity": {
    "windowsUser": "user",
    "domainUser": "DOMAIN\\user",
    "userSid": "S-1-5-...",
    "sessionId": 3,
    "workstationName": "CLINIC-PC",
    "workstationDomain": "DOMAIN"
  },
  "printJobOrigin": {
    "source": "port-monitor",
    "printerName": "printRxer",
    "documentName": "Prescription",
    "printJobId": "42",
    "capturedAtUtc": "2026-05-11T09:00:00Z",
    "submittingUser": "DOMAIN\\user",
    "submittingUserSid": "S-1-5-..."
  }
}
```

## `request.sha256`

Format:

```text
<pdf-sha256>  prescription.pdf
```

The internal package PDF filename remains `prescription.pdf` for validation and
SHA256 matching. The email attachment may use the `attachmentDisplayName` value
as a friendlier outbound filename. MRN or patient identifiers in outbound
attachment filenames are patient-identifiable information and should only be
used for approved clinical matching workflows.

## Recipient Selection

printRxer requires an explicit recipient selection before it creates a handoff
package. The recipient picker auto-closes after 3 minutes if no selection is
completed. A timeout is treated as cancellation: no package is created and the
print job is deferred rather than silently selecting the first row.

HealthMailer also revalidates recipients at the final send boundary. By
default, live sends are restricted to these recipient domains:

```text
healthmail.ie
hse.ie
nmh.ie
rotunda.ie
```

Domain comparison is case-insensitive. Addresses outside the configured
HealthMailer allow-list, blank addresses, and malformed addresses are rejected
without Outlook send and archived with recipient-rejection evidence.

## HealthMailer Outputs

Before archival, HealthMailer writes:

```text
result.json
summary.txt
summary.html   optional
```

Canonical records are `request.json` and `result.json`.

## `result.json`

Example:

```json
{
  "packageId": "20260511-090000000-abcdef123456",
  "outcome": "Sent",
  "completedAtUtc": "2026-05-11T09:00:10Z",
  "message": "Package processed.",
  "recipientEmail": "pharmacy@healthmail.ie",
  "pdfSha256": "lowercase-hex-sha256",
  "completedPackageHash": "lowercase-hex-sha256",
  "documentKind": "Prescription",
  "documentName": "Prescription",
  "internalPackagePdf": "prescription.pdf",
  "attachmentDisplayName": "MRN123_prescription_20260511_0900.pdf",
  "mailSent": true,
  "chartCopied": false,
  "chartCopyPath": ""
}
```

`chartCopied` and `chartCopyPath` are retained legacy compatibility fields. Chart/ViewPoint copy is removed/deferred in the current release, so new results report `false` and an empty path.

## Outcomes

| Outcome | Meaning | Terminal folder |
| --- | --- | --- |
| `Sent` | Mail handoff succeeded. | `sent` |
| `Failed` | Unexpected processor failure. | `failed` |
| `Quarantined` | Reserved for quarantined safety outcome. | `quarantine` |
| `Duplicate` | Package ID or completed package hash already sent. | `quarantine` |
| `ValidationFailed` | Package malformed or hash mismatch. | `quarantine` |
| `RecipientRejected` | Recipient email is blank, malformed, or outside the HealthMailer allowed domain list. | `quarantine` |
| `ValidatedNoSend` | Package validated while `SendMail=false`; no Outlook send occurred. | `validated-no-send` |
| `ChartCopyFailed` | Legacy/deferred compatibility outcome; not produced by the current chart-copy-disabled processing path. | `failed` |
| `MailFailed` | Outlook handoff failed. | `failed` |

## Ledger

HealthMailer appends successful and terminal mail-attempt records to:

```text
C:\ProgramData\HealthMailer\processed-ledger.jsonl
```

This prevents duplicate sends by package ID or completed package hash.

