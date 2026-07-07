# ADR: Response-deadline + background continuation for long-running MCP application tools

- **Status:** Proposed
- **Date:** 2026-06-24
- **Feature:** `create-app-section-response-deadline`
- **Jira:** [ENG-91316](https://creatio.atlassian.net/browse/ENG-91316) (profile/fix create-app-section latency; sub-task of ENG-91312). Surfaced as the root cause of [ENG-92144](https://creatio.atlassian.net/browse/ENG-92144) (never-green UC test `create-tasks-section`). Builds on [ENG-91274](https://creatio.atlassian.net/browse/ENG-91274) (heartbeat) and [ENG-91540](https://creatio.atlassian.net/browse/ENG-91540) (insert budget).
- **PRD source:** the ENG-91316 description plus the empirical investigation recorded in ENG-92144 (live-stand repro, 2026-06-23) are treated as the PRD.
- **Related ADR:** [adr-mcp-progress-heartbeat.md](adr-mcp-progress-heartbeat.md) — the heartbeat this ADR extends.

---

## Context

`create-app-section` is the primary MCP path for building a Creatio application section
(entity + List page + Form page + mobile pages + navigation). Its handler
(`clio/Command/McpServer/Tools/ApplicationTool.cs:211` `ApplicationSectionCreate`) runs the
backend work **synchronously** through `McpProgressHeartbeat.RunWithProgressAsync(... applicationSectionCreateService.CreateSection(...) ...)`
and **awaits full completion** before returning its structured readback.

### What the regression-test evidence shows (ENG-92144, confirmed on a live stand)

The UC regression test `create-tasks-section` (TeamCity `Team_UserCustomization_Custom_AdacAgentsTestsUc`)
has **never** passed (0/7). Root cause, confirmed empirically:

- **clio `create-app-section` works and registers pages in one shot** when driven directly
  against a warm stand (verified: returned `UsrTask_FormPage` + `UsrTask_ListPage` (+mobile+Detail),
  section visible in `list-app-sections`). This is **not** a capability gap.
- In the failing CI runs the agent calls `create-app-section` and **every attempt returns
  `MCP error -32001: Request timed out`** (sonnet/06-16/MSSQL ×3; gpt-5.4-mini/06-19/PostgreSQL ×4).
  `list-app-sections` after each timeout shows the section was **not** created. The agent then
  falls back to `create-page`/`sync-pages`, which create page schemas but do **not** register
  them as the section's pages → the Application-Explorer Pages gallery never shows the Task pages
  → the design-time Playwright check fails.
- The clio binary in those builds was post-ENG-91540 (insert budget = 90 s); the 90 s insert
  budget did **not** prevent the `-32001`.

### Root cause (confirmed by code reading + the heartbeat ADR's own limitation)

1. `-32001 Request timed out` is the **MCP client's hard per-request ceiling** (GitHub Copilot
   CLI: ~180 s). Per the heartbeat ADR, `notifications/progress` resets a client's *inactivity*
   timer — but Copilot CLI's ceiling is a **fixed wall-clock cap that progress does NOT reset**.
   So the ENG-91274 heartbeat keeps Claude Code alive but cannot save a Copilot-CLI call that
   exceeds ~180 s.
2. Server-side section generation is genuinely slow on cold / large stands. ENG-91316 measured a
   worst-case `create-app-section` of **4:47 (287 s)** — well past the 180 s ceiling. On a
   freshly-deployed CI stand even the small UC app exceeds it; on a warm stand the same call
   returns in seconds.
3. ENG-91540 bounded the **insert HTTP call** to 90 s and returns a structured
   `creatio-timeout / section-created: unknown / "verify with list-app-sections"` envelope when
   the insert itself times out. But the **whole MCP call** (preparation reads → 90 s insert →
   success-path readback with `Timeout.Infinite` + a 15×2 s poll loop with **no cumulative
   deadline**, residual ENG-91316) has no wall-clock bound, so on a slow stand it rides past
   180 s and the client kills it with `-32001` **before** clio can return its envelope. The
   agent receives a raw transport error (not the actionable envelope) and falls back.

### Constraints

- The clio MCP server (`clio mcp-server`) is a **long-lived process** for the duration of an
  agent session (ADAC launches it once per run), so work started during one tool call can
  continue across the response and be observed by a later `list-app-sections`/`get-app-info`.
- Application tools are plain `[McpServerToolType]` classes (not `BaseTool<T>`), so they do not
  run under the global `McpToolExecutionLock`.
- The structured `creatio-timeout` envelope already exists (ENG-91540). The gap is **when** it
  is returned, not its shape.
- CLI/MCP conventions: kebab-case names, `[Category("Unit")]`, AAA + `because` + `[Description]`,
  no new `CLIO*` warnings, DI-first, mandatory `clio.mcp.e2e` coverage + docs/MCP review.

---

## Decision

Add a **wall-clock response deadline** to the long-running application-create MCP tools and let
the underlying work **continue in the background** past that deadline. When the work finishes
within the deadline the contract is unchanged (full synchronous readback). When it does not, the
tool returns the **existing** `creatio-timeout / section-created: in-progress` envelope **before
the client ceiling**, instructs the agent to poll `list-app-sections` / `get-app-info` (and to NOT
retry `create-app-section`), and the backend section generation keeps running on the long-lived
server so the poll eventually succeeds.

This completes the ENG-91274 → ENG-91540 line: heartbeat keeps inactivity-timeout clients alive;
the response deadline makes the tool correct for **hard-ceiling** clients (Copilot CLI) too.

### Mechanism

1. **Response deadline (new).** Introduce a bounded wrapper around the synchronous `work` —
   `McpProgressHeartbeat.RunWithProgressAndDeadlineAsync(work, deadline, …)` (or a sibling helper)
   — that races `work` against a wall-clock `deadline`. Default deadline **150 s** (safely below
   Copilot CLI's ~180 s), overridable via a new env var (e.g. `CLIO_MCP_RESPONSE_DEADLINE_SECONDS`)
   for clients with a different ceiling. The heartbeat continues to fire at 15 s for clients that
   honour it.
2. **Background continuation (new).** The deadline does **not** cancel `work`. When the deadline
   fires first, `work` keeps running on its task on the long-lived server; the section completes
   server-side and becomes visible to subsequent read tools. The background task's own
   readback/return value is discarded (no client is waiting) — its only purpose is to let the
   already-issued backend creation finish.
   - Consequence for `ApplicationSectionCreateService`: on the deadline path the insert must run
     with a **generous budget** (not the 90 s abort) so the section actually commits server-side.
     The 90 s insert budget remains the default for the **non-deadline** (synchronous-return) call
     shape; the deadline path uses a longer/uncapped server budget because no client is blocked.
3. **Envelope reuse (minimal contract change).** On the deadline path the tool returns the existing
   `ApplicationSectionContextResponse` error envelope with `error-class = creatio-timeout` and a new
   `section-created` value **`in-progress`** (today the enum is `true | false | unknown`; `unknown`
   means "verification failed", which is semantically different from "still creating"). The
   `retry-guidance` is sharpened to: *"Section creation is still running server-side. Do NOT retry
   create-app-section (it would create a duplicate). Wait, then poll list-app-sections / get-app-info
   until the section and its `UsrX_ListPage`/`UsrX_FormPage` appear."*
4. **Scope of tools.** Apply the deadline to the create family that generates pages and can exceed
   the ceiling: `create-app-section` (primary), and review `create-app` for the same treatment.
   `sync-schemas` latency (the other half of ENG-91316) is **out of scope** here and tracked
   separately.

### Why background continuation is safe

- The section is keyed by a clio-generated `Id` per call; a duplicate would only arise from a
  retry, which the guidance explicitly forbids and which the agent is told to replace with polling.
- Read tools (`list-app-sections`, `get-app-info`) are the existing, idempotent verification path;
  no new operation-handle registry or cross-call state store is introduced.
- The long-lived-process assumption is documented; if the server is restarted mid-flight the worst
  case is the same as today (section may or may not exist) and the agent's poll detects the truth.
- On the MCP/background path each success-path readback HTTP call is bounded by a finite per-request
  budget (`ApplicationSectionCreateTool.BackgroundReadbackTimeoutMs`, 30 s — mirrors the recovery-readback
  budget), so a wedged readback (one Creatio accepts but never answers) cannot park a thread-pool worker
  and HTTP connection for the life of the long-lived process — closing the residual ENG-91316 hang risk
  on the detached continuation. The synchronous CLI path keeps the patient `Timeout.Infinite` readback. A
  cumulative cap across the 15-attempt poll loop stays intentionally absent: each call is now individually
  bounded and the agent polls `list-app-sections` regardless, so abandoning a hung readback is low-harm.

---

## Alternatives considered

1. **Heartbeat only (status quo, ENG-91274).** Rejected: progress notifications do not reset
   Copilot CLI's hard ceiling, so it cannot fix the UC tests (the very evidence here).
2. **Longer insert budget / `CLIO_CREATE_SECTION_TIMEOUT_SECONDS`.** Rejected: raising the budget
   makes the synchronous call wait *longer*, hitting `-32001` sooner, not later — the client ceiling
   is the wall, not clio's budget.
3. **CAADT stand-warmup (throwaway create-app-section in the precondition).** Cheaper and not in
   clio, but fragile (depends on warmup hiding cold-start latency), test-harness-specific, and does
   not fix `create-app-section` for real users on cold/large stands. Could be a complementary
   stop-gap, not the systemic fix.
4. **Platform ticket to speed up server-side ApplicationSection generation.** Correct long-term but
   out of clio's control and slow; clio must still behave correctly under the existing latency.
   File alongside, do not block on.
5. **Switch the UC runner off Copilot CLI to a heartbeat-respecting client.** Out of clio; narrows
   the test harness rather than fixing the tool; leaves Copilot-CLI users broken.
6. **Operation-handle + explicit `get-operation-status` tool.** More general but heavier: new tool,
   new state registry, larger contract. Rejected for now — polling existing read tools achieves the
   same outcome with no new surface.

---

## Consequences

- **Positive:** `create-app-section` becomes correct on hard-ceiling clients; the never-green
  `create-tasks-section` UC test can pass (agent gets an actionable envelope + poll path instead of
  `-32001` → no destructive fallback). The fix generalises to any slow create on any client.
- **Contract:** additive — a new `section-created: in-progress` value and sharpened guidance. The
  success path is byte-for-byte unchanged. Tool description, prompt guidance, and the
  `ApplicationToolResponses` mapping must be updated together (MCP-review rule).
- **Negative / risks:** background work whose result is discarded (acceptable — read tools are the
  source of truth); reliance on the long-lived server process (documented); the agent must actually
  follow the poll guidance (covered by prompt + tool-description updates and an e2e assertion).
- **Tuning:** the 150 s default trades a little slack below 180 s; env-var override covers clients
  with other ceilings.

---

## Scope / work items (for stories)

1. `McpProgressHeartbeat`: add a deadline-aware run variant (race work vs wall-clock; do not cancel
   work on deadline). Unit tests for: completes-before-deadline → result; exceeds-deadline →
   deadline signal while work continues; no-progress-token no-op preserved.
2. `ApplicationSectionCreateService`: support a "deadline / background" call shape that uses a
   generous server-side insert budget (no 90 s abort) so the section commits; keep the existing
   synchronous shape for non-MCP/CLI callers.
3. `ApplicationSectionCreateTool`: wire the response deadline (default 150 s, env override); on
   deadline return the `creatio-timeout / in-progress` envelope; update `[Description]`.
4. `ApplicationToolResponses` / mapper: add `in-progress` `section-created` state + sharpened
   `retry-guidance`.
5. Prompts/resources (`Prompts/ApplicationPrompt.cs`, `Resources/*GuidanceResource.cs`): instruct
   "on create-app-section timeout → poll list-app-sections/get-app-info, do NOT retry, do NOT fall
   back to create-page".
6. Docs: `help/en/create-app-section.txt`, `docs/commands/create-app-section.md`, `Commands.md`.
7. Tests: unit (`clio.tests/Command/McpServer`) + **mandatory** `clio.mcp.e2e` coverage that a slow
   create returns the in-progress envelope before the deadline and that a poll observes the section.
8. Review `create-app` for the same treatment; file a platform ticket for server-side latency with
   the ENG-91316 per-stage evidence; `sync-schemas` latency tracked separately.

---

## Open questions

- **Q1 — does Creatio's `ApplicationSection` InsertQuery commit the section only after full page
  generation, or incrementally?** If the HTTP POST blocks until generation finishes, background
  continuation requires keeping that POST alive (generous budget) — confirmed as the plan. If the
  insert returns early and generation is async server-side, the readback poll alone suffices. Needs
  one instrumented cold-stand run (ENG-91316 item 1) to confirm; the design works either way but the
  insert-budget detail depends on it.
- **Q2 — exact Copilot CLI ceiling.** Assumed ~180 s from ENG-91540; the 150 s default leaves margin.
  Confirm and make the env-var default authoritative.
- **Q3 — should the deadline live in a shared MCP base (so every long create inherits it)** rather
  than per-tool? Decide during story 1/3.
