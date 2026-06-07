# HealthMailer

HealthMailer is the local Outlook courier for printRxer handoff packages.

It watches a configured handoff folder, validates a completed printRxer package, sends the PDF through the logged-in user's Outlook profile, writes terminal audit records, and archives the package locally.

## Runtime Flow

```text
printRxer package folder
  request.json
  prescription.pdf
  request.sha256
  READY
        |
HealthMailer watcher
        |
validate READY + PDF signature + SHA256
        |
recipient domain allow-list + live-send approval
        |
Outlook COM send using current user profile
        |
result.json + summary.txt
        |
local sent/failed/quarantine archive
```

HealthMailer does not use SMTP, Microsoft Graph, a relay service, or embedded credentials.

HealthMailer can be installed by itself on the Outlook/Healthmail machine. It does not require printRxer to be installed locally; it only needs access to the configured handoff folder.

## Install

Publish the single-file EXE:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Publish-HealthMailer.ps1
```

Run the setup wizard:

```powershell
.\publish\HealthMailer\HealthMailer.exe --install
```

The wizard asks the user to browse to the printRxer handoff folder.

It writes config to:

```text
%ProgramData%\HealthMailer\healthmailer.settings.json
```

and registers a per-user logon scheduled task named `HealthMailer`.

The handoff folder may be local for same-machine testing or a UNC path such as `\\server\HealthMailerDrop$\incoming` for site deployment. UNC paths should be used directly rather than mapped drive letters.

## Secure Handling

HealthMailer rejects packages unless all of these are true:

- the package folder is not a staging folder such as `.writing-*`
  or `.uploading-*`
- `READY` exists
- `request.json`, `prescription.pdf`, and `request.sha256` exist
- `prescription.pdf` begins with `%PDF-`
- the actual PDF SHA256 matches both `request.json` and `request.sha256`
- a selected recipient email is present, well-formed, and in the HealthMailer allowed domain list

`SendMail=false` is a dry-run/no-send mode. Valid packages are archived under
`validated-no-send`, `MailSent` remains `false`, and the duplicate-send ledger
is not poisoned for a later approved live send. Live Outlook sending requires
`SendMail=true` and `LiveSendingApproved=true` in an explicit configuration.

Every terminal processing attempt writes `result.json` and a human-readable
`summary.txt` into the package before it is archived. `summary.html` can be
enabled with `WriteHtmlSummary`; generated HTML is self-contained and does not
use scripts or external resources.

HealthMailer also maintains:

```text
%ProgramData%\HealthMailer\processed-ledger.jsonl
```

The ledger remains full append-only audit evidence. The active duplicate cache loads valid timestamped sent records from the previous 30 days and keeps legacy timestamp-less records fail-closed. Do not manually edit or truncate the ledger.

The ledger records package IDs and completed package hashes once `MailSent=true`.
If either value is seen again, HealthMailer quarantines the duplicate instead of
sending it. Legacy chart-copy audit outcomes remain readable for compatibility.

The default processing order is:

```text
validate -> mail send -> result.json -> archive
```

Chart/ViewPoint copy is removed/deferred in the current release.

For local folders, the installer attempts to harden ACLs for the HealthMailer root and configured folders. For shared folders, ACLs must still be set correctly on the file server. The share should be restricted to the printRxer writer identity, the HealthMailer runtime user, local admins, and authorised support admins only.

Processed packages are moved out of the handoff folder into:

```text
%ProgramData%\HealthMailer\sent
%ProgramData%\HealthMailer\validated-no-send
%ProgramData%\HealthMailer\failed
%ProgramData%\HealthMailer\quarantine
```

Successful `sent` archives keep audit files, but `prescription.pdf` is removed after the configured `SentPrescriptionRetentionDays` period. The installer default is 14 days. Failed and quarantined packages are preserved for explicit review.

## Commands

Run watcher:

```powershell
HealthMailer.exe --watch
```

Process current ready packages once:

```powershell
HealthMailer.exe --process-once
```

Validate config and Outlook registration:

```powershell
HealthMailer.exe --validate
```

Uninstall scheduled task:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Uninstall-HealthMailer.ps1
```

Remove local HealthMailer data as well:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Uninstall-HealthMailer.ps1 -RemoveData
```

## ViewPoint Import

ViewPoint/chart copy is removed/deferred and cannot be enabled in the current release. Legacy audit fields and outcomes remain readable for compatibility with older evidence. Any future import workflow requires separate design, security review, and local validation.

