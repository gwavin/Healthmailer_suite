# PrintRxer Port Monitor

This folder contains the native capture half of the active `printRxer` design.

## Purpose

The port monitor exposes one fixed local port, `printrx:`, and captures incoming XPS jobs to disk so the managed agent can process them.

The intended flow is:

1. a workstation prints to the local `printRxer` queue
2. the port monitor captures the XPS payload locally
3. the managed agent processes that local job directory
4. the managed agent hands the result to Outlook

## Current Scope

The implementation in [PrintRxerPortMonitor.c](PrintRxerPortMonitor.c) is deliberately small:

- it exposes exactly one fixed port, `printrx:`
- it captures each job to `%ProgramData%\printRxer\work\spool\<job-folder>\job.xps`
- it writes a `metadata.json` sidecar
- it moves the completed job into `%ProgramData%\printRxer\work\incoming\<job-folder>`
- it leaves PDF rendering, curated-recipient search, and Outlook automation to the managed agent

## Safety-Relevant Notes

- This component writes to local disk only.
- It does not contain HTTP, SMTP, or socket send logic.
- It is part of the local workstation capture path, not a relay service.

## Build And Install

Build with [tools/Build-PrintRxerPortMonitor.ps1](../../tools/Build-PrintRxerPortMonitor.ps1).

Install with [tools/Install-PrintRxerPortMonitor.ps1](../../tools/Install-PrintRxerPortMonitor.ps1).

## Platform Constraint

Windows will not pair the built-in `Microsoft XPS Document Writer v4` driver with this non-inbox port monitor.

That means the queue needs the custom v3 driver package tracked in [../PrintRxer.Driver/README.md](../PrintRxer.Driver/README.md).

## Not In Scope

This component does not attempt to provide:

- dynamic port creation or deletion
- a configuration UI
- bidirectional printer status
- remote transport
- anything beyond a fixed local capture target for `printRxer`

