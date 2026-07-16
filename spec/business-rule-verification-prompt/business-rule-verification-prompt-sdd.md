# SDD — Ask before verifying a created business rule (ENG-89971)

- **Jira:** [ENG-89971](https://creatio.atlassian.net/browse/ENG-89971) — *"The agent should ask if verification is needed"*
- **Epic:** ENG-91854 *Analytical widgets creation using coding agent* · Component: *pixel ninjas* · Sprint PN-45
- **Type:** Improvement (MCP guidance) · **Author:** Vitalii Ryzhenko
- **Status:** Implemented — guidance + unit test edited; affected McpServer fixtures green (see §7)

---

## 1. Problem

After the coding agent (Claude Code driving clio's MCP business-rule tools) successfully creates a
business rule, it **sometimes automatically opens a browser** (Playwright / chrome MCP) to confirm the
rule exists. This is time-consuming and often unwanted.

**Expected:** after creating a business rule the agent should **ask** whether verification should be
automatic (agent-driven) or manual (user-driven). If manual, the agent gives a short summary of the
created rule plus clear checking steps. The answer must be **remembered** so the user is not asked
every time.

## 2. Root cause (where the behavior comes from)

clio never opens a browser itself. The auto-verification is **agent behavior**, prompted by clio's
guidance text:

- [`BusinessRulesGuidanceResource.cs`](../../clio/Command/McpServer/Resources/BusinessRulesGuidanceResource.cs)
  — **Workflow step 8:** *"Verify by checking the entity or page on the environment."* (unconditional).
- [`AgentExecutionGuidanceResource.cs`](../../clio/Command/McpServer/Resources/AgentExecutionGuidanceResource.cs)
  — evidence buckets `implemented` / `machineChecked` / `manualCheckPending`; *"Never claim UI acceptance
  is verified unless the corresponding evidence was returned by MCP tools."* (already supports a
  manual-pending outcome — **no change needed**).

The fix therefore lives in **clio's guidance layer**, not in tool execution.

## 3. Decisions (confirmed)

| # | Decision | Rationale |
|---|----------|-----------|
| D1 | **Persistence = agent memory (guidance-only).** clio instructs the agent to ask once and store the choice in *its own* memory (Claude Code memory / project `AGENTS.md`). No new clio state, no new MCP tool. | clio is a stateless guidance provider with no session/user context; the agent is the stateful party. Matches the "targeted edit, no refactor" preference. |
| D2 | **Scope = business rules only.** Change only the business-rule creation flow. | Matches the literal ticket. Widget/dashboard/page creation (sibling tickets) is explicitly deferred — see §8. |
| D3 | **No tool-contract change.** `create-entity-business-rules` / `create-page-business-rules` args, result shape (`BusinessRuleBatchResponse`), destructive flags, discovery output stay identical. | Keeps the change out of the ClioRing compatibility gate and out of E2E tool-shape churn; the summary/steps are content the agent can build from data it already has (the rules it authored + `ruleName` already returned per rule). |

## 4. Design

Single behavioral change, delivered as guidance text in
`BusinessRulesGuidanceResource.Guide`.

### 4.1 New section — "Post-creation verification (ASK; do not auto-open a browser)"

Inserted after the `Workflow` block. Contract for the agent:

1. After `create-entity-business-rules` / `create-page-business-rules` returns, **do not
   automatically open a browser / Playwright / navigate the app** to confirm the rule.
2. **Read the saved preference first.** Look in the agent's own persistent memory for a stored
   *business-rule verification preference* (`auto` | `manual`).
   - If present → follow it silently; **do not ask again**.
   - If absent → **ask the user exactly once**: *"How should I verify the business rule(s) —
     automatically (I open the app and check), or will you verify manually?"*
3. **Persist the answer** to the agent's memory keyed to *business-rule verification* so future
   creations don't re-prompt. If the memory write is unavailable, honor the choice for the current
   session and continue (graceful degrade — no hard failure).
4. **AUTO** → run browser/env verification **only when verification tools/environment are actually
   available** (`get-browser-session` + open app). If unavailable, fall back to manual and say so.
5. **MANUAL** → do **not** open a browser. Emit:
   - a **concise summary** per created rule — caption, target entity/page, condition, action(s), and
     the generated `ruleName` from the tool response;
   - **numbered manual steps** — open `<entity/page>` in `<environment>`, trigger the condition,
     confirm the action fires.
   - Mark the outcome `manualCheckPending` (per `agent-execution` evidence buckets).
6. **Partial batch:** summarize/verify only the rules that were `created`; failed rules keep their
   per-rule `error` from the response. One preference prompt covers the whole batch.

### 4.2 Workflow step edit

Replace the current unconditional step 8:

> `8. Verify by checking the entity or page on the environment.`

with a pointer into the new section, e.g.:

> `8. Post-creation verification: follow "Post-creation verification" below — read the saved`
> `   preference or ask auto/manual; never auto-open a browser before that.`

### 4.3 "Common mistakes to avoid" addition

> `- Do NOT auto-open a browser to verify a created business rule before reading the saved`
> `  verification preference or asking the user auto-vs-manual (ENG-89971).`

### 4.4 What does NOT change

- `AgentExecutionGuidanceResource` — already models `manualCheckPending`; referenced, not edited.
- Tool classes, `BusinessRuleBatchResponse`, action contracts — unchanged.
- `RoutingGuidanceResource` — `business-rules` already routed; unchanged.
- Tool `[Description]` trigger lines — already say *"Read get-guidance business-rules … before
  calling"*; the guide is the single source of truth, so no per-tool description edit (confirm during
  implementation; optional one-line nudge only if reviewers want it).

## 5. Affected files

| File | Change |
|------|--------|
| `clio/Command/McpServer/Resources/BusinessRulesGuidanceResource.cs` | Add §4.1 section, edit workflow step (§4.2), add mistake line (§4.3). |
| `clio.tests/Command/McpServer/McpGuidanceResourceTests.cs` | Extend `BusinessRulesGuidanceResource_Should_Return_Canonical_Business_Rules_Guide` with assertions for the new content. |

No other production files.

## 6. Test plan

Guidance-only ⇒ unit-level assertions on the article text (matches the existing pattern in
`McpGuidanceResourceTests`). All tests: AAA, `[Description]`, `because:` on every assertion.

**TC-U-1** — guide contains the ask/auto/manual prompt intent (e.g. `Contain("automatically")` +
`Contain("manually")`).
**TC-U-2** — guide forbids auto-opening a browser before asking (`Contain("do not automatically open a
browser")` / mistake line).
**TC-U-3** — guide instructs persisting the preference in memory and not re-asking
(`Contain("verification preference")`, `Contain("do not ask again")`).
**TC-U-4** — manual path requires a summary + steps (`Contain("summary")`, `Contain("steps")`).
**TC-U-5** — auto path is conditional on tooling availability.

Regression run (smart-testing policy — `McpServer` module only):

```
dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=McpServer" --no-build
```

**No new E2E** — there is no tool-contract change, so `EntityBusinessRuleToolE2ETests` /
`PageBusinessRuleToolE2ETests` stay valid as-is (they exercise creation, not guidance).

## 7. MCP maintenance & compatibility checklist (per AGENTS.md)

- [x] **Guidance:** `BusinessRulesGuidanceResource` edited (new section + step 8 + mistake line);
      content unit test updated (7 asserts). Routing map unchanged (already lists `business-rules`).
- [x] **Tool descriptions:** reviewed — pointer to `business-rules` already present; **no change**.
      *MCP reviewed, no tool-surface update required.*
- [x] **Templates (`clio/tpl/**` AGENTS.md):** no new tool name introduced → no
      `WorkspaceTemplateGuidanceDriftTests` impact. Reviewed, no change.
- [x] **CLI docs (`help/en`, `docs/commands`, `Commands.md`):** confirmed business rules have **no CLI
      `[Verb]`** (grep clean) → MCP-only surface → CLI docs N/A.
- [x] **ClioRing gate:** change limited to guidance-resource **text**; no tool name/args/result/
      destructive/discovery/progress change. Grep of `clio-ring` for these tools/guidance → **zero refs**.
      *ClioRing compatibility reviewed, no Ring-consumed contract changed.*
- [ ] **Code review gate:** guidance-text + test diff → single combined-lens review before PR (pending).

**Test evidence:** `dotnet test -f net8.0 --filter "FullyQualifiedName~McpGuidanceResourceTests"` → 39/39;
guidance + forcing + tool-contract trio → **131/131** (13 s). Change is isolated guidance text; no other
McpServer fixture depends on it.

## 8. Out of scope / follow-ups

- Applying the same "ask before verifying" pattern to **widget / dashboard / page** creation
  (siblings ENG-90486/90489/90495 etc.). The pattern designed here should be lifted into a shared
  guidance snippet if the team later wants it epic-wide. **Not in this ticket.**
- A clio-owned, agent-agnostic preference store + get/set MCP tools (rejected as D1; would be the path
  if ClioRing or non-memory agents ever need deterministic persistence).

## 9. Work plan

| Step | Task | Est. |
|------|------|------|
| 1 | Edit `BusinessRulesGuidanceResource.cs` (§4.1–4.3). | S |
| 2 | Extend guidance unit test (TC-U-1…5). | S |
| 3 | Run `Module=McpServer` unit filter; confirm green. | S |
| 4 | Complete §7 checklist; write MCP + ClioRing review statements into the PR body. | S |
| 5 | Pre-PR combined-lens review; open PR to `master`; move story to `review`. | S |

**Definition of done:** guide states the ask-auto-vs-manual + persist-in-memory + no-auto-browser +
manual-summary-and-steps behavior; unit tests assert it; `Module=McpServer` suite green; MCP &
ClioRing review statements recorded in the PR.
