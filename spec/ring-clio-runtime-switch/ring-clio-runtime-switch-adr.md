# ADR: Separate ClioRing runtime selection from runtime configuration

Status: accepted

## Context

ClioRing currently resolves a valid `DevClioPath`, then an explicit `ClioIpc` command, then a hard-coded
repository Debug DLL. The Settings overlay calls the last two cases a "normal build", and the main Ring
does not disclose the active source. A user can therefore perform destructive work with an unintended
development clio.

## Decision

Persist an explicit `ClioRuntimeMode` value with `release` and `development` choices. Keep the existing
`DevClioPath` and `ClioIpc` fields as the saved development target; changing modes does not erase them.

Release resolves to `Command = "clio"`, `Args = ["mcp-server"]`, using the dotnet tool on `PATH`.
Development resolves through the existing valid `DevClioPath` then explicit `ClioIpc` precedence. For
backwards compatibility, settings that have a development target but no `ClioRuntimeMode` migrate in
memory to Development; clean settings migrate to Release.

Expose the resolved source as a small immutable runtime-selection model consumed by startup and UI.
The first implementation applies changes on Ring restart because the registered IPC client owns one
immutable child launch configuration for the app session. The main warning must distinguish "running"
from "selected for next launch" and provide a restart action before it may claim Release is active.

Render a reserved-height banner above the radial control when Development is running. This avoids
covering action nodes and makes the warning part of the main visual hierarchy. Release uses a compact
status pill in the same reserved region.

## Consequences

- Ordinary installs use the released dotnet tool instead of a machine-specific repository path.
- Existing developer configurations remain active and become conspicuous rather than breaking silently.
- A selector can switch modes without destroying the development target.
- The Ring window grows vertically while the development warning is present; the radial geometry remains
  unchanged.
- Immediate in-process retargeting is deferred; a future change may add it behind the same selection model.
