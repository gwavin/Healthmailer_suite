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

## Current Release Status

The release bundle includes the prebuilt driver package and the normal printRxer installer installs or repairs the port monitor, driver, and local queue. Target machines do not need the SDK, WDK, Visual Studio, or C++ build tools.

## Operational Notes

- Installing or removing printer-capture components requires administrator approval.
- The prebuilt package must remain paired with the release bundle that produced it.
- Use [tools/Install-PrintRxerDriver.ps1](../../tools/Install-PrintRxerDriver.ps1) and [tools/Install-PrintRxerQueue.ps1](../../tools/Install-PrintRxerQueue.ps1) only for approved repair or development work; normal installation should use the bundled setup application.
