# Handoff Folder Setup

The handoff folder is the boundary between printRxer and HealthMailer.

printRxer builds completed package folders locally first, then publishes them to the handoff folder. HealthMailer watches, validates, sends, and moves packages out.

## No Mapped Drive Required

Use UNC paths for shared deployments:

```text
\\server\HealthMailerDrop$\incoming
```

Mapped drives are user-session state and are not reliable for scheduled tasks.

UNC permissions must be configured server-side by IT. Local ACL hardening performed by the installer applies only to local folders on the HealthMailer or printRxer machine; it does not secure a remote file share.

## Identities

| Identity | Needs |
| --- | --- |
| printRxer writer | Create package staging folders and final package folders. |
| HealthMailer watcher | Read, create lock files, move completed packages out. |
| IT/admin/support | Administer ACLs and collect support bundles. |
| Ordinary clinical user | No direct browse/delete access required. |

## Write-Only Drop Pattern

Where possible, configure the shared folder so ordinary users do not browse existing packages. printRxer needs to create and write its own package folder. HealthMailer needs broader rights because it validates and moves packages.

Recommended practical split:

```text
\\server\HealthMailerDrop$\incoming
  printRxer writer: create/write
  HealthMailer watcher: read/write/delete or modify
  Administrators/support: full control
  Ordinary users: no direct browse/delete unless operationally required
```

Ordinary users should not receive direct browse/delete access unless that access is explicitly approved by the local site. The normal workflow should not require users to open the share.

## Copy-Paste IT Request

```text
Please create a secured UNC handoff folder for PrintRxer Suite:

Preferred path:
\\<server>\HealthMailerDrop$\incoming

Purpose:
printRxer will write completed handoff package folders containing prescription PDFs and metadata.
HealthMailer will watch the folder, validate packages, send via Outlook/Healthmail, and move packages out.
The folder will contain PHI.

Requested access model:
- printRxer writer identity: create/write package folders and files.
- HealthMailer watcher identity: read, write lock files, and move/delete completed package folders.
- IT/admin/support group: full control for administration and support.
- Ordinary clinical users: no direct browse/delete access unless locally approved.

Please use UNC access, not a mapped drive dependency.
Please confirm the effective identities that will run printRxer and HealthMailer before go-live.
```

## Half-Written Package Protection

printRxer writes packages in a local durable outbox first. When publishing to the handoff folder it copies into a hidden upload directory:

```text
.uploading-<packageId>-<suffix>
```

It copies `READY` last, then moves the folder to its final package ID. HealthMailer ignores dot-prefixed staging folders and processes only final folders containing `READY`. On normal NTFS/SMB shares the final directory rename is atomic within the same folder; if a storage platform cannot provide atomic rename, the `READY` and dot-folder rules still prevent HealthMailer from sending partial packages.

If the UNC share is unavailable, printRxer keeps the package under `LocalOutboxRoot` and retries later. Jobs should not be lost merely because the network handoff folder is temporarily down.

## Duplicate Protection

HealthMailer keeps:

```text
C:\ProgramData\HealthMailer\processed-ledger.jsonl
```

If a package ID or completed package hash has already been sent, the package is quarantined rather than resent.

## Terminal Folders

HealthMailer moves packages into local terminal folders:

```text
C:\ProgramData\HealthMailer\sent
C:\ProgramData\HealthMailer\failed
C:\ProgramData\HealthMailer\quarantine
```

The shared handoff folder should normally be empty or contain only active packages.

