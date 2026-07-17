# ADR: Response deadline for read-only (retry-safe) MCP tools

- **Status:** Proposed
- **Date:** 2026-07-17
- **Feature:** `read-only-mcp-response-deadline`
- **Jira:** [ENG-93373](https://creatio.atlassian.net/browse/ENG-93373) (sub-task of ENG-93367 "Analyze long app creating"; prior analysis of the same pain ENG-90634).
- **Related ADR:** [adr-create-app-section-response-deadline.md](adr-create-app-section-response-deadline.md) — the *write*-path deadline this ADR complements.
- **Related work:** ENG-93087 (progress streaming for long *write* operations) — different mechanism, complementary.

---

## Context

Session evidence (2026-07-10, Claude Opus 4.8 via Claude Desktop + clio MCP): the agent called
`list-pages {"package-name": "UsrBookLoan"}`. The call produced **no response for 452.9 s
(7 min 33 s)** and never timed out — the user had to cancel it manually. The stand was busy after a
section creation, but the MCP call gave zero feedback and no deadline.

`create-app-section` already solves the mirror problem for the **write** path
(adr-create-app-section-response-deadline): after ~150 s it returns
`{"error-class":"creatio-timeout","section-created":"in-progress","retry-guidance":…}` before the
client's hard request ceiling, and the backend work keeps running so a later poll observes it.

The gap is the **read** path. Read-only / retry-safe tools (`list-pages`, `list-app-sections`,
`get-page`, `list-apps`, `get-app-info`, and the rest) run their Creatio round-trip **synchronously
with no wall-clock bound**, so a stalled stand hangs the tool indefinitely.

### Constraints

- The clio MCP server (`clio mcp-server`) is a **long-lived** single-session stdio process; the
  mcp-http host reuses the same tool surface.
- Read-only tools have **heterogeneous return shapes**: typed records (`PageListResponse`,
  `ApplicationListResponse`, `ComponentInfoResponse`, the DataForge family, …), the generic
  `CommandExecutionResult`, and even `IReadOnlyList<T>` (which has **no error field at all**). A
  per-tool `error-class`/`retry-guidance` envelope cannot be added uniformly — some shapes cannot
  carry it.
- All MCP call-tool invocations already flow through one request filter,
  `McpToolErrorFilter.HandleCallToolErrors`, registered once in `BindingsModule.RegisterMcpServer`
  for **both** stdio and mcp-http. It already flattens tool exceptions into a `CallToolResult`.
- CLI/MCP conventions: kebab-case, `[Category("Unit")]`, AAA + `because` + `[Description]`, no new
  `CLIO*` warnings, DI-first, mandatory `clio.mcp.e2e` coverage + docs/MCP review.

---

## Decision

Add a **wall-clock response deadline at the call-tool filter layer**, applied only to
**retry-safe** tools, and return a **shape-agnostic structured timeout `CallToolResult`** on
expiry. Because the deadline lives in the pipeline (not in each tool), it covers **every** read-only
tool regardless of return shape with a single mechanism, and never touches the write tools that own
their own timeout semantics.

### Gate: which tools get the read deadline

A tool is **retry-safe** — and therefore gets the read deadline — when:

```
!DestructiveHint && (ReadOnlyHint || toolName == "get-page")
```

- Covers all five named tools. Four (`list-apps`, `list-pages`, `get-app-info`,
  `list-app-sections`) are `ReadOnly=true`. **`get-page` is `ReadOnly=false`** (it writes local
  `.clio-pages` files) so it is admitted by an explicit one-tool allowlist: it reads from Creatio and a
  retry re-reads Creatio and overwrites the local files, so it is retry-safe despite `ReadOnly=false`.
- Excludes every destructive write (`create-app-section`, `create-app`, `update-*`, `delete-*`,
  `deploy-*`, `restart-*`, `compile-creatio`, …). Those are `Destructive=true` and keep their own
  contract (e.g. `create-app-section`'s `in-progress` / "do NOT retry" envelope). Applying a
  "safe to retry" timeout to them would be **wrong** (a retry could duplicate a section).
- **The `Idempotent` hint is deliberately NOT part of the predicate.** Several non-read tools are
  `ReadOnly=false, Destructive=false, Idempotent=true` — but they are SERVER writes (`install-gate`
  installs a package, `generate-source-code`, `add-package-dependency`, `start-creatio`, `build-theme`).
  `Idempotent` guarantees only that *sequential* re-runs are equivalent, NOT that a retry issued while
  the abandoned first call is still mutating the server is safe. Bounding those with "safe to retry"
  guidance would invite a concurrent duplicate write (e.g. two overlapping `install-gate` deploys on one
  env). So the deadline covers **reads only** (plus the `get-page` local-write read), never server
  writes. An earlier draft used `ReadOnly || Idempotent` and was corrected in review for exactly this.
- Non-destructive non-idempotent writes (`reg-web-app`, `create-ui-project`, …) are excluded as well
  (not `ReadOnly`, not `get-page`).

The single predicate `McpReadDeadlineGate.IsRetrySafe(toolName, readOnly, destructive)` is the
authority, used by all dispatch paths below so the classification can never drift.

### Mechanism

1. **`McpReadResponseDeadline` (new helper).** Races the wrapped work against a wall-clock deadline.
   - Default **120 s**, overridable via a **new, dedicated** env var
     `CLIO_MCP_READ_DEADLINE_SECONDS` (invariant culture, `0 < n ≤ 600`; invalid/out-of-range →
     120 s). Kept separate from the write path's `CLIO_MCP_RESPONSE_DEADLINE_SECONDS` (150 s) so an
     operator can tune read latency independently of the write ceiling.
   - On completion within the deadline: returns the tool's real `CallToolResult` unchanged.
   - On expiry: returns a structured timeout `CallToolResult` (`IsError = true`) whose
     `StructuredContent` carries a machine-readable `error-class: creatio-timeout`,
     `read-response-timed-out: true`, the deadline seconds, and `retry-guidance`; a concise text
     mirror serves older clients. The wrapped work is **abandoned** (its linked token is cancelled
     to nudge cooperative tools; its eventual result/exception is observed so it can never surface
     as an `UnobservedTaskException`). Abandoning a read is safe — the caller simply retries.
2. **Filter wiring (matched tools).** In `McpToolErrorFilter.HandleCallToolErrors`, read the matched
   tool's annotations from `context.MatchedPrimitive` (`ProtocolTool.Annotations`). If retry-safe,
   run `next` under the deadline; otherwise call `next` unchanged (today's behavior). This covers
   every advertised (resident) retry-safe tool — all five named tools included.
3. **clio-run wiring (the primary long-tail vector).** Non-resident tools are invoked via `clio-run`
   per core-rules, so `ClioRunExecutor.RunAsync` bounds its inner dispatch when
   `IMcpToolInvokerRegistry.IsRetrySafe(name)` holds. `clio-run` / `clio-run-destructive` are themselves
   `Destructive=true`, so the outer call is never filter-wrapped — no double-wrapping.
4. **Durable-handler wiring (raw-name fallback).** `McpDurableCallToolHandler` dispatches unadvertised
   tool names invoked by RAW name (not via clio-run). It bounds `executor.InvokeResolvedAsync` on the
   same `IMcpToolInvokerRegistry.IsRetrySafe(name)` (new; mirrors the existing `_destructive` map built
   from `ProtocolTool.Annotations`). Destructive unmatched tools already return `confirmation-required`
   without executing, so they are untouched.

On the clio-run / durable paths the wrapped `DispatchAsync` retargets the shared `RequestContext` and
restores it in a `finally`; when abandoned on deadline that restore runs late on a pool thread. This is
benign under the single-session, fresh-per-request-context model (no live request reads the retargeted
context after the bounded call returns) — the same accepted detach trade-off as the write path.

### Reuse of the `creatio-timeout` error-class

The value `creatio-timeout` is the same wire token already used by the write path
(`ApplicationSectionCreateFailureClass.CreatioTimeout`), so existing client guidance ("on
`error-class: creatio-timeout` …") applies unchanged. The read envelope is distinguished from the
write one by `read-response-timed-out: true` and by the absence of `section-created`.

---

## Alternatives considered

1. **Per-tool deadline + `error-class`/`retry-guidance` on each response record.** Rejected: ~40
   tools with heterogeneous shapes; `IReadOnlyList<T>` returns cannot carry an envelope at all;
   massive churn and guaranteed drift. The filter layer is the only shape-agnostic seam.
2. **Blanket deadline for *all* tools (no gate).** Rejected: would return "safe to retry" for
   destructive/non-idempotent writes, risking duplicate sections/packages — the exact failure mode
   `create-app-section`'s `in-progress` contract exists to prevent.
3. **Strict `ReadOnlyHint` gate.** Rejected: excludes the named `get-page` (ReadOnly=false because
   it writes local files). `ReadOnly || Idempotent` is the correct "retry-safe" predicate.
4. **Reuse the write env var `CLIO_MCP_RESPONSE_DEADLINE_SECONDS`.** Rejected per the ticket's
   explicit ask for an independently tunable read deadline; a separate knob lets reads use a
   tighter budget than the 150 s write ceiling.
5. **Cancel (not abandon) the work on deadline.** The underlying read services are synchronous and
   do not observe the token, so cancellation cannot truly stop them; abandon-and-observe (with a
   best-effort token cancel) is the only realistic behavior and is safe for reads.

---

## Consequences

- **Positive:** no read-only MCP command can block indefinitely; worst case is
  `deadline + structured error`. The fix is one pipeline change covering every retry-safe tool on
  both transports, plus the long-tail dispatch path.
- **Contract:** additive. Success paths are byte-for-byte unchanged; only a *new* timeout outcome
  appears, and only for retry-safe tools that exceed the deadline.
- **Negative / risks:** an abandoned read may keep running server-side until it completes (result
  discarded) — bounded by the single-session, sequential-client execution model already documented
  for the write path. Mis-annotated tools (a destructive tool marked non-destructive) would be
  mis-gated — mitigated by the annotation-audit test and the existing duplicate-name/annotation
  guards.
- **Tuning:** 120 s default (below common client ceilings); env-var override for other ceilings.

---

## Scope / work items

1. `McpReadDeadlineGate.IsRetrySafe` predicate (shared authority).
2. `McpReadResponseDeadline` helper: race work vs wall-clock deadline; structured timeout
   `CallToolResult`; abandon-and-observe; env-var knob. Unit tests: completes-before → passthrough;
   exceeds → structured `creatio-timeout`; invalid/over-range env → 120 s default.
3. `McpToolErrorFilter.HandleCallToolErrors`: apply the deadline to matched retry-safe tools.
4. `IMcpToolInvokerRegistry.IsRetrySafe` + `McpDurableCallToolHandler`: same deadline for unmatched
   non-destructive retry-safe tools.
5. Prompts/resources: note the read `creatio-timeout` outcome where the write one is already
   documented.
6. Docs: MCP surface notes; no CLI verb changed, so no `help/en/*.txt` change (state
   "MCP reviewed").
7. Tests: unit (`clio.tests/Command/McpServer`) + **mandatory** `clio.mcp.e2e` — a read tool driven
   past a tiny deadline returns the structured `creatio-timeout` envelope; a fast read is unchanged.
8. `ClioRing` compatibility: `list-apps` is invoked by Ring via `clio-run`; the change is additive
   (a new timeout outcome only on expiry). Run the Ring compatibility gate.

---

## Open questions

- **Q1 — long-tail coverage depth.** The durable-handler path covers unmatched retry-safe tools;
  confirm no resident tool relies on being *un*bounded (none identified — all resident retry-safe
  reads are safe to abandon).
- **Q2 — exact default.** 120 s chosen from the ticket's "120–150 s"; the env var makes it
  authoritative per client ceiling.
