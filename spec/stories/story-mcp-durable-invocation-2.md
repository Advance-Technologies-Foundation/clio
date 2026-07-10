# Story 2: Forgiving unmatched-name handler (execute / confirm / actionable error)

**Feature**: mcp-durable-invocation
**FR coverage**: FR-1, FR-2, FR-3, FR-5
**PRD**: [prd-mcp-durable-invocation.md](../prd/prd-mcp-durable-invocation.md)
**ADR**: [adr-mcp-durable-invocation.md](../adr/adr-mcp-durable-invocation.md) (D1, D2, D2b, D3, D5)
**Status**: ready-for-dev
**Size**: L
**Depends on**: story-mcp-durable-invocation-1
**Revised**: 2026-07-10 after Codex adversarial review (B1, B2, B3, H5, H6, M10, M12)

## As a
an AI agent following static guidance that names a pre-#743 tool directly

## I want
the server to resolve and (safely) run that name — reproducing the pre-#743 per-tool gate — instead of returning an opaque "Unknown tool"

## So that
guidance written against the pre-#743 surface self-heals rather than dead-ending, without weakening the destructive-confirmation the host used to apply

## Design
- New `clio/Command/McpServer/McpDurableCallToolHandler.cs`, registered via `WithCallToolHandler` **at the stdio call-site only** (`BindingsModule.cs:129`, next to `WithStdioServerTransport`) — **not** in the transport-neutral `RegisterMcpServer`, so it does not ship on `mcp-http` (B3).
- Runs only on `ToolCollection` miss (`context.MatchedPrimitive is null && name not empty`). Pulls `IClioRunExecutor` + `IMcpToolCompatibilityCatalog` from `context.Services`.
- Resolution order per ADR D2 (catalog-alias precedence; discriminated result).
- **Authorization gate = per-tool `Destructive` over the pre-#743 set** (B1):
  - `Destructive==false` → execute via the native-call path (D2b) and return the result + advisory note **in `Content`** (H6: not `_meta`) recommending `clio-run <canonical>` / resident tools.
  - `Destructive==true` or `IsDestructive` fail-closed unknown → **do not execute**; return `confirmation-required` with a ready-to-retry `clio-run-destructive` shape.
- **Annotation-correctness hardening (D3, B1):** audit the `Destructive` flag of high-impact write tools (`install-gate`, `reg-web-app`, `experimental`, `install/update-toolkit`, `get-browser-session`, hotfix verbs) and correct any genuinely privileged/destructive tool mis-flagged `Destructive=false`, so the reproduced gate is accurate. Add a **completeness test** classifying every registry tool as execute-silently vs confirmation-required (fails when a new tool lands unclassified).
- **Native-call executor path (D2b, B2/H5):** add `IClioRunExecutor.InvokeResolvedAsync(tool, ctx, canonical)` that maps native `context.Params.Arguments` onto the tool's parameter shape **without** re-wrapping single-complex-param tools, **preserves** `_meta`/progress-token/task metadata, and **restores** original `Params`/`MatchedPrimitive` in `finally`. `clio-run` and the handler share this path.
- **Errors are returned, never thrown** (M10): all expected outcomes are `CallToolResult` with a stable code in `StructuredContent` (`unknown-tool`, `deprecated-tool-alias`, `cli-verb-not-mcp-tool`, `foreign-command`, `confirmation-required`, `feature-disabled`, `ambiguous-alias`), did-you-mean (reuse Levenshtein), discovery hint, and a **correlation ID** (M12). Concise text mirror for old clients.

## Acceptance Criteria
- [ ] AC-01 — Direct `tools/call get-fsm-mode` (non-destructive) executes; advisory note is in `Content`.
- [ ] AC-02 — Direct `tools/call restart-by-environment-name` (destructive) ⇒ `confirmation-required` + retry shape; no restart.
- [ ] AC-03 — A single-complex-param tool invoked with native args executes with no `{"args":{"args":…}}` double-wrap (B2).
- [ ] AC-04 — Progress token / `_meta` / task metadata survive fallback execution; `Params`/`MatchedPrimitive` restored afterward (H5).
- [ ] AC-05 — Deprecated alias executes via canonical; feature-disabled ⇒ `feature-disabled`; unknown ⇒ did-you-mean; foreign ⇒ `foreign-command`.
- [ ] AC-06 — Every handler outcome carries a correlation ID and survives the `McpToolErrorFilter` pipeline with its code intact (M10/M12).
- [ ] AC-07 — Handler never runs for a `tools/list` hit; `tools/list` still 27; `mcp-http` registration unchanged (B3).
- [ ] AC-08 — Completeness test classifies every registry tool (execute-silently vs confirmation-required); audited high-impact write tools carry a correct `Destructive` flag.

## Tests
`clio.tests/Command/McpServer/McpDurableCallToolHandlerTests.cs` — execute+Content-note, destructive→confirm (substitute: zero exec), native-arg-shape matrix (no-arg/scalar/multi-scalar/complex-record/malformed), context preserve/restore, alias resolve, feature-disabled/unknown/foreign, correlation-id, and **through-`RegisterMcpServer`/filter** composition; `[Category("Unit")]`, `Module=McpServer`.
