# ADR — Durable MCP invocation (forgiving tool surface)

- **Status:** Proposed — **revised 2026-07-10 after Codex adversarial review (round 1)**. The round-1 NO-GO items are resolved in the decisions below; see "Review round 1" appendix for the finding→resolution map.
- **Date:** 2026-07-10
- **Jira:** [ENG-93370](https://creatio.atlassian.net/browse/ENG-93370)
- **Related:** [PRD](../prd/prd-mcp-durable-invocation.md); builds on [ADR — MCP lazy-schema tool surface](./adr-mcp-lazy-schema.md) (PR #743, ENG-90312)
- **Inputs:** 4 code-exploration passes + 2 independent Codex passes (design `task-mrezpvfr-gnnwa9`, adversarial review `task-mrf28k5r-naq2pv`) + live JSON-RPC protocol reproduction (2026-07-10).

## Context

PR #743 made the MCP server register only a **lazy profile** — ~27 resident tools in
`tools/list` (`McpCoreToolProfile.CoreToolTypes ∪ AlwaysOnLazyToolTypes`), everything
else reachable only through the `clio-run` / `clio-run-destructive` executors and
discovered via `get-tool-contract`. This context-economy design must be preserved.

The unintended side effect: **listing was reduced, and so was invocation.** Before
#743 every tool was advertised in `tools/list` and the host applied its normal
per-tool `Destructive` gating. After #743 a `tools/call` for a long-tail name misses
the SDK's `ToolCollection`; clio has no fallback, so `McpToolErrorFilter`
(`clio/BindingsModule.cs:806`) converts the SDK's downstream throw into a soft
`IsError` result with no recovery information. Static guidance that names long-tail
tools directly (clio's own `clio/tpl/workspace/AGENTS.md`, external skills, partner
repos — all written against the **pre-#743 surface**) therefore dead-ends.

The deeper gap: clio offers a backward-compat contract at the **CLI-flag** boundary
(kebab + hidden aliases, CLIO001) but **none at the MCP-tool boundary**.

**Design principle (from ENG-93370 owner):** restore the **pre-#743 invocation
contract** — every tool that was a first-class advertised tool before #743 stays
directly invocable — *reproducing*, not widening, the pre-#743 security posture. The
faithful reproduction is: the handler self-applies each tool's own `Destructive`
gate, because the host can no longer see it (the tool is unadvertised).

### Verified facts the design builds on (round-1 confirmed)

- **SDK:** `ModelContextProtocol` / `.AspNetCore` **1.4.0** (`Directory.Packages.props:21`,
  `clio/clio.csproj:162`). `WithCallToolHandler` exists (`ModelContextProtocol.xml:507`),
  the SDK checks `ToolCollection` first and invokes the handler **only on a miss**
  (`ModelContextProtocol.Core.xml:12011,12330`), and `AddCallToolFilter` forms an outer
  wrapper that **coexists** with the handler without shadowing it
  (`...Core.xml:12324`). Resident tools cannot be shadowed by the fallback.
- **Executor exists but its public contract is `clio-run`-shaped:** `IClioRunExecutor`
  / `ClioRunExecutor(IMcpToolInvokerRegistry)` (`Tools/ClioRunTool.cs:22,48`) resolves a
  name via `IMcpToolInvokerRegistry` over the **full** catalog and invokes the real
  tool — BUT `RunAsync` expects normalized clio-run target args and wraps a single
  complex param under its name (`ClioRunTool.cs:373`), overwrites `Params` /
  `MatchedPrimitive` without restoring them (`:122`), and drops request `_meta` /
  progress token / task metadata (`:377`). It is therefore **not** directly reusable
  with native call arguments — a native-shape entry point is required (see D2b).
- **Destructive gate:** the host gates on the `Destructive` flag read from `tools/list`.
  For the long tail that gate exists today only because the `clio-run*` wrappers are
  themselves `Destructive=true`. `IMcpToolInvokerRegistry.IsDestructive` fails
  **closed** (`McpToolInvokerRegistry.cs:171`). The set annotated
  `ReadOnly=false, Destructive=false` includes write-capable tools (`install-gate`,
  `reg-web-app`, `experimental`, `get-browser-session`, `install/update-toolkit`, …) —
  these ran without a destructive prompt **pre-#743 too** (their own annotation), so
  reproducing that is not a regression; but the annotations themselves must be audited
  for correctness (see D3).
- **`_meta` is not model-visible:** the executor's own comment says `_meta` is
  out-of-band, "never the content the model reads" (`ClioRunTool.cs:149`); SDK reserves
  `Result.Meta` for protocol metadata (`...Core.xml:7968`) and identifies `Content` as
  the model-facing channel (`...Core.xml:3773`). Advisory text must go in `Content`.
- **`RegisterMcpServer` is transport-neutral** (`BindingsModule.cs:789`): stdio
  (`:129`) and HTTP (`McpHttpServerCommand.cs:64`) both call it. A handler registered
  there ships on **both** transports.
- **No duplicate tool-name keys today:** all 150 `[McpServerTool]` names are unique
  under the case-insensitive comparer; the restart pair are two distinct names, not a
  dictionary collision. `McpToolInvokerRegistry.cs:108` silently keeps the first of any
  future duplicate. The registry is a **transient** DI service (`BindingsModule.cs:154`),
  so its constructor is not eagerly run by DI graph validation.
- **`McpToolErrorFilter` swallows downstream exceptions** into a text-only result
  (`McpToolErrorFilter.cs:29,96`): any structured error must be **returned**, not
  thrown, or it loses its code.
- **Codex correction:** `generate-source-code`→`restore-workspace` is **not** a clio
  rename — both verbs exist; that drift is inside a downstream consumer's own map.
  Jarvis `SKILL.md` call shell `clio <verb>`, not MCP — out of scope here.

## Decision

Add a **forgiving invocation layer** to the **stdio** MCP server: an unmatched-name
handler that resolves the requested name against a single compatibility catalog, then
either executes it (reproducing the pre-#743 per-tool gate) or returns a structured,
self-healing response. Progressive disclosure is rejected.

### D1 — Seam: `WithCallToolHandler`, **stdio call-site only**

Register `McpDurableCallToolHandler` via `WithCallToolHandler` **at the stdio
registration site** (next to `WithStdioServerTransport`, `BindingsModule.cs:129`), **not**
inside the transport-neutral `RegisterMcpServer` — so it does not silently ship on the
unreleased, multi-client `mcp-http` transport. It runs only on a `ToolCollection`
miss; coexists with the existing `AddCallToolFilter`. It obtains its collaborators from
`context.Services`.

### D2 — Resolution order

1. exact canonical tool (registry hit) — but a **catalog-declared alias takes
   precedence over a raw registry hit** for the same string (so deprecation metadata /
   adapters are never bypassed) →
2. compatibility alias → canonical →
3. recognized CLI-verb-but-not-MCP-tool → `cli-verb-not-mcp-tool` →
4. recognized foreign command → `foreign-command` →
5. fuzzy candidates (Levenshtein) → `unknown-tool` with did-you-mean →
6. otherwise `unknown-tool` + discovery hint.

Resolution returns a **discriminated result**: `Resolved(canonical, tool) |
Disabled(canonical) | Unknown | Foreign | Ambiguous(candidates)` — so
feature-disabled is distinguishable from unknown and maps to a stable code.

### D2b — Native-call executor contract (no arg double-wrap, context preserved)

Add a native-shape entry point (e.g. `IClioRunExecutor.InvokeResolvedAsync(McpServerTool
tool, RequestContext ctx, string canonicalName)`) that:

- maps the **native** `context.Params.Arguments` onto the target tool's parameter
  shape **without** re-wrapping (avoid `{"args":{"args":{…}}}` for single-complex-param
  tools);
- **preserves all inherited request fields** (`_meta`, progress token, task metadata)
  when retargeting;
- restores the original `Params` / `MatchedPrimitive` in a `finally`.

`clio-run` and the fallback handler share this one path (no duplicated dispatch, parity
with the SDK `WithTools` scan preserved via `IMcpToolInvokerRegistry`).

### D3 — Authorization: reproduce the pre-#743 per-tool gate

Eligible for forgiving execution = **every registry-resolvable tool** (the pre-#743
standalone set the executor already reaches; excludes the executor wrappers
themselves). The gate is the tool's **own `Destructive` flag**, self-enforced because
the host can no longer see it:

- **`Destructive == false`** → execute via D2b and return the result plus an advisory
  note **in `Content`** (prefer `clio-run <canonical>` / resident tools next time).
  This reproduces the host's pre-#743 non-prompt behavior for these tools.
- **`Destructive == true`** (or `IsDestructive` fail-closed unknown) → **do not
  execute.** Return a structured `confirmation-required` result with a ready-to-retry
  `clio-run-destructive` call shape. This reproduces the host's pre-#743 destructive
  prompt, which the host can no longer raise for an unadvertised tool.

**Annotation-correctness hardening (in scope):** audit the `Destructive` annotation of
high-impact write tools (`install-gate`, `reg-web-app`, `experimental`,
`install/update-toolkit`, `get-browser-session`, hotfix verbs) and correct any that
are genuinely privileged/destructive but mis-flagged `Destructive=false`, so the
reproduced gate is accurate. Add a **completeness test** that classifies every registry
tool as execute-silently vs confirmation-required, so the gate set is explicit and
reviewed on every change.

### D4 — `IMcpToolCompatibilityCatalog` (the durable core)

A single DI service (`clio/Command/McpServer/McpToolCompatibilityCatalog.cs`,
CLIO001-compliant) — the one source of truth for name evolution:

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

Rules: catalog-declared aliases win over raw registry hits (D2 step 1); aliases
case-insensitive, emitted canonically; **duplicate canonical or alias collision ⇒
fail startup + test**; an alias executes only when its arg contract is identical or an
explicit adapter exists; feature-disabled canonical resolves to `Disabled` (not
`Unknown`). Consumed by the handler, `clio-run`, `get-tool-contract` (project an
`alias` field), and the drift test.

**Seed & sequencing:** the restart pair — remove the duplicate
`restart-by-environmentName` `[McpServerTool]` method **atomically** with adding its
catalog alias (else D2's registry hit bypasses the catalog). There are **no duplicate
keys today**, so the registry fail-fast (below) will not break the current catalog; use
a **synthetic** duplicate fixture to test it, plus a production-uniqueness test.

**Eager validation:** make the compatibility catalog (and the registry's
collision-fail) run at **startup**, not on first resolution — via an eagerly
constructed immutable singleton or an `IValidateOnBuild`-style startup validator — and
test host construction.

### D5 — Structured errors & outcomes (must survive the filter)

All expected handler outcomes are **returned as `CallToolResult`** (never thrown), so
`McpToolErrorFilter` cannot flatten them to text and strip the code. Stable
machine-readable codes in `StructuredContent` (concise text mirror for old clients):
`unknown-tool`, `deprecated-tool-alias`, `cli-verb-not-mcp-tool`, `foreign-command`,
`confirmation-required`, `feature-disabled`, `ambiguous-alias`. Each carries canonical
name (when known), candidates, owner, destructiveness, ready-to-retry call shape, **and
a correlation ID** (core-rules requires one on every response,
`CoreRulesGuidanceResource.cs:35`). Any genuinely unexpected exception may still fall
through to the filter. Reuse the existing Levenshtein rankers.

### D6 — Drift guard + maintenance ownership (oracle rewritten)

New `clio.tests/Command/McpServer/WorkspaceTemplateGuidanceDriftTests.cs`
(`[Category("Unit")]`, `Module=McpServer`) scanning `clio/tpl/workspace/AGENTS.md`,
`ui-project*/AGENTS.md`, `McpServerInstructions.Text`, and enabled `GuidanceCatalog`
article bodies.

**Oracle (corrected — existence is not the test):** because the registry contains the
full long-tail, "name resolves in the registry" proves existence, not valid direct
invocation, and would leave the motivating regression green. The oracle instead
classifies each reference as **direct-imperative-invocation** vs **mention**, and:

- a direct imperative MCP-tool reference MUST name a **resident** tool **or** explicitly
  use the `clio-run` / `get-tool-contract` bridge;
- a direct imperative CLI reference MUST be a current `[Verb]` name/alias;
- a guide reference MUST resolve in `GuidanceCatalog`.

**Tokenization is specified, not ad-hoc:** prefer an explicit machine-readable
reference marker (or a sidecar manifest) over prose scraping; where prose is scanned,
define exact rules for code-fences, backticked paths/shell/examples, negated
instructions ("do NOT call `x`"), and a **deterministic feature baseline** for
feature-gated guides — with comprehensive fixtures (a bogus `` `not-a-tool` ``
imperative must fail; a hardcoded non-resident imperative without the bridge must
fail). Add `clio/tpl/**` to the documentation + MCP maintenance-target lists and
trigger-conditions in the root `AGENTS.md`.

### D7 — Template fix (this cycle)

Rewrite the `clio/tpl/workspace/AGENTS.md` deploy/FSM section to delegate to the live
channel (`get-guidance routing` / `get-tool-contract` / `clio-run`) instead of
hardcoding long-tail names; keep durable structural facts; assert the live MCP guidance
is authoritative over this static section. Strip the UTF-8 BOM from
`clio/tpl/workspace/*`. Must pass D6.

### D-REJECTED — Progressive disclosure (`tools/list_changed`)

Rejected: churns the prompt cache (undoing #743), the unreleased stateless-HTTP
transport cannot send unsolicited notifications, weak context inference, cross-session
leakage. A call that reaches the server is handled by D2/D3; default `tools/list` stays
static at 27.

## Consequences

**Positive:** the pre-#743 invocation contract is restored faithfully (same per-tool
gate), so static/legacy guidance stops dead-ending; renamed tools resolve; failures
self-correct; the MCP boundary gains the backward-compat guarantee the CLI boundary
has; the destructive gate is *reproduced* (not bypassed) and its annotations are
audited; drift can no longer ship uncaught; #743's context economy is untouched;
`mcp-http` is explicitly unaffected.

**Negative / limits:** listed-only hosts that never emit an unlisted `tools/call` are
unaffected (accepted — mitigated by the D3 `Content` note + core-rules guidance and the
canonical `clio-run` path); destructive long-tail requires the confirmation
round-trip (by design = pre-#743 prompt); a small always-present catalog + annotation
audit to maintain.

**Follow-ups (separate):** machine-readable CLI verb/alias contract + CLI
backward-compat for Jarvis/subprocess consumers; `clio createw --sync-instructions`
refresh; progressive-disclosure spike (own ADR) if ever revisited.

## Verification

Protocol reproduction (`clio mcp-server`, JSON-RPC): direct `get-fsm-mode`
(non-destructive) executes + advisory in `Content`; direct `restart-by-environment-name`
(destructive) ⇒ `confirmation-required`, no restart; a single-complex-param tool via
native args executes without double-wrap; deprecated alias ⇒ resolves via catalog;
feature-disabled ⇒ `feature-disabled` (≠ unknown); unknown ⇒ did-you-mean + hint;
`tools/list` still 27; `mcp-http` unchanged. Unit: `Module=McpServer` incl. drift test
(fails current template, passes fixed), catalog-collision (synthetic) + production
uniqueness, per-tool gate completeness, native-arg-shape matrix, context-preservation,
and **through-`RegisterMcpServer`/filter-pipeline** composition (not only isolated
handler substitutes). E2E in `clio.mcp.e2e`. See
[test plan](../test-plans/tp-mcp-durable-invocation.md).

## Review round 1 (Codex adversarial, `task-mrf28k5r-naq2pv`, 2026-07-10) — finding → resolution

- **B1** non-destructive silent-exec widens authz → **D3**: eligible set = pre-#743
  standalone tools; gate = per-tool `Destructive`; write/unknown ⇒ confirmation; +
  annotation audit + completeness test. (Reproduces pre-#743, not wider.)
- **B2** raw args double-wrap → **D2b** native-call contract.
- **B3** ships on `mcp-http` → **D1** stdio call-site only.
- **B4** drift test can't fail current template → **D6** oracle rewrite (resident-or-bridged; existence ≠ valid).
- **H5** context mutation/leak → **D2b** preserve + restore in `finally`.
- **H6** `_meta` not model-visible → **D3/D5** advisory in `Content`.
- **H7** restart alias bypasses catalog → **D4** catalog-precedence + atomic legacy-method removal.
- **H8** feature-disabled == unknown → **D2/D4** discriminated result + `feature-disabled` code.
- **H9** collision not caught at startup → **D4** eager validation.
- **M10** filter strips structured codes → **D5** return (never throw) expected outcomes.
- **M11** drift tokenization under-specified → **D6** explicit markers/grammar + feature baseline + fixtures.
- **M12** missing correlation-id / full-pipeline tests → **D5** correlation-id on all outcomes; verification through `RegisterMcpServer`.
- **L13** SDK seam / precedence / no current duplicate keys → confirmed; D1/D4 wording corrected accordingly.
