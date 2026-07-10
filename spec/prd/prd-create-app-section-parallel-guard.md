# PRD: create-app-section — Parallel-Contention Guard and Retryable-Rejection Recovery

**Status**: Draft
**Author**: PM Agent
**Created**: 2026-07-10
**Jira**: [ENG-93089](https://creatio.atlassian.net/browse/ENG-93089) (Engineering sub-task, Major, In Progress, assignee Bohdan Horodyskyi). Source: ENG-90634 session analysis, rollout 2026-05-27 session 1. Parent context: the create-app / create-app-section AI-driven app-creation toolkit.

---

## Problem Statement

When an AI agent creates several sections in one Creatio application concurrently, `create-app-section` fails with the opaque body `{"success":false,"error":"InsertQuery failed."}` even though every input is valid. Two defects compound: the concurrent inserts contend server-side and abort each other (there is no serialization on the async MCP path), and clio then classifies the detail-less `InsertQuery failed` rejection as a non-retryable `ServerError` — telling the agent NOT to retry, which is the exact opposite of what recovers (the same payloads succeed when run sequentially). This blocks the AI no-code app-creation flow, which naturally batches section-create calls.

## Evidence (confirmed reproduction)

Session rollout `019e6982`, user a.shykova, 2026-05-27, application `UsrClient`:

- Three `create-app-section` calls against ONE application were issued as a parallel batch — start timestamps 13:24:02.235 / .286 / .287 (within 52 ms).
- All three failed with `{"success":false,"error":"InsertQuery failed."}` after only 36 / 36 / 55 s.
- ~27 min later the agent re-ran the SAME payloads SEQUENTIALLY (13:51:06, then 13:52:46 after the first finished): both SUCCEEDED, taking 93.3 s and 102.6 s.
- Conclusion: parallel → fail, sequential → success ⇒ lock/contention, not bad input. Each section takes ~93–102 s, so overlap is likely whenever an agent batches calls.
- Signal: failed calls returned FASTER (36–55 s) than successful ones (93–103 s) — contention aborts the insert mid-way.
- Control: sibling session `019e68d6` (user a.voloshchenko) created an app but never reached parallel section creation → no repro. Confirms the trigger is specifically concurrent section inserts.

### Current-state code findings (master 8.1.0.69 — verified by reading source)

- `clio/Command/ApplicationSectionCreateCommand.cs` — `ApplicationSectionCreateService.CreateSection` performs the `InsertQuery` POST, then `EnsureInsertSucceeded` turns a detail-less `success:false` / `"InsertQuery failed."` body into an `ApplicationSectionCreateException` with `FailureClass = ServerError`, `sectionCreated = false`, and `ServerErrorRetryGuidance` ("retrying with the same arguments will most likely fail again"). There is **no serialization anywhere** in this path.
- The service already contains the building blocks for recovery: `TryVerifySectionExists` (bounded post-timeout existence check that matches strictly by the client-generated section `Id`, so a pre-existing same-entity section cannot cause a false positive) and `ClassifyHttpStatus`, which already maps transient statuses (408/429/502/503/504) to the retryable `CreatioTimeout` class. The detail-less `InsertQuery failed` body does not flow through that classifier — it is caught in `EnsureInsertSucceeded` and hard-coded to `ServerError`.
- `clio/Command/ApplicationSectionCreateFailure.cs` — `ApplicationSectionCreateFailureClass` enum (`Transport` / `CreatioTimeout` / `ServerError`) + `ApplicationSectionCreateException` carrying `FailureClass`, `SectionCreated`, `RetryGuidance` (surfaced on the MCP envelope as `error-class` / `section-created` / `retry-guidance`).
- `clio/Command/McpServer/Tools/ApplicationTool.cs` — the async MCP tool `ApplicationSectionCreate` (`ApplicationSectionCreateToolName = "create-app-section"`) runs via `McpProgressHeartbeat.RunWithProgressAndDeadlineAsync`. It does NOT hold `BaseTool`'s `lock(CommandExecutionLock)`: it is a typed-response async tool that bypasses `BaseTool.InternalExecute`, so the global MCP lock never covers the insert.
- `clio/Command/McpServer/Tools/BaseTool.cs` + `McpToolExecutionLock.cs` — the global static in-process lock (`McpToolExecutionLock.SyncRoot`) that the synchronous command paths take. It cannot span `await` boundaries (the async section-create path bypasses it) and does not span processes.

### Two problems, one sharper than the ticket

1. **Concurrency is unguarded.** No lock/serialization protects concurrent section inserts against the same application. The existing global MCP lock does not (and cannot, across `await`) apply to this async path, and does not span processes anyway — the toolkit spawns a fresh `clio mcp-server` per call via `scripts/mcp_client.py`.
2. **The detail-less `InsertQuery failed` is mis-classified as non-retryable `ServerError`.** The agent is told NOT to retry, the opposite of what works (serialized retry succeeds). This is a correctness/guidance bug, not only a UX nicety.

## Goals

- [ ] Goal 1 — Eliminate spurious `InsertQuery failed` on concurrent section creation against one application. Success metric **SM-01**: an E2E scenario that issues N concurrent `create-app-section` calls against one application produces zero contention-caused `InsertQuery failed` failures across repeated runs (all sections end up created). / Counter **CM-01**: a legitimately invalid input (duplicate section code, missing entity schema, non-Latin caption with no `--code`) still fails fast with its existing actionable message — the guard must not mask real rejections or convert them into silent success.
- [ ] Goal 2 — Give the agent correct recover-by-serialize guidance for the detail-less rejection. Success metric **SM-02**: a detail-less `InsertQuery failed` that is not proven to be a real conflict is surfaced with a retryable classification and guidance that tells the agent to serialize/retry (verified by unit tests on the classification + guidance contract). / Counter **CM-02**: no duplicate section is created by the recovery path — existence is re-verified (by client-generated `Id`) before any retry, and a genuine code collision is still reported as non-retryable.
- [ ] Goal 3 — Document the constraint so agents stop batching. Success metric **SM-03**: the tool `[Description]` and the relevant MCP guidance article state that sections in one application must be created sequentially, not in parallel (asserted by a guidance/description content test). / Counter **CM-03**: no measurable regression to the single-section happy-path latency or response size.

## Non-goals

- Will NOT guarantee cross-process / cross-agent serialization in this ticket via an in-process primitive alone. An in-process semaphore does not serialize two separate `clio` processes (the toolkit spawns one `clio mcp-server` per call) or two agents hitting the same app — see Option A trade-off and OQ-01. Whether cross-process (cliogate/server-side or DB-level) serialization ships here is an explicit open question, not an assumed deliverable.
- Will NOT change the synchronous CLI happy-path timeout/readback semantics (the `Timeout.Infinite` readback default, the 90 s insert budget, `CLIO_CREATE_SECTION_TIMEOUT_SECONDS`).
- Will NOT alter the existing `CreatioTimeout` recovery flow (`RecoverFromInsertTimeout` / `TryVerifySectionExists`) beyond reusing it for the new retry path.
- Will NOT add unbounded or indefinite automatic retries — any auto-retry is strictly bounded (see FR-05).
- Will NOT redesign the section-create service or migrate it off DataService / onto ClioGate for the insert itself.

## User Stories (high level)

| As a | I want | So that |
|------|--------|---------|
| AI no-code agent (MCP client) | to batch several `create-app-section` calls for one app and have them all succeed | I do not have to discover the sequential-only constraint by trial and error |
| AI no-code agent (MCP client) | a retryable classification + clear guidance when I hit the detail-less `InsertQuery failed` | I retry/serialize (which works) instead of abandoning (current wrong guidance) |
| developer using clio CLI | concurrent scripted `create-app-section` calls against one app to not fail spuriously | my CI/scripts that fan out section creation are reliable |
| QA engineer | a deterministic `clio.mcp.e2e` scenario that reproduces the contention and proves the fix | I can guard against regression of this exact bug |

## Feature Requirements

| ID | Requirement | Priority |
|----|-------------|----------|
| FR-01 | Concurrent `create-app-section` operations targeting the same environment + application MUST NOT contend server-side into a spurious `InsertQuery failed`; overlapping requests are serialized or otherwise reconciled so each valid section is created. | Must |
| FR-02 | A detail-less `InsertQuery failed` rejection that cannot be attributed to a genuine, permanent cause (e.g. a proven code collision) MUST be classified as retryable rather than non-retryable `ServerError`, and its `retry-guidance` MUST direct the agent to serialize/retry. | Must |
| FR-03 | Before any automatic or advised retry, section existence MUST be re-verified by the client-generated section `Id` (reusing `TryVerifySectionExists` / `list-app-sections` semantics) so a retry can never create a duplicate or trigger an "already bound" error. | Must |
| FR-04 | A genuine permanent rejection (duplicate section code, missing/invalid entity binding, non-Latin caption without `--code`) MUST continue to fail fast with its current actionable message and remain non-retryable. | Must |
| FR-05 | Any automatic retry MUST be strictly bounded (finite attempt count, no indefinite loop) and MUST NOT extend the operation beyond the client's observed request ceiling in a way that reintroduces the opaque `-32001 Request timed out`. | Must |
| FR-06 | The MCP tool `[Description]` for `create-app-section` MUST document that sections in one application must be created sequentially, not in parallel. | Must |
| FR-07 | The MCP guidance surface (app-modeling and existing-app-maintenance guides) MUST state the sequential-only constraint and the recover-by-serialize behavior. | Must |
| FR-08 | The CLI/GitHub docs for `create-app-section` MUST reflect the sequential-only constraint and any new/changed behavior. | Should |
| FR-09 | A `clio.mcp.e2e` scenario MUST cover the concurrent-create → no-spurious-failure path and the retry/serialization recovery. | Must |
| FR-10 | The serialization/guard scope key MUST be environment + application-code (concurrent creates against different applications, or different environments, MUST remain fully parallel). | Should |

## Solution Options (framing for the ADR — final decision deferred)

The ADR owns the final design. Three options, recommended combination **A + C primary, B as the cross-process-robust complement**:

| Option | Approach | Fixes | Trade-off / limitation |
|--------|----------|-------|------------------------|
| **A — In-process async serialization** | Keyed async lock (`SemaphoreSlim`) on environment + application-code around the insert on the async MCP path. | The dominant single-server parallel-batch repro (one `clio` process, batched calls). | Does NOT serialize two separate `clio` processes / two agents against the same app — and the toolkit spawns a fresh `clio mcp-server` per call, so a pure in-process lock may not even cover the observed repro topology. Must be verified against how the toolkit actually hosts the server. |
| **B — Auto-retry-once-with-verify + reclassification** | On the detail-less `InsertQuery failed`: re-check existence via `TryVerifySectionExists` / `list-app-sections`, then bounded retry; fix the misleading `ServerError` classification + guidance for this specific detail-less rejection. | The mis-classification (Problem 2) and is robust across processes/agents because it recovers after the contention regardless of who caused it. | Adds latency on the failure path; must strictly avoid duplicates ("already bound") via the `Id`-based existence check; must not retry genuine permanent rejections (FR-04). |
| **C — Documentation + guidance + mandatory E2E** | Tool `[Description]` + app-modeling / existing-app-maintenance MCP guidance stating sequential-only; mandatory `clio.mcp.e2e` scenario. | Prevents agents from batching in the first place and locks in regression coverage. | Guidance alone does not enforce; must be paired with A and/or B. |

**Cross-process limitation to decide explicitly (OQ-01):** if cross-process robustness is required within this ticket, only a server-side (cliogate) or DB-level serialization/idempotency mechanism fully solves it; A cannot. Recommendation: A + C as primary (fixes and documents the dominant repro), B as the complement that recovers correctly even across processes, and defer true cross-process serialization to a follow-up unless the ADR finds a low-cost server-side option.

## Non-functional Requirements

| ID | Requirement |
|----|-------------|
| NFR-01 | Single-section happy path: no measurable latency or response-size regression (CM-03). |
| NFR-02 | Serialization must not deadlock, leak semaphores/keys, or starve unrelated apps/environments; keys are scoped and released deterministically even on exception. |
| NFR-03 | No raw `HttpClient`; all Creatio calls continue through `IApplicationClient` per project rules. |
| NFR-04 | New behavior classes are constructor-injected and DI-registered (no `new`, no MediatR); any new CLI flag is kebab-case (CLIO001); no new `CLIO*` diagnostics. |
| NFR-05 | Unit tests use `[Category("Unit")]`, `MethodName_ShouldExpectedBehavior_WhenCondition`, AAA + a `because` on each assertion + `[Description]`; command tests prefer `BaseCommandTests<TOptions>`. |

## CLI Impact

| Change | Details | Breaking? |
|--------|---------|-----------|
| No new flag expected (default-on internal guard) | The serialization/retry guard is intended to be transparent default behavior, not a user-facing flag. | No |
| Possible optional tuning flag (ADR decision) | If a knob is needed (e.g. retry count / concurrency), it MUST be kebab-case, e.g. `--section-create-max-retries`; default preserves current behavior. | No |

All flags: **kebab-case only** (CLIO001 enforced). No breaking changes anticipated; if any flag is renamed, a hidden alias is added per policy.

## Acceptance Criteria

Mapped to the three Jira acceptance criteria (JAC-1: no spurious `InsertQuery failed` under concurrency; JAC-2: E2E covers retry/serialization; JAC-3: tool `[Description]` / guidance documents sequential-only).

- [ ] **AC-01 (JAC-1)**: Given one application and N `create-app-section` calls issued concurrently against it, when the guard is active, then no call fails with a contention-caused `InsertQuery failed` and every valid section is created (verified by the E2E scenario, repeated runs).
- [ ] **AC-02 (JAC-1)**: Given a detail-less `{"success":false,"error":"InsertQuery failed."}` insert response that is not a proven permanent rejection, when it is classified, then `error-class` is retryable (not `server-error`) and `retry-guidance` instructs the agent to serialize/retry.
- [ ] **AC-03 (JAC-1, no duplicates)**: Given a retry is attempted after a detail-less rejection, when the section with the client-generated `Id` already exists, then the operation returns the existing section (readback) and does NOT insert a duplicate or raise "already bound".
- [ ] **AC-04 (JAC-1, real errors preserved)**: Given a genuine permanent rejection (duplicate code / missing entity / non-Latin caption without `--code`), when it occurs, then it still fails fast with the existing actionable message and remains non-retryable (`section-created=false`).
- [ ] **AC-05 (JAC-2)**: Given the `clio.mcp.e2e` harness, when the concurrent-create scenario runs, then it exercises the real `clio mcp-server` path and asserts both the no-spurious-failure outcome and the retry/serialization recovery.
- [ ] **AC-06 (JAC-3)**: Given the `create-app-section` MCP tool metadata and the app-modeling / existing-app-maintenance guidance, when inspected, then both state that sections in one application must be created sequentially, not in parallel (asserted by a content test).
- [ ] **AC-ERR**: Given invalid input to `create-app-section`, clio prints `Error: {message}` and exits non-zero (unchanged), and the classified failure carries `error-class` / `section-created` / `retry-guidance` on the MCP envelope.

## Assumptions Index

| # | Assumption | Risk if wrong |
|---|-----------|--------------|
| A-01 | The dominant repro is multiple concurrent calls within a topology an in-process async lock can cover. | If the toolkit truly spawns one process per call, Option A alone will not fix the observed repro; Option B (recover-after-contention) becomes primary. |
| A-02 | The detail-less `InsertQuery failed` under contention is transient (serialized retry succeeds), distinguishable from a permanent code-collision rejection. | If the two are indistinguishable from the response body alone, reclassification risks retrying genuine failures — existence re-verification (FR-03) is the safeguard. |
| A-03 | `TryVerifySectionExists` (Id-matched) reliably detects a committed section post-contention. | A false negative causes an unnecessary retry; a false positive is prevented by strict `Id` matching. |
| A-04 | Bounded retry within the client's request ceiling is enough for the ~93–102 s serialized insert to complete. | If a serialized insert plus retry exceeds the ~180 s client ceiling, the agent again sees `-32001`; the ADR must bound attempts accordingly. |
| A-05 | Sections against different applications/environments never contend. | If server-side contention is broader than per-application, the guard scope (FR-10) is too narrow. |

## Open Questions

| # | Question | Owner | Due |
|---|---------|-------|-----|
| OQ-01 | Is cross-process / cross-agent serialization (cliogate or DB-level) in scope for ENG-93089, or deferred to a follow-up? | TBD | TBD |
| OQ-02 | How does the toolkit (`scripts/mcp_client.py`) host `clio mcp-server` — one long-lived process, or one per call? Determines whether Option A can cover the repro. | TBD | TBD |
| OQ-03 | Can the server distinguish contention-abort from a real code-collision in the insert response (any detail we can key on)? | TBD | TBD |
| OQ-04 | Is a user-facing tuning flag (retry count / concurrency) desired, or is a fixed internal default sufficient? | TBD | TBD |

## Dependencies

- Depends on: existing `ApplicationSectionCreateService` classification + `TryVerifySectionExists` recovery infrastructure; `IApplicationSectionCreateService`; MCP async tool path (`McpProgressHeartbeat.RunWithProgressAndDeadlineAsync`).
- Related MCP surface (mandatory review/update): `clio/Command/McpServer/Tools/ApplicationTool.cs` (`create-app-section` tool `[Description]`), `clio/Command/McpServer/Resources/AppModelingGuidanceResource.cs`, `clio/Command/McpServer/Resources/ExistingAppMaintenanceGuidanceResource.cs`, and the routing map row if a guide is renamed; unit tests in `clio.tests/Command/McpServer/**`; E2E in `clio.mcp.e2e/**`.
- Docs to review/update: `clio/help/en/create-app-section.txt`, `clio/docs/commands/create-app-section.md`, `clio/Commands.md`, `docs/McpCapabilityMap.md`.
- Parent context: ENG-90634 (session analysis) / the create-app AI toolkit epic.
- Blocks: reliable AI no-code multi-section app creation.
