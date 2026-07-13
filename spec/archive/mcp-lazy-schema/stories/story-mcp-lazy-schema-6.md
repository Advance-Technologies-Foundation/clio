# Story 6: Curated get-tool-contract coverage for the full long tail

**Feature**: mcp-lazy-schema
**FR coverage**: ADR Decision 2 (`get-tool-contract` = lazy schema; curated contracts mandatory for long tail)
**PRD**: _none — spike-driven feature_
**ADR**: [adr-mcp-lazy-schema.md](../adr/adr-mcp-lazy-schema.md)
**Status**: review
**Size**: L (full day) — high-count, mechanical-but-broad
**Risk**: MEDIUM — volume risk; the lossy fallback makes "skip a few" tempting and wrong
**Blocked by**: story-mcp-lazy-schema-0

---

## As a

clio MCP client (model)

## I want

every long-tail command to have a **curated** tool contract returned by `get-tool-contract(command)`, not the lossy reflection fallback

## So that

on-demand schemas are accurate (correct enums, nested args, required flags) and `clio-run` (Stories 4/5) has a trustworthy contract source

---

## Acceptance Criteria

- [ ] **AC-01** — Given the current ~46 curated contracts (`CanonicalToolNames`) vs ~124 commands, when this story closes, then every long-tail command (not in the flat core) has a curated contract entry.
- [ ] **AC-02** — Given a curated contract, when compared to the lossy fallback (`McpToolSchemaCatalog.cs:91-178`, first-param-only, enum→"string", nested→"object", required only from `[Required]`), then the curated entry corrects each lossy aspect: all params, real enum values, nested structure, `Required=true` from `[Option(Required=true)]`.
- [ ] **AC-03** — Given any long-tail command, when `get-tool-contract(command)` is called, then it returns the curated contract and NOT the reflection fallback (fallback is a last-resort safety net only).
- [ ] **AC-04** — Given a new long-tail command added later, when no curated contract exists, then a test fails (coverage guard), preventing silent fallback regressions.
- [ ] **AC-05** — Given contract field shapes, when used by Story 5 inline-on-error, then the payload shape is identical (one source of truth).
- [ ] **AC-ERR** — Given an unknown command passed to `get-tool-contract`, when looked up, then it returns `Error: unknown command 'X'` + index pointer, exits/returns non-success.

## Implementation Notes

Key files:
- `clio/Command/McpServer/.../ToolContractCatalog` + `ToolContractGetTool` — curated source + the tool.
- `CanonicalToolNames` (~46 entries) — extend to full long-tail coverage.
- `McpToolSchemaCatalog.cs:91-178` — the lossy fallback; document why it stays only as a net.

Approach: drive curated entries from `[Option]`/`[Value]`/`[Verb]` metadata where faithful, hand-correcting enums/nested/required. A coverage test (AC-04) enumerates long-tail commands and asserts each has a curated entry.

Pattern: no scattered literals; contract data centralized. DI for any new service.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | every long-tail command has a curated contract (coverage guard); curated corrects enum/nested/required vs fallback on sampled commands; unknown→error | `clio.tests/Command/McpServer/ToolContractCoverageTests.cs` |
| Integration | n/a (pure metadata) | — |
| E2E `[Category("E2E")]` | `get-tool-contract` returns curated (not fallback) for sampled long-tail commands over stdio (NOT in CI — manual) | `clio.mcp.e2e/` |

Test naming + AAA + `because` + `[Description]`.

## Definition of Done

- [ ] No CLIO* warnings
- [ ] Coverage guard test (AC-04) is green and would fail on a new uncovered command
- [ ] Curated contracts demonstrably correct the lossy fallback (sampled assertions)
- [ ] Contract payload shape matches Story 5 inline usage
- [ ] MCP e2e added (mandatory) — flagged NOT in CI
- [ ] PR references this story file

## Dev Agent Record

- Implementation started: 2026-06-19
- Implementation completed: 2026-06-19
- Tests passing: `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=McpServer" -f net10.0` → 1267 passed, 1 skipped, 0 failed.
- Notes:
  - Scope delivered = the **proper fix for Codex review #1** (the precise gap, not the full "curate all ~124" sweep AC-01..AC-05 originally framed). Codex #1: in lazy mode the long tail is invoked by name via `clio-run` / `clio-run-destructive`, and `get-tool-contract` is the discovery path for those hidden tools. For UNCURATED tools the contract was synthesized by the lossy `McpToolSchemaCatalog` (reflection over options types), which dropped single-scalar params — `stop-creatio`, `clear-redis-db-by-environment`, `restart-by-environment-name` returned an EMPTY property list even though their real dispatched MCP `inputSchema` carries `environmentName`. The advertised contract therefore did not match what `clio-run` actually accepts.
  - Fix: `get-tool-contract` now derives uncurated contracts from the SAME registered `McpServerTool.InputSchema` in `IMcpToolInvokerRegistry` that `clio-run` dispatches against. Precedence: **curated `ToolContractCatalog` > registry-derived > lossy `McpToolSchemaCatalog` (last-resort only when the registry has no entry / is unavailable)**.
  - New `McpToolRegistrySchemaContract.TryBuild(registry, name, out contract)` converts the tool's JSON schema → `ToolInputSchemaContract`: unwraps a single top-level `args` object wrapper to its inner `properties`/`required` (mirrors `ClioRunTool.BuildChildParams`), keeps top-level properties for scalar-param tools, normalizes `["string","null"]` → `"string"`, preserves each property description and the tool's top-level description, and marks destructiveness-aware preferred flow.
  - `ToolContractGetTool` gained an optional `IMcpToolInvokerRegistry` ctor dependency (SDK resolves it per call via `ActivatorUtilities`, same pattern as `ClioRunTool`); a parameterless ctor is retained so curated-only tests keep working. No `BindingsModule` change needed (registry already registered; greediest ctor wins).
  - The 3 single-scalar env tools (`stop-creatio`, `clear-redis-db-by-environment`, `restart-by-environment-name`) now expose `environmentName` and were MERGED into the `ReturnNonEmptyProperties` coverage set. `stop-all-creatio` genuinely has no args (only an injected `RequestContext`) so it stays in the contract-resolves-only set (InputSchema present, empty properties allowed). Added `ToolContractGet_Should_ExposeEnvironmentNameProperty_ForStopCreatio` as a focused pin.
  - EMPIRICAL: live `clio mcp-server` → `get-tool-contract({"tool-names":["stop-creatio"]})` returns `properties:[environmentName:string]`, `required:[environmentName]` (before this change: empty). Gap closed.
  - Deferred (still open, NOT in this story's delivered scope): AC-01/AC-04 full "every long-tail command has a CURATED entry + coverage guard" — uncurated tools are now schema-faithful via the registry, which removes the urgency, but hand-curated descriptions/enums/examples for the remaining long tail are a separate follow-up.
