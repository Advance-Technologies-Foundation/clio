# Story 12: MobilePageConversionGuideTool leaks the shared fallback lock mapping

**Feature**: mcp-worker-execution-boundary
**Jira**: [ENG-95262](https://creatio.atlassian.net/browse/ENG-95262)
**ADR**: [adr-mcp-worker-execution-boundary.md](../adr/adr-mcp-worker-execution-boundary.md)
**Test plan**: [tp-mcp-worker-execution-boundary.md](../test-plans/tp-mcp-worker-execution-boundary.md)
**Stage**: folded-in
**Status**: ready-for-dev
**Size**: S

## As a
tenant whose lock mapping is pinned forever by an unrelated tool

## I want
every `GetLock` balanced by `MarkAvailable`, and the real tenant key used

## So that
one mobile-conversion call does not permanently pin the shared fallback mapping

## Design
- `MobilePageConversionGuideTool` takes `lock (McpToolExecutionLock.GetLock(McpToolExecutionLock.SharedFallbackKey))` at three sites — `:111`, `:339`, `:531` — after it has already resolved a real tenant, and never calls the balancing `MarkAvailable`.
- `GetLock` **pins the lock-provider mapping in-use** (`McpToolExecutionLock.cs:157-159`), explicitly documented as "balanced by `MarkAvailable`". Unbalanced, the mapping is pinned permanently.
- Two defects in one: the wrong key (shared fallback rather than the resolved tenant, which serializes unrelated tenants) and the missing release.
- Use the same `ExecuteUnderTenantLock` path the other tools use rather than hand-rolling the `lock` — the helper balances by construction.

## Acceptance Criteria
- [ ] AC-01 — Every `GetLock` at all three sites is balanced by `MarkAvailable`, including on the exception path (TC-U-F03).
- [ ] AC-02 — The mapping is not pinned after a completed call (TC-U-F03).
- [ ] AC-03 — The resolved tenant key is used, not `SharedFallbackKey` (TC-U-F04).
- [ ] AC-04 — Behaviour of the conversion itself is unchanged.

## Tests
`clio.tests/Command/McpServer/` — `[Category("Unit")]`, `Module=McpServer`. TC-U-F03, TC-U-F04.
