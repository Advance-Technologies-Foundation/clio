# Story mcp-progress-heartbeat-1 — Reusable progress-heartbeat helper

- **Feature:** `mcp-progress-heartbeat` · **Jira:** ENG-91274 · **ADR:** `spec/adr/adr-mcp-progress-heartbeat.md`
- **Status:** ready-for-dev

## Story
As the clio MCP server, I want a reusable helper that emits periodic
`notifications/progress` while a synchronous backend call runs, so MCP clients reset
their inactivity timer instead of timing out mid-operation.

## Acceptance criteria
1. `McpProgressHeartbeat` runs a synchronous `work` delegate and returns its result.
2. While `work` runs, a `beat` callback is invoked at a fixed interval (default 15 s) on a
   background task; cadence is driven by a linked `CancellationToken`.
3. When `progressToken` is absent, the helper runs `work` inline with **no** beats and no
   added behavior (byte-for-byte current behavior).
4. Exceptions thrown by `work` propagate unchanged; the heartbeat is stopped in all cases
   (success, throw, cancellation).
5. Beat send failures are swallowed and never break `work`.
6. No `CLIO*` warnings; behavior class resolved/used per DI policy (static helper acceptable
   as it is stateless and has no injected behavior dependencies — documented in ADR).

## Definition of Done
- [ ] `clio/Command/McpServer/Tools/McpProgressHeartbeat.cs` added with XML docs.
- [ ] Unit tests TC-U-1..TC-U-5 implemented and green.
- [ ] `dotnet test --filter "Category=Unit&Module=McpServer"` green.
