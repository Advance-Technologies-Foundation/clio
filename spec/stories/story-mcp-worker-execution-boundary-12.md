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
- **Correction applied during implementation.** `ExecuteUnderTenantLock` / `ExecuteWithCleanLog` /
  `ResolveTenantLockKey` are `private protected` on `BaseTool<T>` (`BaseTool.cs:59`), and
  `MobilePageConversionGuideTool` derives from nothing (`:33`, a sealed class with a seven-service
  constructor). The helper is therefore **unreachable** from here. Rebasing the class onto
  `BaseTool<PageGetOptions>` was rejected: it costs constructor churn, a `command: null` base argument,
  and a second inherited `[McpServerToolType]` registration surface, for no safety the local fix does not
  already give. The implementation instead uses a balanced `try`/`finally` around the resolved tenant key
  at all three sites — same guarantee, no inheritance change.

## Acceptance Criteria
- [ ] AC-01 — Every `GetLock` at all three sites is balanced by `MarkAvailable`, including on the exception path (TC-U-F03).
- [ ] AC-02 — The mapping is not pinned after a completed call (TC-U-F03).
- [ ] AC-03 — The resolved tenant key is used, not `SharedFallbackKey` (TC-U-F04).
- [ ] AC-04 — Behaviour of the conversion itself is unchanged.

## Tests
`clio.tests/Command/McpServer/` — `[Category("Unit")]`, `Module=McpServer`. TC-U-F03, TC-U-F04.
