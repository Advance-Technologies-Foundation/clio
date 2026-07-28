# ClioRing clio-runtime switch test plan

## Design checkpoint

- Render the main Ring with the current Debug-DLL configuration.
- Verify the Development warning is the first visual emphasis and does not cover ring nodes.
- Render Release mode and verify it is calm but still identifiable.
- Stop before comprehensive review and Windows x64 NativeAOT publish for requester approval.

## Automated

- Default resolution selects Release and launches `clio mcp-server`.
- Legacy explicit `ClioIpc` and valid `DevClioPath` settings select Development.
- Explicit Release ignores but preserves the saved development target.
- Explicit Development rejects a missing/invalid target with an actionable UI state.
- Release resolution supports `DOTNET_CLI_HOME` without trusting arbitrary PATH executables.
- Ordinary radial actions and IPC workflows use the same Release or Development launch target.
- Store round-trips runtime mode without altering unrelated JSON fields.
- View-model states distinguish running mode, pending mode, and restart requirement.
- Release and Development banner controls have accessible names and keyboard focus.
- Run `dotnet test clio-ring/ClioRing.Tests/ClioRing.Tests.csproj -c Release` after UI approval.
- Run the mandatory Windows x64 NativeAOT publish after UI approval.
