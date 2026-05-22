# Recipient Lists

The preferred recipient list lives here:

```text
<handoff folder>\recipients\recipients.csv
```

printRxer reads that file in the background and keeps a local last-known-good copy.

If the central file is unavailable, printRxer uses the last good local copy. If that is not available, it uses the original recipient list supplied with the installer.

The normal printRxer user does not need write access to the central recipients folder.

## Runtime Sources

printRxer uses these sources in order:

1. In-memory recipients already loaded by the app.
2. `C:\ProgramData\printRxer\data\recipients\recipients.cache.csv`
3. `C:\ProgramData\printRxer\data\recipients\bundled-recipients.csv`

The recipient picker opens from memory, cache, or bundled fallback. It does not wait on the central share during normal picker opening.

Central refresh runs once shortly after startup, then every 12 hours while printRxer is running. The picker also has a support/manual `Refresh recipients` button.

## Central File

Default central path:

```text
<HandoffRoot>\recipients\recipients.csv
```

Example:

```text
\\server\HealthMailerDrop$\incoming\recipients\recipients.csv
```

The central path is derived from `HandoffRoot`; normal deployments should not configure a separate recipient folder.

Recommended ACLs for `<HandoffRoot>\recipients`:

| Identity | Permission |
| --- | --- |
| IT / authorised maintainers | Read/write |
| printRxer runtime users or workstation identities | Read-only |
| HealthMailer | No access required unless local deployment makes read access unavoidable |
| Broad ordinary user groups | Avoid write access |

## Local Files

| File | Purpose |
| --- | --- |
| `C:\ProgramData\printRxer\data\recipients\bundled-recipients.csv` | Release-time safety fallback. |
| `C:\ProgramData\printRxer\data\recipients\recipients.cache.csv` | Last-known-good central list. |
| `C:\ProgramData\printRxer\data\recipients\recipient-source-status.json` | Last check, source used, central path, cache path, counts, and warning. |

## CSV Schema

Required columns:

```text
recipientId,displayName,email,active
```

Optional columns:

```text
organisation,site,department,service,sortOrder,notes
```

Rules:

- `recipientId` must be stable and unique.
- `displayName` is required for active recipients.
- `email` is required for active recipients and must be syntactically plausible.
- `active` must be true/false, yes/no, or 1/0.
- At least one active recipient must exist.
- Invalid central files are rejected and do not overwrite the local cache.

The recipients.csv file is an address book/configuration file only. It must not contain patient names, MRNs, prescription details, or other patient-identifiable information.

## IT Update Process

1. Prepare a new `recipients.csv`.
2. Validate it using the printRxer validation path or a test install.
3. Copy it to `<HandoffRoot>\recipients`.
4. Prefer copying to `recipients.csv.tmp`, then renaming to `recipients.csv`.
5. Keep a backup before replacing the file.

HealthMailer does not need this CSV for normal sending. The package itself remains the HealthMailer delivery contract.
