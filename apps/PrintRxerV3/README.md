# printRxer

printRxer is a workstation-side package creator for HealthMailer handoff packages.

The intended flow is:

```text
print/PDF capture -> document preparation -> curated picker -> request metadata -> HealthMailer handoff package
```

printRxer does not send mail and does not include Outlook COM, SMTP, Microsoft Graph, or Power Automate senders.

The default live handoff queue is:

```text
C:\ProgramData\printRxer\handoff
```

HealthMailer should consume only package directories with a `READY` marker and should ignore `.writing-*` staging directories.

