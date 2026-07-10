# Story 2: Forgiving unmatched-name handler (execute / confirm / actionable error)

**Feature**: mcp-durable-invocation
**FR coverage**: FR-1, FR-2, FR-3, FR-5
**PRD**: [prd-mcp-durable-invocation.md](../prd/prd-mcp-durable-invocation.md)
**ADR**: [adr-mcp-durable-invocation.md](../adr/adr-mcp-durable-invocation.md) (D1, D2, D3, D5)
**Status**: ready-for-dev
**Size**: L
**Depends on**: story-mcp-durable-invocation-1

## As a
an AI agent following static guidance that names a long-tail clio tool directly

## I want
the server to resolve and (safely) run that name instead of returning an opaque "Unknown tool"

## So that
stale/legacy guidance that reaches the server self-heals rather than dead-ending

## Design
- New `clio/Command/McpServer/McpDurableCallToolHandler.cs`, registered via `WithCallToolHandler` in `BindingsModule.RegisterMcpServer` (`:801-807`), alongside the existing `AddCallToolFilter`.
- Runs only on `ToolCollection` miss (`context.MatchedPrimitive is null && name not empty`). Pulls `IClioRunExecutor` + `IMcpToolCompatibilityCatalog` from `context.Services`.
- Resolution order per ADR D2. On resolve:
  - non-destructive → `IClioRunExecutor.RunAsync(canonical, args, ...)`; append `_meta` advisory note (prefer `clio-run <canonical>` / resident).
  - destructive (`IMcpToolInvokerRegistry.IsDestructive`, fail-closed) → `confirmation-required` with ready-to-retry `clio-run-destructive` shape; do NOT execute.
- On non-resolve → structured error (ADR D5 codes) + Levenshtein did-you-mean (reuse `ClioRunExecutor` helper) + discovery hint. Mirror data in `StructuredContent`, concise text for old clients.

## Acceptance Criteria
- [ ] AC-01 — Direct `tools/call get-fsm-mode` executes and returns FSM mode + advisory note.
- [ ] AC-02 — Direct `tools/call restart-by-environment-name` returns `confirmation-required` + retry shape; no restart occurs.
- [ ] AC-03 — Deprecated alias executes via canonical resolution.
- [ ] AC-04 — Unknown name returns `unknown-tool` with did-you-mean + discovery hint; foreign name returns `foreign-command`.
- [ ] AC-05 — Resident tools are unaffected (handler never runs for a `tools/list` hit); no duplicate execution path.
- [ ] AC-06 — `tools/list` still returns 27 (no surface mutation).

## Tests
`clio.tests/Command/McpServer/McpDurableCallToolHandlerTests.cs` — execute+note, destructive→confirm (no side effect via substitute), alias resolve, unknown/foreign errors; `[Category("Unit")]`, `Module=McpServer`.
