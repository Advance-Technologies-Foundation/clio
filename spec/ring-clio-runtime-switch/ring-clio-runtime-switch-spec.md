# ClioRing clio-runtime switch specification

Issue: #903

## Goal

Make the installed `clio` dotnet tool the ordinary ClioRing runtime while keeping an explicit local
development runtime easy to select and impossible to overlook on the main Ring surface.

## Requirements

- Release mode launches a verified `clio mcp-server` dotnet-tool shim from the standard user tool directory
  or `DOTNET_CLI_HOME`. Custom paths remain explicit Development targets.
- A valid `DevClioPath` or explicit `ClioIpc` command remains a saved development target.
- Existing settings with a development target and no mode retain that target during migration and are
  classified as Development rather than silently switching.
- The selected mode is persisted independently from the saved development target.
- The main Ring surface shows a prominent amber warning whenever the running runtime is Development.
- The warning identifies the runtime and provides a Release/Development selector without requiring the
  Settings overlay.
- Release mode uses a compact, calm identity indicator instead of a warning.
- A mode change must never claim the running child changed before it actually did. The first delivery may
  require a Ring restart, but the UI must say so clearly and offer the next action.
- The Settings overlay uses the same Release and Development terminology.
- Deployment workflows, environment discovery, and ordinary radial actions all use the same resolved runtime.
- If Development is selected without a valid target, Ring safely runs Release and shows an actionable warning.

## Acceptance criteria

- With no explicit development target, Ring resolves to `clio mcp-server` and selects Release.
- With the current explicit Debug-DLL `ClioIpc` settings, Ring selects Development and shows the main warning.
- Selecting Release persists the choice without deleting the development target.
- Selecting Development later reuses the saved target.
- The UI design is rendered and approved before comprehensive review or NativeAOT validation.
- Focused tests cover resolution, migration, persistence, and both main-surface states.
- The final Windows x64 NativeAOT publish is clean.
