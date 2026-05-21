# Handoff Contract

PrintRxerV3 creates a package folder for HealthMailer.

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

## `request.json`

Required fields include:

```json
{
  "packageId": "20260511-090000000-abcdef123456",
  "createdAt": "2026-05-11T09:00:00Z",
  "preparedAt": "2026-05-11T09:00:01Z",
  "readyAt": "2026-05-11T09:00:02Z",
  "selectedRecipientName": "Example Pharmacy",
  "selectedRecipientEmail": "pharmacy@example.ie",
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
  "recipientEmail": "pharmacy@example.ie",
  "pdfSha256": "lowercase-hex-sha256",
  "completedPackageHash": "lowercase-hex-sha256",
  "mailSent": true,
  "chartCopied": false,
  "chartCopyPath": ""
}
```

## Outcomes

| Outcome | Meaning | Terminal folder |
| --- | --- | --- |
| `Sent` | Mail handoff succeeded and optional chart copy succeeded or was disabled. | `sent` |
| `Failed` | Unexpected processor failure. | `failed` |
| `Quarantined` | Reserved for quarantined safety outcome. | `quarantine` |
| `Duplicate` | Package ID or completed package hash already sent. | `quarantine` |
| `ValidationFailed` | Package malformed or hash mismatch. | `quarantine` |
| `ChartCopyFailed` | Mail succeeded, chart copy failed. | `failed` |
| `MailFailed` | Outlook handoff failed before chart copy. | `failed` |

## Ledger

HealthMailer appends successful and terminal mail-attempt records to:

```text
C:\ProgramData\HealthMailer\processed-ledger.jsonl
```

This prevents duplicate sends by package ID or completed package hash.
