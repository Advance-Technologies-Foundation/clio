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
| TC-U-16 | 1 | Synthetic duplicate canonical/alias throws at **startup** (host construction), not first resolution. |
| TC-U-17 | 1 | Catalog alias takes precedence over a raw registry hit for the same name. |
| TC-U-18 | 1/2 | Feature-disabled target ⇒ discriminated `Disabled` ⇒ handler returns `feature-disabled` (≠ `unknown-tool`). |
| TC-U-19 | 2 | Native-arg-shape matrix (no-arg / scalar / multi-scalar / complex-record / malformed) executes without `{"args":{"args":…}}` double-wrap. |
| TC-U-20 | 2 | Progress token / `_meta` / task metadata survive fallback; `Params`/`MatchedPrimitive` restored in `finally`. |
| TC-U-21 | 2 | Advisory note is in `Content` (model-visible), not only `_meta`. |
| TC-U-22 | 2 | Per-tool gate **completeness**: every registry tool classified execute-silently vs confirmation-required; audited high-impact write tools flagged correctly. |
| TC-U-23 | 2 | Every handler outcome carries a correlation ID and survives the `McpToolErrorFilter` pipeline with its structured code intact (not flattened to text). |

## Integration / surface test cases

| ID | Story | Assertion |
|---|---|---|
| TC-I-01 | 2 | `tools/list` returns the same 27 resident tools before/after the change (reuse/extend `McpProfileGatingTests` budget). |
| TC-I-02 | 4 | `clio createw` produces a valid workspace; existing `CreateWorkspaceCommand.Tests` green. |
| TC-I-03 | 2 | `mcp-http` registration path is unchanged — the durable handler is registered only at the stdio call-site (assert HTTP builder does not carry it). |
| TC-I-04 | 2 | Handler behavior verified **through `RegisterMcpServer` + filter pipeline** (not an isolated substitute): execute, confirm, and error outcomes keep their codes end-to-end. |

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

FR-1→TC-U-06/TC-E-01 · FR-2→TC-U-07/21 · FR-3→TC-U-08/09/22/TC-E-02 · FR-4→TC-U-01..05/16..18/TC-E-03 · FR-5→TC-U-10/11/18/23/TC-E-04 · FR-6→TC-U-13/14 (resident-or-bridged oracle) · FR-7→TC-U-15/TC-I-02 · NFR-1→TC-I-01/TC-E-05 · NFR-2→TC-U-19/20/TC-I-04 · NFR (transport)→TC-I-03.

## Round-1 review deltas (Codex `task-mrf28k5r-naq2pv`)

Added/changed vs the first draft: per-tool `Destructive` gate + annotation audit + completeness test (B1 → TC-U-22); native-arg-shape dispatch (B2 → TC-U-19); stdio-only registration (B3 → TC-I-03); **drift oracle rewritten to resident-or-bridged** — existence is no longer the check (B4 → TC-U-13/14); context preservation (H5 → TC-U-20); advisory in `Content` (H6 → TC-U-21); startup-eager collision (H9 → TC-U-16); returned-not-thrown + correlation-id through the real filter (M10/M12 → TC-U-23/TC-I-04); discriminated feature-disabled (H8 → TC-U-18).
