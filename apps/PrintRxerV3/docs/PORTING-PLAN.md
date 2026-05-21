# PrintRxer v3 Porting Plan

PrintRxer v3 is a workstation-side package creator. It captures or prepares a print/PDF package, opens a curated picker, records metadata, and writes a HealthMailer handoff package. It does not send mail.

| Legacy file | Purpose | Port decision: keep / adapt / reject | Reason | Target v3 location | Known dependencies | Security notes |
|---|---|---|---|---|---|---|
| `agent/Recipients/RecipientRecord.cs` | Recipient data model, CSV/workbook loading, data-source selection, validation, alias/search term construction. | adapt | The recipient model, CSV loading, header fallbacks, active-row filtering, and search-term ideas are useful. Workbook parsing, config coupling, validation reporting, and path-selection policy need separate v3 modules before being carried over. | `src/Recipients/RecipientRecord.cs`, `src/Recipients/RecipientCsvLoader.cs` | Old `AppConfig`, `Microsoft.VisualBasic.FileIO`, `System.IO.Packaging`, `XmlUtilities`. | Keep local-file-only recipient loading. Avoid remote address book lookup. Preserve safe XML parsing if workbook support is later adapted. |
| `agent/Recipients/RecipientPicker.cs` | WPF curated picker, preparing window, recipient search, subject/body/attachment editors, timeout handling, and direct Outlook action choices. | adapt | Search scoring, summary preview, timeout, and editor concepts are valuable, but old action model includes `ReviewInOutlook` and `SendNow` and must not be imported as-is. | Later `src/Recipients` picker UI/view-model plus `src/Packaging` request draft models. | WPF, `MailSettings`, attachment preparation tasks, old runtime tokens, Win32 foreground calls. | New picker must produce metadata only. No send/display/save-draft actions. Avoid early-picker staging behaviour as-is. |
| `agent/Common/Utilities.cs` | Whitespace normalization, safe XML loading, JSON helpers, random suffix generation, runtime user identity, path security checks, spooler interop. | adapt | Pure helpers can move in smaller modules. Security and identity helpers are important but should be separated from old runtime/config types. Spooler interop belongs only if v3 still manages queue cleanup. | `src/Common`, `src/Metadata`, later `src/Capture`. | `WindowsIdentity`, ACL APIs, `WorkingPaths`, `AppConfig`, winspool P/Invoke. | Preserve local ACL/reparse-point hardening for ProgramData paths. Use random/package IDs with cryptographic entropy. |
| `agent/Configuration/AppConfig.cs` | Legacy agent configuration, mail settings, rendering settings, processing settings, working path layout. | adapt | Recipient/path/rendering/processing options are useful patterns. `MailSettings`, direct-send modes, Outlook account settings, archive sent/drafts layout, and old defaults must not cross into v3. | Later `config`, `src/Common`, `src/Packaging`. | JSON serialization helpers, legacy `MailSettings`, archive directories. | v3 config must describe package creation and handoff locations only. It must not imply mail sending. |
| `agent/Runtime/AppRuntime.JobProcessing.cs` | Active job processing: move incoming job, render PDF, open picker, call Outlook COM, archive metadata, cleanup, early-picker staging, token/template helpers. | adapt | Capture-to-PDF preparation, metadata merge, package ID/token generation, safe filename, summary/template helpers, and failure handling can inform v3. Outlook handoff, direct send/display/save-draft, metadata-only archive deletion, and early-picker flow are rejected. | Later `src/Capture`, `src/Documents`, `src/Metadata`, `src/Packaging`, `src/Handoff`. | `RecipientPicker`, `OutlookMailer`, XPS/PDF renderer, `AppConfig`, runtime paths. | New terminal output is a HealthMailer handoff package with retained PDF, `request.sha256`, and `READY`. Use "audit evidence", not "non-repudiation". |
| `agent/Runtime/AppRuntime.State.cs` | Runtime state: recipient cache, owner matching, notifications, recycle, queue cleanup, path validation, utility methods. | adapt | Recipient cache refresh and owner metadata checks are relevant. Desktop notifications, recycle logic, queue deletion, and Outlook posture logging are legacy-agent concerns. | Later `src/Recipients`, `src/Metadata`, `src/Capture`. | `RecipientRecord`, `AppConfig`, WPF picker, notify icon, winspool P/Invoke. | Preserve owner/user SID checks where they protect workstation-local jobs. Do not retain mail posture concepts. |
| `native/PrintRxer.PortMonitor/PrintRxerPortMonitor.c` | Native port monitor capturing raw XPS to ProgramData spool/incoming with metadata and secure root ACL. | adapt | Local capture, secure root creation, job folder naming, submitting-user metadata, atomic staging-to-incoming move, and no-network posture are valuable. This should be reviewed carefully before v3 native integration. | Later `src/Capture` design docs or `native` if v3 includes a monitor. | Windows spooler monitor API, ProgramData, SDDL, filesystem APIs. | Keep no network transport. Preserve secure ACLs, no overwrite, free-space checks, metadata capture, and staging/ready boundaries. |

## Safe First Modules

The first safe modules are those with no Outlook/mail dependency:

- recipient model
- recipient CSV loading
- pure text utilities
- metadata models
- package ID generation
- hashing helpers
- summary template helpers

The first ported module is recipient model plus CSV loading only. Workbook loading, validation reports, recipient picker UI, package writing, and capture integration remain future steps.

## Explicitly Rejected For Now

- `agent/Outlook/OutlookMailer.cs`
- Outlook COM self-test code
- old installer scripts
- old release packaging scripts
- old direct mail send/display/save-draft modes
- early-picker staging flow as-is
