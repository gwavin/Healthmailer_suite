# PrintRxer Driver

This folder contains the custom driver scaffold required to create the local `printRxer` queue on the `printrx:` port.

## Why This Exists

The custom `printrx:` port monitor works, but Windows blocks the built-in Microsoft XPS v4 driver from using a non-inbox port monitor.

On this machine, attempting to create the queue with `Microsoft XPS Document Writer v4` produced PrintService Admin event `242`:

`Printer driver 'Microsoft XPS Document Writer v4' may not be used in conjunction with a non-inbox port monitor.`

The practical consequence is that `printRxer` needs a custom v3 driver package rather than a built-in v4 driver.

## Current Package Shape

The scaffold in this folder includes:

- [PrintRxerXpsDrv.inf](PrintRxerXpsDrv.inf)
- [PrintRxerXpsDrv.gpd](PrintRxerXpsDrv.gpd)
- [PrintRxer-pipelineconfig.xml](PrintRxer-pipelineconfig.xml)

The design goal is:

- keep `printrx:` as the capture port
- preserve an XPS-compatible render path for the managed agent
- install under queue name `printRxer`
- use driver name `PrintRxer XPS Driver`

## Safety-Relevant Notes

- This folder defines printer package metadata and packaging instructions.
- The `https://printrxer.local/printschema/private` string in the GPD is a print schema namespace, not a network endpoint used by the runtime.
- This driver scaffold is part of the local workstation printing path, not a remote transport path.

## Build And Install

Use [tools/Build-PrintRxerDriverPackage.ps1](../../tools/Build-PrintRxerDriverPackage.ps1) to stage the driver package.

Use [tools/Install-PrintRxerDriver.ps1](../../tools/Install-PrintRxerDriver.ps1) to test-sign, stage, and install it locally.

## Remaining Work

What still remains before the queue can be installed reliably on a clean machine:

- settle the final INF model metadata and hardware-ID strategy
- sign the generated catalog for local installation
- install the driver package and rerun [tools/Install-PrintRxerQueue.ps1](../../tools/Install-PrintRxerQueue.ps1)
