# ring

## Command type

Experimental integration tool.

## Synopsis

```powershell
clio experimental --name ring --enable
clio ring [launch|install|update|version|status|uninstall] [--manifest-url <https-url>]
```

## Description

Manages the internal clio-ring Windows x64 desktop preview. The Ring application is not bundled into the clio NuGet tool: clio downloads its independently versioned ZIP from GitHub Releases, verifies the manifest SHA-256, and installs it side-by-side under `%LOCALAPPDATA%\Creatio\clio-ring`.

The feature is off by default, collects no telemetry, and may be removed if the POC does not prove useful.

## Actions

- `launch` — starts the current version; this is the default action.
- `install` — downloads and installs the current preview.
- `update` — downloads and checksum-verifies the current preview, including the active version, so an incomplete same-version installation is repaired transactionally.
- `version` — prints the active version.
- `status` — reports whether the active installation is complete.
- `uninstall` — removes Ring binaries only; clio environments and settings are untouched.

## Options

- `--manifest-url <https-url>` — release manifest URL. Production use is restricted to this repository's `ring-latest` release.

## Security

The bootstrap rejects unsupported manifest schemas/RIDs, non-HTTPS URLs, checksum mismatches, corrupt archives, and ZIP entries that escape the installation directory.

## Examples

```powershell
clio experimental --name ring --enable
clio ring install
clio ring
clio ring status
clio ring update
clio ring uninstall
```
