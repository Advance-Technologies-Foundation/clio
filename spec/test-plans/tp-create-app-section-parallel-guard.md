# Test Plan: create-app-section — Parallel-Contention Guard and Retryable-Rejection Recovery

**Feature**: create-app-section-parallel-guard
**Jira**: [ENG-93089](https://creatio.atlassian.net/browse/ENG-93089)
**Stories**:
[story-1 (guard)](../stories/story-create-app-section-parallel-guard-1.md) ·
[story-2 (contention+retry)](../stories/story-create-app-section-parallel-guard-2.md) ·
[story-3 (MCP tool/guidance)](../stories/story-create-app-section-parallel-guard-3.md) ·
[story-4 (CLI docs)](../stories/story-create-app-section-parallel-guard-4.md) ·
[story-5 (E2E)](../stories/story-create-app-section-parallel-guard-5.md)
**PRD**: [prd-create-app-section-parallel-guard.md](../prd/prd-create-app-section-parallel-guard.md)
**ADR**: [adr-create-app-section-parallel-guard.md](../adr/adr-create-app-section-parallel-guard.md)
**Author**: QA Planner Agent
**Status**: Draft
**Created**: 2026-07-10

---

## Scope

### In scope
- ADR part **A** — in-process keyed serialization guard (`ISectionCreateSerializationGuard`) around the `insert→readback` span, keyed by `environment + application-code` (case-insensitive), bounded wait, best-effort degrade on timeout, release-in-`finally`.
- ADR part **B** — reclassify the detail-less `InsertQuery failed` body from terminal `ServerError` to retryable `Contention`; verify-by-`Id` (`TryVerifySectionExists`) then retry-once; `contention` wire value on the MCP envelope.
- ADR part **C** — tool `[Description]` + both MCP guidance guides + CLI/GitHub docs + `McpCapabilityMap`; the mandatory `clio.mcp.e2e` concurrent-create scenario.
- Regression flip of the **existing** `ApplicationSectionCreateServiceTests` cases whose detail-less-body assertions become `Contention`.

### Out of scope (with reason)
- Cross-process / cross-agent serialization (OQ-01) — ADR Decision 5 defers cliogate/DB-level serialization to a follow-up; only the in-process guard (A) plus recover-after-contention (B) are in this ticket.
- Changing the synchronous CLI happy-path timeout/readback semantics (`Timeout.Infinite` readback, 90 s insert budget, `CLIO_CREATE_SECTION_TIMEOUT_SECONDS`) — PRD non-goal.
- Altering the existing `CreatioTimeout` recovery flow (`RecoverFromInsertTimeout`) beyond reusing `TryVerifySectionExists`.
- Any new CLI flag (guard/retry are transparent defaults; OQ-04 tuning knob not shipped here).
- MCP envelope shape / `ApplicationSectionCreateException` type change (both unchanged — additive enum member only).

---

## Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|-----------|
| **[HIGHEST] `ServerError` → `Contention` reclassification** — the detail-less `{"success":false}` / `InsertQuery failed.` body flips from terminal-non-retryable to retryable, and now drives a verify-then-retry flow that issues extra `IApplicationClient` calls. Existing tests that fed a detail-less body and asserted the old behavior break. | High | High | Enumerated flip list below (TC-U-10..12); every remaining `ServerError` case is re-asserted to stay terminal (TC-U-13..16). Predicate is intentionally narrow (detail-less only); any *detailed* message stays `ServerError`. |
| Insert→readback critical-section widening — the guard now wraps not just the insert POST but the whole `LoadCreatedSection`/`RecoverFromInsertTimeout` span (~93–102 s). A bug here could serialize/park more work than intended or hold the lock across the readback poll loop. | Med | High | TC-U-01/02 assert serialization boundary via recording client; TC-U-06 asserts prep reads stay OUTSIDE the guard (fail-fast parallel); TC-U-05 asserts release-in-`finally` on throw so a readback exception cannot leak the semaphore. |
| Retry composing with the 90 s (CLI) / 600 s (MCP background) insert budgets + 30 s verify + 2 s backoff (A-04/FR-05) — a serialized insert plus retry could breach the client's ~180 s ceiling and re-surface `-32001`. | Med | Med | TC-U-08 asserts retry bounded to exactly one; TC-U-20 asserts the guard wait uses the resolved insert budget as its timeout; budget-capture regression (TC-U-17) confirms insert-timeout arg still flows unchanged. |
| Best-effort degrade path — on guard wait-timeout the work runs UNSERIALIZED (must not throw, must warn), so a deep queue never becomes a hard failure. Easy to get wrong (throw, or skip the warning, or release a never-acquired semaphore). | Med | Med | TC-U-03 (degrade + warn + no throw) and TC-U-05 (release only if acquired). |
| Duplicate-section creation on retry (CM-02/AC-03) — retrying after a section actually committed under contention would create a duplicate or raise "already bound". | Low | High | TC-U-07 asserts verify-`true` ⇒ readback + **zero** second insert POST; strict `Id` match reused from existing `TryVerifySectionExists`. |
| Budget-capture helper coupling — `SetUpInsertTimeoutCaptureMocks` returns `{"success":false,"errorInfo":{"message":"Rejected"}}`. `"Rejected"` is a *detailed* message ⇒ must stay terminal `ServerError` (one insert). If the predicate is authored too broadly it becomes `Contention`, fires a second insert, and silently breaks the timeout-capture tests. | Med | Med | Predicate rule pinned in TC-U-10/13; regression note calls out this helper explicitly (see Regression Guard). |
| MCP tool exposure vs CLI — guard lives in the service (both surfaces funnel through `CreateSection`), NOT the tool. Guarding the tool only would leave the CLI insert unprotected. | Low | Med | TC-U-06 targets the service SUT directly; E2E (TC-E2E-01) exercises the real MCP path. |
| Docs/guidance drift — `contention` must appear consistently across tool `[Description]`, both guidance guides, help txt, docs md, `Commands.md`, `McpCapabilityMap.md`. | Med | Low | Content tests TC-U-21..24; docs-consistency gate TC-U-25. |

---

## Unit Tests (`clio.tests/`)

All `[Category("Unit")]`, `Module=Command` (guard + service) or `Module=McpServer` (content tests).
Naming `MethodName_ShouldExpectedBehavior_WhenCondition`; AAA + a `because` on every assertion + `[Description]` on every test; NUnit 4 / FluentAssertions / NSubstitute; SUT resolved from DI where a `BaseCommandTests`-style fixture applies.

### Story 1 — Serialization guard (`clio.tests/Command/SectionCreateSerializationGuardTests.cs`, new)

Concurrency is tested with a **barrier/gated `IApplicationClient` substitute**: the first `work` blocks inside a recorded "insert started" callback on a `ManualResetEventSlim` until the test releases it; a recording list captures `(key, phase, timestamp)` for start/end of each `work`. Determinism comes from the barrier, never from `Thread.Sleep` races.

#### TC-U-01: same env+app-code serializes — second insert must not start until the first releases
- **Story**: 1 · **PRD AC**: AC-01 · **Jira**: JAC-1 · **FR**: FR-01 · **Story AC**: 1/AC-01
- **Precondition**: guard SUT resolved from DI; a recording substitute whose `work` records start/end.
- **Steps**:
  1. Start task T1 `Run("env","AppA", budget, work1)`; `work1` records "start", then blocks on a barrier the test controls.
  2. Spin-wait until T1 has recorded "start" (T1 owns the semaphore).
  3. Start task T2 `Run("env","AppA", budget, work2)`; assert `work2` has **not** recorded "start" while T1 holds the lock (T2 is parked in `Wait`).
  4. Release the barrier; T1 records "end" and releases; T2 then records "start"/"end".
- **Expected**: the two `[start,end]` intervals are **non-overlapping** (T2.start ≥ T1.end); both complete; results returned unchanged.
- **Because**: same env+app-code must serialize the destructive span (FR-01/AC-01).

#### TC-U-02: different app-codes do NOT serialize — inserts allowed to overlap
- **Story**: 1 · **PRD AC**: (FR-10) · **Story AC**: 1/AC-02
- **Precondition**: guard SUT; barrier substitute.
- **Steps**:
  1. Start T1 `Run("env","AppA", budget, work1)`; `work1` records "start" then blocks on a barrier.
  2. Start T2 `Run("env","AppB", budget, work2)`; `work2` records "start".
  3. Assert T2 recorded "start" **while T1 is still blocked** (overlap observed).
  4. Release both barriers.
- **Expected**: the two intervals **overlap** (different keys ⇒ different semaphores).
- **Because**: creates against different apps must stay fully parallel (FR-10).

#### TC-U-03: wait-timeout degrades to best-effort — runs unserialized, warns, does not throw
- **Story**: 1 · **PRD AC**: (NFR-02, ADR Q2) · **Story AC**: 1/AC-03
- **Precondition**: guard SUT with an injected `ILogger` substitute; T1 holds the "env␟AppA" semaphore (blocked in a barrier).
- **Steps**:
  1. With T1 holding the lock, call `Run("env","AppA", waitTimeout: 50 ms, work2)`.
  2. Let the 50 ms wait elapse.
- **Expected**: `work2` **executes** (unserialized), returns its result; a warning is logged (`logger.Received().WriteWarning(...)` / equivalent); **no** exception thrown.
- **Because**: a deep queue must never become a hard failure — contention is caught by Option B (NFR-02, ADR Q2).

#### TC-U-04: case-insensitive key — env/app differing only by case map to the same semaphore
- **Story**: 1 · **Story AC**: 1/AC-05 · **FR**: FR-10
- **Precondition**: guard SUT; barrier substitute.
- **Steps**: run `Run("ENV","APPA", …)` (blocked) then `Run("env","appa", …)`; assert the second parks (serialized), mirroring TC-U-01.
- **Expected**: both map to key `env␟appa` (`ToLowerInvariant()` on both parts joined by `␟`) ⇒ serialized.
- **Because**: the guard key is case-insensitive (ADR Q1, FR-10).

#### TC-U-05: semaphore released in `finally` when work throws (no leak) and exception propagates
- **Story**: 1 · **Story AC**: 1/AC-04 · **NFR**: NFR-02
- **Precondition**: guard SUT.
- **Steps**:
  1. `Run("env","AppA", budget, () => throw new InvalidOperationException("boom"))` — assert it throws `InvalidOperationException` unchanged.
  2. Immediately `Run("env","AppA", budget, work2)` and assert `work2` acquires without blocking (semaphore was released).
- **Expected**: original exception propagates unchanged; the key is immediately re-acquirable (released in `finally`, only because it was acquired).
- **Because**: exceptions in `work` must not leak the semaphore (NFR-02).

#### TC-U-ERR (guard): uncontended single call pays exactly one Wait/Release and returns unchanged
- **Story**: 1 · **Story AC**: 1/AC-ERR · **NFR**: NFR-01 / CM-03
- **Precondition**: guard SUT, no contention.
- **Steps**: `Run("env","AppA", budget, () => 42)`.
- **Expected**: returns `42`; a subsequent call for the same key acquires immediately (lock was released).
- **Because**: the uncontended happy path must not regress (NFR-01/CM-03).

### Story 1 — service wiring (`clio.tests/Command/ApplicationSectionCreateServiceTests.cs`, existing fixture)

#### TC-U-06: only the insert→readback span is guarded; prep reads run OUTSIDE the guard
- **Story**: 1 · **PRD AC**: AC-01 (+ CM-01/FR-04 fail-fast) · **Story AC**: 1/AC-01, 1/AC-ERR
- **Precondition**: service SUT with an `ISectionCreateSerializationGuard` **substitute**; recording `IApplicationClient`.
- **Steps**:
  1. Configure the guard substitute so `Run<T>` records the call order relative to the client calls, and executes the `work` delegate it receives.
  2. Run a normal successful `CreateSection`.
- **Expected**: `guard.Run` is invoked with `environmentName` + `resolvedRequest.ApplicationCode` + the resolved insert budget; the `SysAppIcons` / `SysSchema` preparation reads and `GetApplicationInfo` happen **before** `guard.Run`; the `Insert` POST and readback happen **inside** the delegate passed to `guard.Run`.
- **Because**: prep reads must fail fast in parallel (CM-01/FR-04) and only the destructive span is serialized (ADR Q1).

### Story 2 — Contention classification + retry-with-verify (`clio.tests/Command/ApplicationSectionCreateServiceTests.cs`, existing fixture — add cases)

The verify step is driven by stubbing the `ApplicationSection` `SelectQuery` readback (the `TryVerifySectionExists` query filtered by `ApplicationId`, matched strictly by generated `Id`). Insert attempts are counted with a recording `IApplicationClient` substitute (matching the `rootSchemaName:"ApplicationSection"` + `columnValues` + no-`filters` insert body).

#### TC-U-07: verify returns TRUE ⇒ readback, NO second insert POST (no duplicate)
- **Story**: 2 · **PRD AC**: AC-03 (+ CM-02) · **Jira**: JAC-1 · **FR**: FR-03 · **Story AC**: 2/AC-03
- **Precondition**: first insert returns detail-less `{"success":false}`; verify `SelectQuery` returns a row whose `Id` equals the generated section `Id`; readback query returns a valid section.
- **Steps**: `CreateSection("sandbox", CreateNewEntityRequest())`.
- **Expected**: returns the `LoadCreatedSection` readback result; the insert POST was issued **exactly once** (`client.Received(1)` on the insert body); no `ApplicationSectionCreateException` thrown; no "already bound".
- **Because**: a section that committed despite the aborted response must be returned, never re-inserted (AC-03/CM-02/FR-03).

#### TC-U-08: verify returns FALSE ⇒ exactly one retry insert (2 s PollDelay), then classify again
- **Story**: 2 · **PRD AC**: AC-02 · **FR**: FR-05 · **Story AC**: 2/AC-04
- **Precondition**: first insert detail-less; verify `SelectQuery` returns rows without the generated `Id` (⇒ `false`); second insert succeeds (`{"success":true}`); readback returns the section.
- **Steps**: `CreateSection(...)`.
- **Expected**: insert POST issued **exactly twice** (`client.Received(2)`); the call succeeds; retry is bounded to one attempt (no third insert).
- **Because**: a verified-absent section is safe to retry exactly once (FR-05).

#### TC-U-09: verify returns NULL (verification itself failed) ⇒ NO blind retry, throw Contention with section-created=null
- **Story**: 2 · **PRD AC**: AC-02 · **FR**: FR-03/FR-05 · **Story AC**: 2/AC-05
- **Precondition**: first insert detail-less; verify `SelectQuery` throws (⇒ `TryVerifySectionExists` returns `null`).
- **Steps**: `CreateSection(...)` — capture the `ApplicationSectionCreateException`.
- **Expected**: insert POST issued **exactly once** (no blind retry); `FailureClass == Contention`; `SectionCreated == null`; `RetryGuidance` contains the serialize/`list-app-sections` text.
- **Because**: when existence cannot be confirmed, a blind retry could duplicate — do not auto-retry (FR-03/FR-05, AC-05).

#### TC-U-10: detail-less body ⇒ error-class = contention, retryable, serialize/retry guidance
- **Story**: 2 · **PRD AC**: AC-02 · **Jira**: JAC-1 · **FR**: FR-02 · **Story AC**: 2/AC-01
- **Precondition**: insert returns `{"success":false}`; verify `false`; the single retry also returns detail-less (so the terminal outcome is a thrown `Contention`).
- **Steps**: `CreateSection(...)`; capture exception.
- **Expected**: `FailureClass == Contention`; `FailureClass.ToWireValue() == "contention"`; `SectionCreated == false`; `RetryGuidance` states sections must be created one-at-a-time / serialize + retry.
- **Because**: the detail-less rejection is transient and must be surfaced as retryable, not the old "do not retry" `ServerError` (AC-02/FR-02).
- **Cover the predicate variants (`[TestCase]`)**: `{"success":false}` (no `errorInfo`), `{"success":false,"errorInfo":{"message":""}}` (empty), `…"message":"InsertQuery failed."` and `…"message":"InsertQuery failed"` (with/without trailing period, `OrdinalIgnoreCase`) — **all ⇒ `contention`**.

#### TC-U-11 (FLIP of existing): detail-less reused-entity rejection ⇒ Contention, not ServerError
- **Story**: 2 · **PRD AC**: AC-02 · **Story AC**: 2/AC-01
- **Existing test to change**: `CreateSection_Should_Throw_Actionable_Error_When_Insert_Fails_Without_Server_Message_For_Reused_Entity` (body `{"success":false}`, entity `Contact`, currently only asserts message text and implicitly rode the `ServerError` path).
- **Precondition**: add a verify `SelectQuery` stub (return `false`) and a second-insert stub so the flow terminates deterministically.
- **Expected**: `FailureClass == Contention`; message still names the reused entity `Contact` and avoids the false "already bound" language; guidance is the serialize/retry text.
- **Because**: the reused-entity detail-less rejection is now retryable contention.

#### TC-U-12 (FLIP of existing): detail-less new-object rejection ⇒ Contention; empty-message ⇒ Contention
- **Story**: 2 · **PRD AC**: AC-02 · **Story AC**: 2/AC-01
- **Existing tests to change**:
  - `CreateSection_Should_Throw_Actionable_Error_When_Insert_Fails_For_New_Object_Without_Server_Message` (body `{"success":false}`).
  - `CreateSection_Should_Use_Fallback_When_Server_Returns_Empty_Error_Message` (body `{"success":false,"errorInfo":{"message":""}}`).
- **Precondition**: add verify (`false`) + second-insert stubs.
- **Expected**: both now assert `FailureClass == Contention` (retryable), guidance = serialize/retry; retained message assertions (e.g. "section with code", generated code) are re-homed onto the terminal `Contention` message or dropped if the message contract changed per ADR Q4.
- **Because**: empty and absent server messages are both detail-less ⇒ contention.

#### TC-U-13: detailed rejection stays terminal ServerError, non-retryable (verify NEVER called)
- **Story**: 2 · **PRD AC**: AC-04 · **Jira**: JAC-1 · **FR**: FR-04 · **Story AC**: 2/AC-02
- **Precondition**: insert returns `{"success":false,"errorInfo":{"message":"Cannot insert duplicate key row"}}`.
- **Steps**: `CreateSection(...)`; capture exception.
- **Expected**: `FailureClass == ServerError`; `SectionCreated == false`; the existing actionable message is preserved; insert POST issued **exactly once**; the verify `SelectQuery` is **never** issued (no retry path entered).
- **Because**: a genuine, detailed rejection must fail fast and stay non-retryable (AC-04/FR-04). Also cover `[TestCase]`s: `"already exists"`, `"already bound"`, `"Duplicate section code violation"`.

#### TC-U-14 (regression, keep): duplicate-key rejection classified ServerError
- **Story**: 2 · **PRD AC**: AC-04 · **Story AC**: 2/AC-02
- **Existing test kept unchanged**: `CreateSection_Should_Classify_Rejected_Insert_As_ServerError` (`"Cannot insert duplicate key row"`).
- **Expected**: still `ServerError`, `SectionCreated == false`, guidance mentions `list-app-sections`.
- **Because**: detailed rejection must NOT flip — proves the predicate is narrow.

#### TC-U-15 (regression, keep): non-JSON / empty response stays ServerError
- **Story**: 2 · **PRD AC**: AC-04 · **Story AC**: 2/AC-02
- **Existing tests kept unchanged**:
  - `CreateSection_Should_Classify_Empty_Insert_Response_As_ServerError` (body `null` ⇒ deserializes to null response object).
  - `CreateSection_Should_Throw_ServerError_Classified_Failure_When_Response_Is_Html` (HTML ⇒ `JsonException`).
- **Expected**: both stay `ServerError`; JSON-null keeps `SectionCreated == null`, HTML keeps `SectionCreated == null`; no verify/retry.
- **Because**: ADR Q3 — non-JSON / empty bodies remain terminal `ServerError` (they are not a detail-less *rejection*).

#### TC-U-16 (regression, keep): protocol/HTTP-status failures still classify as before
- **Story**: 2 (guard against reclassification bleed) · **PRD AC**: AC-04
- **Existing tests kept unchanged**: `CreateSection_Should_Throw_ServerError_Classified_Failure_When_Protocol_Error_Occurs` (500 ⇒ ServerError) and the `ClassifyInsertFailure` chain-walk `[TestCaseSource]`.
- **Expected**: transient statuses (408/429/502/503/504) stay `CreatioTimeout`; 500 stays `ServerError`; transport shapes stay `Transport`. `Contention` is reachable **only** from a parsed `success:false` detail-less body, never from the exception classifier.
- **Because**: the new class must not leak into the HTTP-status/transport classifier.

#### TC-U-17 (regression, keep): insert-timeout budget capture unaffected by the retry path
- **Story**: 2 (composition with budgets) · **NFR**: FR-05
- **Existing tests kept**: the `SetUpInsertTimeoutCaptureMocks`-based budget tests (default 90 s, env-var override, explicit override precedence, clamp) — helper returns `{"success":false,"errorInfo":{"message":"Rejected"}}`.
- **Expected**: `"Rejected"` is **detailed** ⇒ terminal `ServerError` ⇒ insert issued exactly once ⇒ captured timeout arg still asserted correctly; retry path NOT entered.
- **Because**: proves the predicate does not treat a detailed message as detail-less (otherwise a second insert would corrupt the timeout capture).

#### TC-U-18: ToWireValue(Contention) == "contention"; unmapped defaults to "server-error"
- **Story**: 2 · **PRD AC**: AC-02 · **Story AC**: 2/AC-07
- **File**: `clio.tests/Command/ApplicationSectionCreateServiceTests.cs` (or a small `ApplicationSectionCreateFailureTests`).
- **Steps**: assert `ApplicationSectionCreateFailureClass.Contention.ToWireValue() == "contention"`; assert each existing member still maps (`transport`/`creatio-timeout`/`server-error`); assert an out-of-range cast value still yields `"server-error"`.
- **Because**: kebab-case wire value on the `error-class` envelope; back-compatible default preserved (AC-07).

#### TC-U-19: retry bounded to exactly one — retry also detail-less ⇒ throw Contention, no unbounded loop
- **Story**: 2 · **FR**: FR-05 · **Story AC**: 2/AC-06
- **Precondition**: first insert detail-less; verify `false`; second (retry) insert also detail-less; verify after retry `false`.
- **Steps**: `CreateSection(...)`; capture exception.
- **Expected**: insert POST issued **exactly twice** (never three+); throws `Contention` with retryable guidance and `SectionCreated` = last verify outcome (`false`).
- **Because**: strictly one auto-retry, no indefinite loop (FR-05/AC-06).

#### TC-U-20: guard wait timeout equals the resolved insert budget for the call
- **Story**: 1+2 (budget composition) · **NFR**: NFR-02, FR-05
- **Precondition**: service SUT with a guard substitute capturing the `waitTimeout` argument.
- **Steps**: run `CreateSection` on the CLI path (no override) and with an explicit 600 000 ms override (MCP background).
- **Expected**: `guard.Run` receives `waitTimeout` == the resolved insert budget (≈90 s CLI / 600 s MCP background), matching `ResolveInsertTimeoutMilliseconds`.
- **Because**: the guard wait must be bounded by the same budget so a deep queue degrades rather than blocking indefinitely (ADR Q2).

### Story 3 — MCP tool `[Description]` + guidance content tests (`clio.tests/Command/McpServer/`)

Follow the existing content-test fixture pattern in `clio.tests/Command/McpServer/ApplicationToolTests.cs` (reflects over the `[McpServerTool]`/`[Description]` metadata) and the guidance-resource content tests.

#### TC-U-21: create-app-section tool [Description] states sequential-only + lists contention in error-class enum
- **Story**: 3 · **PRD AC**: AC-06 · **Jira**: JAC-3 · **FR**: FR-06 · **Story AC**: 3/AC-01
- **Expected**: the `ApplicationSectionCreate` tool `[Description]` contains the sequential-only constraint ("sections in one application must be created sequentially, not in parallel") and the error-class enumeration reads `(transport | creatio-timeout | server-error | contention)`.

#### TC-U-22: tool args [Description] mentions contention/sequential + the in-process-serialize note
- **Story**: 3 · **FR**: FR-06 · **Story AC**: 3/AC-02
- **Expected**: the args `[Description]` detail-less-rejection sentence mentions `contention`/sequential and the "clio serializes in-process and auto-retries once with verification — do not manually blast parallel create-app-section calls" note.

#### TC-U-23: AppModelingGuidanceResource error-class bullet includes contention + a sequential-only guardrail bullet
- **Story**: 3 · **PRD AC**: AC-06 · **FR**: FR-07 · **Story AC**: 3/AC-03
- **Expected**: `AppModelingGuidanceResource` content includes `contention` (retryable — serialize/retry) on the `create-app-section` error-class bullet and a "create sections in one app sequentially, not in parallel" bullet.

#### TC-U-24: ExistingAppMaintenanceGuidanceResource error-class bullet includes contention + sequential-only note
- **Story**: 3 · **PRD AC**: AC-06 · **FR**: FR-07 · **Story AC**: 3/AC-04
- **Expected**: `ExistingAppMaintenanceGuidanceResource` content includes `contention` and the sequential-only note.
- **Note (3/AC-05)**: routing map (`Resources/RoutingGuidanceResource.cs`) is unaffected — no guide added/renamed; assert-or-state "MCP reviewed: routing map unaffected" in the change summary (no test needed).

### Story 4 — docs-consistency

#### TC-U-25: docs-consistency / ReadmeChecker gate stays green for create-app-section
- **Story**: 4 · **FR**: FR-08 · **Story AC**: 4/AC-ERR
- **Expected**: the existing docs-consistency fixture (if it covers this command) passes with the canonical `[Verb]` name `create-app-section` and the four doc targets (`help/en/create-app-section.txt`, `docs/commands/create-app-section.md`, `Commands.md`, `docs/McpCapabilityMap.md`) consistent.
- **Because**: docs-only change must keep the consistency gate green; the human-facing content (Concurrency note, `contention` error-class) is verified by review, not asserted char-for-char.

---

## Integration Tests (`clio.tests/`)

**None required.** Every unit-testable seam (guard, classification, verify/retry) is exercised with NSubstitute mocks (`[Category("Unit")]`, no I/O). The concurrency and recovery behavior that would otherwise need real I/O is covered deterministically at the unit tier (barrier substitute) and end-to-end at the E2E tier. Adding an `[Category("Integration")]` test here would duplicate coverage without new signal.

---

## E2E Tests (`clio.mcp.e2e/`)

`clio.mcp.e2e/ApplicationSectionToolE2ETests.cs` (new tests in the existing fixture). Mirror the existing destructive sandbox tests: `[Category("McpE2E.Sandbox")]`, gated by `settings.AllowDestructiveMcpTests` + a seeded `settings.Sandbox.EnvironmentName` (both `Assert.Ignore` when absent), `ApplicationCode = "AutoTestClioMcp"`, `SeededApplicationResolver.ResolveOrIgnoreAsync`, and delete-app-section cleanup in `finally`.

> ⚠️ **CI status**: `clio.mcp.e2e` is **NOT in CI**. These tests are manual/release-only. This must be called out in the PR and satisfied by a recorded manual run before merge (story-5 DoD).

#### TC-E2E-01: N concurrent create-app-section calls against one seeded app ⇒ no spurious failure, all created
- **Story**: 5 · **PRD AC**: AC-01, AC-05 · **SM**: SM-01 · **Jira**: JAC-2 · **FR**: FR-09 · **Story AC**: 5/AC-01, 5/AC-02
- **Tool**: `create-app-section`
- **Input**: N (≥3, matching the repro) concurrent `CallToolAsync` invocations against `AutoTestClioMcp`, each with a distinct Latin caption, through one real `clio mcp-server` session.
- **Expected**: every call returns `success:true` with section readback metadata; **zero** responses with `error-class == "contention"` or `"InsertQuery failed."`; repeat the batch across multiple runs (loop) to guard flakiness; clean up all created sections.
- **Manual gate**: add to PR checklist (destructive, seeded env, not in CI).

#### TC-E2E-02: retry/serialization recovery is exercised on the real MCP path
- **Story**: 5 · **PRD AC**: AC-05 · **Jira**: JAC-2 · **FR**: FR-09 · **Story AC**: 5/AC-02
- **Tool**: `create-app-section`
- **Expected**: the scenario asserts it ran against the real `clio mcp-server` (not a mock) and that the concurrent batch which previously produced `InsertQuery failed` now recovers (all sections present after the batch) — proving guard + recover-after-contention end to end.
- **Manual gate**: PR checklist.

#### TC-E2E-03: invalid-input counter-case still fails fast, actionable, non-retryable (guard does not mask real errors)
- **Story**: 5 · **PRD Counter**: CM-01 · **FR**: FR-04 · **Story AC**: 5/AC-03
- **Tool**: `create-app-section`
- **Input**: include in the batch a legitimately invalid call (e.g. a duplicate section code, or reuse an existing code) against the same seeded app.
- **Expected**: that call returns `success:false` with its existing actionable message and a non-retryable/terminal classification (NOT `contention`); the valid siblings still succeed.
- **Manual gate**: PR checklist.

---

## Regression Guard

Tests that MUST stay green after this feature ships (all in `clio.tests/Command/ApplicationSectionCreateServiceTests.cs` unless noted):

| Test name | Why at risk | Expected post-change |
|-----------|-------------|----------------------|
| `CreateSection_Should_Throw_Actionable_Error_When_Insert_Fails_Without_Server_Message_For_Reused_Entity` | Detail-less `{"success":false}` — **FLIPS** to `Contention`; now enters verify/retry so needs new stubs | Rewritten (TC-U-11): `FailureClass == Contention`, entity name retained, no "already bound" |
| `CreateSection_Should_Throw_Actionable_Error_When_Insert_Fails_For_New_Object_Without_Server_Message` | Detail-less new-object — **FLIPS** to `Contention` | Rewritten (TC-U-12) |
| `CreateSection_Should_Use_Fallback_When_Server_Returns_Empty_Error_Message` | Empty `message:""` is detail-less — **FLIPS** to `Contention` | Rewritten (TC-U-12) |
| `CreateSection_Should_Classify_Rejected_Insert_As_ServerError` | Detailed `"Cannot insert duplicate key row"` — must **NOT** flip | Unchanged: `ServerError` (TC-U-14) |
| `CreateSection_Should_Throw_Actionable_Error_When_Insert_Fails_With_Server_Message` | Detailed reused-entity rejection — must **NOT** flip | Unchanged: terminal, message preserved |
| `CreateSection_Should_Propagate_Server_Message_When_Insert_Fails_For_New_Object` | Detailed `"Duplicate section code violation"` | Unchanged: terminal |
| `CreateSection_Should_Append_Period_When_Server_Message_Has_No_Terminal_Punctuation` / `..._Not_Double_Period_...` / `..._Not_Append_Period_After_Terminal_Punctuation` | Detailed messages exercise `BuildSectionInsertFailureMessage`; must stay on the terminal path | Unchanged: terminal `ServerError` message formatting |
| `CreateSection_Should_Classify_Empty_Insert_Response_As_ServerError` (body `null`) | Must NOT be treated as detail-less rejection | Unchanged: `ServerError`, `SectionCreated == null` (TC-U-15) |
| `CreateSection_Should_Throw_ServerError_Classified_Failure_When_Response_Is_Html` | Non-JSON body | Unchanged: `ServerError` (TC-U-15) |
| `CreateSection_Should_Throw_ServerError_Classified_Failure_When_Protocol_Error_Occurs` | HTTP 500 via classifier | Unchanged: `ServerError` (TC-U-16) |
| `ClassifyInsertFailure` chain-walk `[TestCaseSource]` (transport/timeout/500) | New class must not leak into the exception classifier | Unchanged: `Contention` reachable only from parsed detail-less body (TC-U-16) |
| Budget-capture tests via `SetUpInsertTimeoutCaptureMocks` (`"Rejected"` message) | Detailed message must stay terminal so only one insert fires and the captured timeout arg is correct | Unchanged: one insert, `ServerError` (TC-U-17) |
| Happy-path tests (`..._Create_New_Object_Section_With_Web_Pages_Only`, `..._Existing_Entity_Section_With_Mobile_Pages`, platform-entity code match) | Guard now wraps insert→readback; wiring must not perturb the success path | Unchanged: success + readback; guard adds one uncontended Wait/Release |
| Existing `clio.mcp.e2e` sandbox tests (`WithCustomEntity`, `WithPlatformEntity`, missing app-code/caption, non-Latin, non-existent entity) | Guard + reclassification run on the real path | Unchanged behavior; non-Latin/non-existent still fail fast pre-insert (not `contention`) |
| Existing MCP content tests in `clio.tests/Command/McpServer/ApplicationToolTests.cs` | Tool `[Description]` edited | Updated to include `contention` + sequential-only (TC-U-21/22) |

---

## Coverage Estimate

| Layer | New tests | Modified (flipped) tests | Notes |
|-------|-----------|--------------------------|-------|
| Unit — guard (story 1) | 6 (TC-U-01..05, TC-U-ERR) | 0 | New file `SectionCreateSerializationGuardTests.cs` |
| Unit — service wiring (story 1) | 1 (TC-U-06) | 0 | Existing fixture |
| Unit — classification/retry (story 2) | ~7 new (TC-U-07..10, 18, 19, 20) | 3 flipped (TC-U-11, and 2 under TC-U-12) + 6 kept-as-regression (TC-U-13..17) | Existing fixture; predicate `[TestCase]` variants expand effective count |
| Unit — MCP content (story 3) | 4 (TC-U-21..24) | (existing tool-description test updated) | `Module=McpServer` |
| Unit — docs gate (story 4) | 0 (reuse) | 0 | TC-U-25 uses existing fixture |
| Integration | 0 | 0 | Not needed (see rationale) |
| E2E (story 5) | 3 (TC-E2E-01..03) | 0 | Manual/release only — NOT in CI |

Approx: **~18 new unit tests + predicate variants**, **3 flipped + ~6 explicitly-kept regression** unit tests, **3 new E2E** (manual).

---

## Smart-Regression Filters (per story)

Per `AGENTS.md` module mapping. Run the targeted filter for each story before committing; `--no-build` after an initial build.

| Story | Changed source | Filter to run |
|-------|----------------|---------------|
| 1 (guard + service + DI) | `clio/Command/ApplicationSectionCreate*.cs`, `clio/BindingsModule.cs` | `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=Command" --no-build` **plus the full unit suite** (`--filter "Category=Unit"`) because `BindingsModule.cs` (DI composition root) changed |
| 2 (classification + retry) | `clio/Command/ApplicationSectionCreateCommand.cs`, `clio/Command/ApplicationSectionCreateFailure.cs` | `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=Command" --no-build` |
| 3 (MCP tool + guidance) | `clio/Command/McpServer/Tools/ApplicationTool.cs`, `Resources/AppModelingGuidanceResource.cs`, `Resources/ExistingAppMaintenanceGuidanceResource.cs` | `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=McpServer" --no-build` |
| 4 (docs only) | `help/en/*.txt`, `docs/commands/*.md`, `Commands.md`, `docs/McpCapabilityMap.md` | Docs-only — zero-risk triage; run the docs-consistency fixture if present (`Module=Command` or `Module=McpServer` depending on the gate). No code review fan-out required. |
| 5 (E2E) | `clio.mcp.e2e/ApplicationSectionToolE2ETests.cs` | **Manual only — NOT in CI** (see below) |

Because stories 1+2 edit the same `CreateSection` span and story 1 touches `BindingsModule.cs`, run the **full unit suite** once at the end of story 1/2 integration: `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit"`.

### Manual E2E run instructions (story 5) — NOT gated in CI

`clio.mcp.e2e` is **not part of CI**. Run manually against a live seeded stand before merge and record the result in the PR / Dev Agent Record:

1. Configure the sandbox in the E2E settings: set `McpE2E:Sandbox:EnvironmentName` to the seeded environment and `McpE2E:AllowDestructiveMcpTests=true` (otherwise the tests `Assert.Ignore`). The seeded app is `AutoTestClioMcp`.
2. Ensure the environment is reachable (`clio ping-app -e <env>` exits 0).
3. Run only the sandbox category:
   ```bash
   dotnet test clio.mcp.e2e/clio.mcp.e2e.csproj --filter "Category=McpE2E.Sandbox"
   ```
   Or target the new tests by name (`ApplicationSectionCreate_Should_*Concurrent*`).
4. Repeat the concurrent batch several times to confirm no `contention`/`InsertQuery failed` flakiness.
5. Paste the run output (pass + repeat count + stand name) into the PR before flipping story 5 to `done`.

---

## Traceability Matrix

### PRD acceptance criteria → test cases

| PRD AC | Test case(s) |
|--------|--------------|
| AC-01 (no spurious `InsertQuery failed` under concurrency) | TC-U-01, TC-U-06, TC-E2E-01 |
| AC-02 (detail-less ⇒ retryable + serialize/retry guidance) | TC-U-08, TC-U-09, TC-U-10, TC-U-11, TC-U-12, TC-U-18 |
| AC-03 (retry never duplicates; verify-`true` ⇒ readback) | TC-U-07 |
| AC-04 (genuine rejection stays fast + non-retryable) | TC-U-13, TC-U-14, TC-U-15, TC-U-16, TC-E2E-03 |
| AC-05 (E2E exercises real server + recovery) | TC-E2E-01, TC-E2E-02 |
| AC-06 (tool `[Description]` + guidance document sequential-only) | TC-U-21, TC-U-22, TC-U-23, TC-U-24 |
| AC-ERR (invalid input ⇒ `Error:` + classified envelope fields) | TC-U-09 (envelope fields), TC-E2E-03, existing missing-app-code/caption sandbox tests |
| CM-01 (guard does not mask real rejections) | TC-U-06, TC-E2E-03 |
| CM-02 (no duplicate section via recovery) | TC-U-07 |
| CM-03 / NFR-01 (no single-section happy-path regression) | TC-U-ERR (guard), happy-path regression tests |
| SM-01 | TC-E2E-01 |
| SM-02 | TC-U-10, TC-U-18 |
| SM-03 | TC-U-21..24 |

### Feature requirements → test cases

| FR | Test case(s) |
|----|--------------|
| FR-01 (serialize same env+app) | TC-U-01, TC-U-06 |
| FR-02 (retryable classification + guidance) | TC-U-10, TC-U-11, TC-U-12, TC-U-18 |
| FR-03 (verify by `Id` before retry) | TC-U-07, TC-U-09 |
| FR-04 (genuine rejection stays non-retryable) | TC-U-13, TC-U-14, TC-U-16, TC-E2E-03 |
| FR-05 (retry strictly bounded to one) | TC-U-08, TC-U-19, TC-U-20 |
| FR-06 (tool `[Description]` sequential-only) | TC-U-21, TC-U-22 |
| FR-07 (guidance guides) | TC-U-23, TC-U-24 |
| FR-08 (CLI/GitHub docs) | TC-U-25 (gate) + review |
| FR-09 (E2E scenario) | TC-E2E-01, TC-E2E-02, TC-E2E-03 |
| FR-10 (guard scope = env+app-code; different apps parallel; case-insensitive) | TC-U-02, TC-U-04 |
| NFR-01 (no happy-path regression) | TC-U-ERR (guard), happy-path regression |
| NFR-02 (no deadlock/leak; degrade; deterministic release) | TC-U-03, TC-U-05, TC-U-20 |

### Jira acceptance criteria → test cases

| Jira AC | Test case(s) |
|---------|--------------|
| JAC-1 (no spurious failure under concurrency + correct classification) | TC-U-01, TC-U-07..14, TC-E2E-01 |
| JAC-2 (E2E covers retry/serialization) | TC-E2E-01, TC-E2E-02, TC-E2E-03 |
| JAC-3 (tool `[Description]`/guidance documents sequential-only) | TC-U-21..24, TC-U-25 |

### Story → owning test cases

| Story | Test cases |
|-------|-----------|
| 1 — serialization guard | TC-U-01, TC-U-02, TC-U-03, TC-U-04, TC-U-05, TC-U-ERR, TC-U-06, TC-U-20 |
| 2 — contention + retry | TC-U-07, TC-U-08, TC-U-09, TC-U-10, TC-U-11, TC-U-12, TC-U-13, TC-U-14, TC-U-15, TC-U-16, TC-U-17, TC-U-18, TC-U-19, TC-U-20 |
| 3 — MCP tool/guidance | TC-U-21, TC-U-22, TC-U-23, TC-U-24 |
| 4 — CLI/GitHub docs | TC-U-25 |
| 5 — E2E | TC-E2E-01, TC-E2E-02, TC-E2E-03 |

---

## Definition of Done for QA

- [ ] All TC-U-* implemented with `[Category("Unit")]` — never `[Category("UnitTests")]`
- [ ] Guard concurrency tests use a deterministic barrier substitute, never `Thread.Sleep` timing races
- [ ] The 3 flipped existing tests (TC-U-11/12) assert `Contention` and add the verify/retry stubs; the ~6 kept-terminal tests (TC-U-13..17) re-confirm `ServerError`/`CreatioTimeout`/`Transport` do not flip
- [ ] TC-E2E-* implemented with `[Category("McpE2E.Sandbox")]`, gated by `AllowDestructiveMcpTests` + seeded `EnvironmentName`, cleanup in `finally`
- [ ] Regression guard: full unit suite green (mandatory because `BindingsModule.cs` changed)
- [ ] MCP E2E documented as NOT in CI; manual run against a live stand recorded in the PR before story 5 → `done`
- [ ] Test naming follows `MethodName_ShouldExpectedBehavior_WhenCondition`; AAA + `because` on every assertion + `[Description]` on every test
- [ ] Smart-regression filter command included in each story's commit/PR description
- [ ] PR includes the changed test files in the changed-files list
