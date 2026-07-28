# SPEC: clio-ring companion integration

**Status:** Approved
**Created:** 2026-07-12

## Outcome

Ship the proven clio-ring POC as an opt-in internal companion to clio without making the desktop application part of clio's runtime or NuGet tool payload.

## Requirements

- Keep all Ring application sources in an isolated `clio-ring/` subtree.
- Keep clio core independent of Avalonia and Ring assemblies.
- Expose a feature-gated `clio ring [launch|install|update|version|status|uninstall]` bootstrap command.
- Download Windows ZIP releases and a manifest from GitHub Releases over HTTPS.
- Verify SHA-256 before extraction and reject archive path traversal.
- Install versions side-by-side under `%LOCALAPPDATA%\Creatio\clio-ring`, with an atomic current-version pointer.
- Publish Ring ZIP, SHA-256, and manifest artifacts from a path-filtered GitHub Actions workflow.
- Version Ring independently as an internal `0.x` preview.
- Collect no telemetry.
- Allow complete removal by deleting the Ring subtree, workflow, command, docs, and solution entries.

## Non-goals

- Bundling Ring into the `dotnet tool install clio` NuGet package.
- Automatically installing or launching Ring.
- Adding Ring commands to MCP.
- Supporting non-Windows platforms in the initial preview.
- Promising long-term compatibility or product status.

## Acceptance

1. With feature `ring` disabled, the command is invisible and unreachable.
2. With the feature enabled, install/update validates a signed-infrastructure HTTPS manifest and artifact SHA-256, then launches the selected version.
3. Status/version do not require Ring to be running.
4. Uninstall removes Ring binaries without modifying clio configuration or registered environments.
5. Normal clio build/test/release remains independent of Ring.
6. Ring release workflow produces a self-contained `win-x64` ZIP, checksum, and manifest.
