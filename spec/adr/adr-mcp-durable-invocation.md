# ADR — Durable MCP invocation (forgiving tool surface)

- **Status:** Proposed (2026-07-10)
- **Date:** 2026-07-10
- **Jira:** [ENG-93370](https://creatio.atlassian.net/browse/ENG-93370)
- **Related:** [PRD](../prd/prd-mcp-durable-invocation.md); builds on [ADR — MCP lazy-schema tool surface](./adr-mcp-lazy-schema.md) (PR #743, ENG-90312)
- **Inputs:** 4 code-exploration passes + independent Codex design pass (`task-mrezpvfr-gnnwa9`) + live JSON-RPC protocol reproduction (2026-07-10).

## Context

PR #743 made the MCP server register only a **lazy profile** — ~27 resident tools in
`tools/list` (`McpCoreToolProfile.CoreToolTypes ∪ AlwaysOnLazyToolTypes`), everything
else reachable only through the `clio-run` / `clio-run-destructive` executors and
discovered via `get-tool-contract`. This is the right context-economy design and must
be preserved.

The unintended side effect: **listing was reduced, and so was invocation.** A
`tools/call` for a long-tail name misses the SDK's `ToolCollection`; today clio has no
fallback, so `McpToolErrorFilter` (`clio/BindingsModule.cs:806`) converts the SDK's
downstream throw into a soft `IsError` result with no recovery information. Static
guidance that names long-tail tools directly (clio's own
`clio/tpl/workspace/AGENTS.md`, external skills, partner repos) therefore dead-ends.

The deeper gap: clio offers a backward-compat contract at the **CLI-flag** boundary
(kebab + hidden aliases, CLIO001) but **none at the MCP-tool boundary**.

### Verified facts the design builds on

- **SDK:** `ModelContextProtocol` 1.4.0. `AddMcpServer` at `clio/BindingsModule.cs:801`;
  the SDK exposes `WithCallToolHandler` — a first-class handler invoked **after** a
  `ToolCollection` miss and **before** the SDK throws "Unknown tool." This is a
  cleaner seam than piggy-backing on the single existing `AddCallToolFilter`.
- **Reusable executor already exists:** `IClioRunExecutor` /
  `ClioRunExecutor(IMcpToolInvokerRegistry)` (`clio/Command/McpServer/Tools/ClioRunTool.cs:22,48`)
  resolves a tool NAME via `IMcpToolInvokerRegistry` over the **full** catalog
  (resident + ~42 long-tail; reflection flags hand-mirrored to the SDK `WithTools`
  scan), retargets `Params`+`MatchedPrimitive` and calls the real `tool.InvokeAsync`.
  It already owns `BuildChildParams`, `BuildSuggestions` (Levenshtein), and
  `toolRegistry.IsDestructive`. **We inject and reuse it — no extraction, no
  duplication.**
- **Destructive gate:** the host gates on the `Destructive` flag read from
  `tools/list`. Long-tail is gated only indirectly (the `clio-run*` wrappers are
  `Destructive=true`). Executing an unlisted destructive tool straight from a fallback
  handler removes that gate. `IMcpToolInvokerRegistry.IsDestructive` fails **closed**.
- **No name-alias layer:** `restart-by-environmentName` is the same tool registered
  twice; `McpToCliCommandMap` is help-only. The invoker registry **silently keeps the
  first** of any duplicate name (`McpToolInvokerRegistry.cs:112`).
- **No drift guard:** nothing checks the shipped template/guidance prose against the
  surface; `clio/tpl/**` is outside all maintenance targets.
- **Codex corrections accepted:** (1) `generate-source-code`→`restore-workspace` is
  **not** a clio rename — both verbs exist independently; that is drift inside
  Jarvis's own compat map. (2) Jarvis `SKILL.md` call shell `clio <verb>`, not MCP —
  out of scope here.

## Decision

Add a **forgiving invocation layer** to the stdio MCP server: an unmatched-name
handler that resolves the requested name against a single compatibility catalog, then
either executes it (non-destructive) or returns a structured, self-healing response
(destructive or unresolved). Progressive disclosure is explicitly rejected.

### D1 — Seam: `WithCallToolHandler`

Register a `McpDurableCallToolHandler` via `WithCallToolHandler` in
`BindingsModule.RegisterMcpServer` (alongside the existing `AddCallToolFilter`, which
stays). The handler runs only on a `ToolCollection` miss. It obtains
`IClioRunExecutor` + the compatibility catalog from `context.Services`.

*Alternative considered — extend `McpToolErrorFilter`:* feasible (it already sits
between primitive-match and the SDK throw) but conflates error-shaping with dispatch
and relies on catching the SDK's post-hoc throw. `WithCallToolHandler` is the
purpose-built seam and keeps concerns separated. **Rejected in favor of D1.**

### D2 — Resolution order (in the handler)

1. exact canonical tool (registry hit) →
2. compatibility alias → canonical →
3. recognized CLI-verb-but-not-MCP-tool → structured `cli-verb-not-mcp-tool` →
4. recognized foreign (e.g. namespaced) command → `foreign-command` →
5. fuzzy candidates (Levenshtein, reuse `ClioRunExecutor` helper) → `unknown-tool`
   with did-you-mean →
6. otherwise `unknown-tool` + discovery hint.

### D3 — Execution vs safeguard

- **Non-destructive resolved tool** → execute through `IClioRunExecutor.RunAsync`
  (same path as `clio-run`), return the result plus a `_meta` advisory note:
  *"executed directly; prefer `clio-run <canonical>` or resident tools next time."*
- **Destructive resolved tool** → **do not execute.** Return a structured
  `confirmation-required` result carrying a ready-to-retry `clio-run-destructive`
  call shape (`command=<canonical>`, `args=…`). Rationale: the host never saw the
  hidden tool's destructive annotation, so silent execution would bypass user
  approval. This is a deliberate, documented exception to "just execute it,"
  independently flagged by the seam analysis and Codex.

### D4 — `IMcpToolCompatibilityCatalog` (the durable core)

A single DI service (`clio/Command/McpServer/McpToolCompatibilityCatalog.cs`,
CLIO001-compliant interface + impl) as the one source of truth for name evolution:

```csharp
public sealed record McpToolCompatibilityEntry(
    string CanonicalName,
    IReadOnlyList<string> Aliases,
    CompatibilityKind Kind,          // Current | DeprecatedAlias | Removed
    string? DeprecatedSince,
    string? Replacement,
    SurfaceOwner Owner,              // Clio | Foreign
    ArgumentAdapter? Adapter);       // null ⇒ identical arg contract required
```

Rules: exact canonical always wins; aliases case-insensitive, emitted canonically;
**duplicate canonical or alias collision ⇒ fail startup + test**; an alias executes
only when its arg contract is identical or an explicit adapter exists;
feature-disabled canonical stays unreachable; never map arbitrary CLI verbs to MCP
tools. Consumed by: the handler (D1), `clio-run` (so both paths agree),
`get-tool-contract` (project an `alias` field), and the drift test (D6). Replaces the
scattered restart-alias duplicate and the help-only map as the source of truth. The
invoker registry's silent-first-duplicate (`:112`) becomes a fail-fast collision.

### D5 — Structured error codes

Stable, machine-readable codes in `StructuredContent` (with concise text for old
clients): `unknown-tool`, `deprecated-tool-alias`, `cli-verb-not-mcp-tool`,
`foreign-command`, `confirmation-required`, `feature-disabled`, `ambiguous-alias`.
Each carries canonical name (when known), candidates, owner, destructiveness, and a
ready-to-retry call shape.

### D6 — Drift guard + maintenance ownership

New `clio.tests/Command/McpServer/WorkspaceTemplateGuidanceDriftTests.cs`
(`[Category("Unit")]`, `Module=McpServer`): scan `clio/tpl/workspace/AGENTS.md`,
`ui-project*/AGENTS.md`, `McpServerInstructions.Text`, and enabled `GuidanceCatalog`
article bodies; classify referenced tokens and assert each resolves —
MCP name ⇒ canonical|alias; CLI name ⇒ current `[Verb]`|alias; guide name ⇒ catalog;
external tokens (`dotnet`, `npm`, skill names) ⇒ explicit allowlist; no ambiguous
alias. Add `clio/tpl/**` to the documentation + MCP maintenance-target lists and
trigger-conditions in the root `AGENTS.md`.

### D7 — Template fix (this cycle)

Rewrite the `clio/tpl/workspace/AGENTS.md` deploy/FSM section to delegate to the live
channel (`get-guidance routing` / `get-tool-contract` / `clio-run`) instead of
hardcoding long-tail names; keep durable structural facts; add a line asserting the
live MCP guidance is authoritative over this static section. Strip the UTF-8 BOM from
`clio/tpl/workspace/*` (verified harmless to Claude Code, corrected while touching).
Must pass D6.

### D-REJECTED — Progressive disclosure (`tools/list_changed`)

The SDK supports runtime `ToolCollection` mutation + `notifications/tools/list_changed`,
but we **reject** it here: it churns the prompt cache (undoing the #743 win), the
unreleased stateless-HTTP transport cannot send unsolicited notifications, context
inference from tool calls is weak, and per-session promoted state risks cross-session
leakage. A call that reaches the server is handled by D2/D3 instead; the default
`tools/list` stays static at 27.

## Consequences

**Positive:** static/legacy guidance that reaches the server stops dead-ending;
renamed tools keep resolving; failures become self-correcting; the MCP boundary gains
the backward-compat contract the CLI boundary already has; drift can no longer ship
uncaught; #743's context economy is untouched.

**Negative / limits:** listed-only hosts that never emit an unlisted `tools/call` are
unaffected (accepted — mitigated by the D3 note + core-rules guidance and the
canonical `clio-run` path); a small always-present catalog to maintain; destructive
long-tail still requires the confirmation round-trip (by design).

**Follow-ups (separate):** machine-readable CLI verb/alias contract + CLI
backward-compat for Jarvis and other subprocess consumers; a
`clio createw --sync-instructions` refresh path; progressive-disclosure spike (own
ADR) if ever revisited.

## Verification

Protocol reproduction (`clio mcp-server`, JSON-RPC): direct `get-fsm-mode` executes +
note; direct `restart-by-environment-name` ⇒ `confirmation-required`, no restart;
deprecated alias ⇒ resolves; unknown ⇒ did-you-mean + hint; `tools/list` still 27.
Unit: `Module=McpServer` filter incl. drift test + catalog-collision test; full suite
(touches `BindingsModule.cs`). E2E in `clio.mcp.e2e`. See
[test plan](../test-plans/tp-mcp-durable-invocation.md).
