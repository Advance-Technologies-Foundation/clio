# Test Plan — Durable MCP invocation

- **Status:** Draft (pending ADR acceptance)
- **Date:** 2026-07-10
- **PRD:** [prd-mcp-durable-invocation.md](../prd/prd-mcp-durable-invocation.md)
- **ADR:** [adr-mcp-durable-invocation.md](../adr/adr-mcp-durable-invocation.md)

## Scope & risk

Changes touch `BindingsModule.cs` (DI composition root → full unit suite trigger), a
new MCP call-path handler, a new compatibility catalog, the invoker registry
(duplicate handling), the shipped workspace template, and MCP e2e. Highest risks:
(1) destructive tool executed without confirmation; (2) resident-tool path
regressed / double-executed; (3) `tools/list` surface accidentally changed
(breaks #743 economy); (4) parity drift between `clio-run` and the fallback handler.

## Test tiers

- **Unit** (`[Category("Unit")]`, `Module=McpServer`) — catalog, handler, registry,
  drift scan. Run: `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=McpServer"`.
- **Full unit suite** — required (BindingsModule touched): `--filter "Category=Unit"`.
- **E2E** (`clio.mcp.e2e`) — live stdio protocol; not in GitHub CI (flag in summary).
- **Manual protocol repro** — JSON-RPC over `clio mcp-server` (the diagnostic harness).

## Unit test cases

| ID | Story | Assertion |
|---|---|---|
| TC-U-01 | 1 | `TryResolve` maps a deprecated alias → canonical, case-insensitively. |
| TC-U-02 | 1 | Duplicate canonical name in catalog ⇒ throws at construction. |
| TC-U-03 | 1 | Duplicate alias (or alias == other canonical) ⇒ throws. |
| TC-U-04 | 1 | Feature-disabled canonical ⇒ `TryResolve` returns false. |
| TC-U-05 | 1 | `McpToolInvokerRegistry` throws on duplicate tool name (was: silently skipped). |
| TC-U-06 | 2 | Handler resolves a non-destructive long-tail name and invokes it via `IClioRunExecutor` (substitute verifies one call, canonical name). |
| TC-U-07 | 2 | Result of TC-U-06 carries the `_meta` advisory note. |
| TC-U-08 | 2 | Handler on a destructive name returns `confirmation-required` with a `clio-run-destructive` retry shape and does **not** call the executor (substitute: zero invocations). |
| TC-U-09 | 2 | `IsDestructive` fail-closed: unknown/ambiguous ⇒ treated destructive (no silent exec). |
| TC-U-10 | 2 | Unknown name ⇒ `unknown-tool` code + Levenshtein candidates + discovery hint in `StructuredContent`. |
| TC-U-11 | 2 | Foreign/namespaced name ⇒ `foreign-command` code. |
| TC-U-12 | 2 | Handler is a no-op for a resident-tool name (never runs when `MatchedPrimitive` set) — no double execution. |
| TC-U-13 | 3 | Drift scan **fails** on a fixture naming `` `not-a-tool` `` imperatively. |
| TC-U-14 | 3 | Drift scan passes when every referenced MCP/CLI/guide token resolves; external allowlist honored. |
| TC-U-15 | 4 | `tpl/workspace/*` text files have no UTF-8 BOM. |

## Integration / surface test cases

| ID | Story | Assertion |
|---|---|---|
| TC-I-01 | 2 | `tools/list` returns the same 27 resident tools before/after the change (reuse/extend `McpProfileGatingTests` budget). |
| TC-I-02 | 4 | `clio createw` produces a valid workspace; existing `CreateWorkspaceCommand.Tests` green. |

## E2E test cases (`clio.mcp.e2e/DurableInvocationToolE2ETests.cs`)

| ID | Assertion |
|---|---|
| TC-E-01 | Direct `tools/call get-fsm-mode` (non-destructive) executes, returns FSM mode + advisory note. |
| TC-E-02 | Direct `tools/call restart-by-environment-name` (destructive) ⇒ `confirmation-required`, no restart side effect. |
| TC-E-03 | Deprecated alias ⇒ resolves to canonical and executes. |
| TC-E-04 | Unknown name ⇒ structured did-you-mean + discovery hint. |
| TC-E-05 | `tools/list` count == 27 (surface unchanged). |

## Manual protocol reproduction (release gate)

Run in a fresh `clio createw` workspace, feed JSON-RPC (initialize → notifications/initialized → tools/call) to `clio mcp-server`; confirm TC-E-01..05 by hand. Optionally run TeamCity `Team_Atf_ClioMcpE2eTests` on the branch (skill `run-clio-mcp-e2e`) and compare to the trunk baseline — no new failures.

## Regression scope

- MCP: `McpProfileGatingTests`, `ToolContractGetToolTests`, `McpToolInvokerRegistryTests`, `McpGuidanceForcingTests`, `ClioRunDispatchTests` must stay green.
- Command: `CreateWorkspaceCommand.Tests`.
- Full unit suite (BindingsModule change).

## Traceability

FR-1→TC-U-06/TC-E-01 · FR-2→TC-U-07 · FR-3→TC-U-08/09/TC-E-02 · FR-4→TC-U-01..05/TC-E-03 · FR-5→TC-U-10/11/TC-E-04 · FR-6→TC-U-13/14 · FR-7→TC-U-15/TC-I-02 · NFR-1→TC-I-01/TC-E-05.
