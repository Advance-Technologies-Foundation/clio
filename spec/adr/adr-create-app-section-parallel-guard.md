# ADR: create-app-section — Parallel-Contention Guard and Retryable-Rejection Recovery

**Status**: Proposed
**Author**: Architect Agent
**PRD**: [prd-create-app-section-parallel-guard.md](../prd/prd-create-app-section-parallel-guard.md)
**Jira**: ENG-93089
**Created**: 2026-07-10
**stepsCompleted**: [1, 2, 3, 4]

---

## Context

When an AI agent batches several `create-app-section` calls against ONE Creatio application, all
calls fail with the opaque body `{"success":false,"error":"InsertQuery failed."}` even though every
input is valid (confirmed repro: session `019e6982`, three calls within 52 ms → all fail in 36–55 s;
the same payloads re-run sequentially → both succeed in 93–102 s). Two independent defects compound
(both verified by reading `clio/Command/ApplicationSectionCreateCommand.cs` at master 8.1.0.69):

1. **Unguarded concurrency.** There is no serialization anywhere in `CreateSection`. The MCP tool
   `ApplicationSectionCreateTool.ApplicationSectionCreate` is an async, typed-response tool that does
   **not** derive from `BaseTool` and never takes `McpToolExecutionLock.SyncRoot`; it runs the
   synchronous `CreateSection` on `Task.Run(work, CancellationToken.None)` inside
   `McpProgressHeartbeat.RunWithProgressAndDeadlineAsync`, deliberately detached so the insert can
   commit after the response deadline. Consequently N concurrent tool calls in one long-lived
   `clio mcp-server` session execute `CreateSection` bodies concurrently on separate thread-pool
   threads, and their `InsertQuery` POSTs contend server-side and abort each other.
2. **Mis-classification.** `EnsureInsertSucceeded` turns the detail-less `success:false` body into an
   `ApplicationSectionCreateException` hard-coded to `FailureClass = ServerError`,
   `sectionCreated = false`, and `ServerErrorRetryGuidance` ("retrying … will most likely fail again").
   That is the exact opposite of what recovers: serialized retry succeeds. The detail-less body never
   reaches `ClassifyHttpStatus`, which already routes transient conditions to the retryable
   `CreatioTimeout` class.

A design decision is needed because the fix must (a) prevent the in-process contention, (b) recover
correctly across processes (the toolkit may spawn one `clio` process per call — OQ-02 unresolved),
and (c) not mask genuine rejections (duplicate code, missing entity, non-Latin caption) or duplicate a
section. This is a correctness bug, not only a UX nicety.

## Decision

Ship **Option A + C as the primary fix and Option B as the cross-process-robust complement** — all
three, because their combination is robust regardless of how the toolkit hosts the server (OQ-02):

- **A — In-process serialization** of the destructive create span, keyed by `environment + application-code`,
  via a new injected process-wide singleton guard holding a keyed-`SemaphoreSlim` registry. It uses a
  **synchronous, bounded** `Wait` (not `lock{}`, which cannot span the readback, and not async
  `WaitAsync`, which would force `CreateSection` async and ripple through the whole sync CLI path).
- **B — Reclassify + auto-retry-once-with-verify** for the detail-less `InsertQuery failed` body: add a
  new retryable failure class `Contention`, verify existence by the client-generated `Id` before any
  retry (reusing `TryVerifySectionExists`), retry the insert at most once, and keep any *detailed*
  rejection terminal `ServerError`. B recovers after contention regardless of which process caused it,
  so it is the cross-process mitigation for this ticket.
- **C — Documentation + guidance + mandatory E2E**: state the sequential-only constraint and the
  recover-by-serialize behavior on the tool `[Description]` and both MCP guidance guides, update the CLI
  docs, and add a `clio.mcp.e2e` concurrent-create scenario.

Cross-process serialization at the cliogate/DB level is **explicitly out of scope** for ENG-93089
(OQ-01) — see Decision 5.

## Alternatives Considered

| Option | Pros | Cons | Status |
|--------|------|------|--------|
| A only (in-process guard) | Fixes the dominant single-server batch repro deterministically; no server-side change | Does not serialize two separate `clio` processes; if OQ-02 shows per-call processes, A alone misses the repro | Rejected as sole fix — **adopted as one pillar** |
| B only (reclassify + verify-retry) | Robust across processes/agents; recovers after contention regardless of cause; fixes the wrong-guidance defect | Adds failure-path latency; contention still happens (only recovered after the fact); relies on `Id`-verify to avoid duplicates | Rejected as sole fix — **adopted as one pillar** |
| C only (docs/guidance) | Cheapest; discourages batching; locks in regression coverage | Guidance does not enforce anything; agents still batch | Rejected as sole fix — **adopted as one pillar** |
| Server-side / cliogate / DB-level serialization | The only thing that fully solves cross-process contention | New privileged endpoint + DB lock design; high cost/risk; large blast radius; unproven necessity for this repro | Rejected for this ticket (deferred, OQ-01) |
| **A + B + C (chosen)** | A kills the documented in-process repro; B recovers correctly even across processes and fixes the guidance defect; C prevents batching and guarantees coverage — robust for either answer to OQ-02 | Two mechanisms to maintain; guard adds a small keyed-lock surface | **Chosen** |

**Why over the alternatives:** the repro topology (OQ-02) is unresolved. Betting on A alone fails if
processes are per-call; betting on B alone leaves avoidable contention and latency on the hot path.
A+B+C is the only combination that is correct whether the toolkit hosts one long-lived server or one
process per call, while B independently fixes the misleading `ServerError` guidance that AC-02 requires.

---

## Detailed Design (answers the 6 mandatory questions)

### Q1 — Where the serialization lives, the primitive, key, lifetime, cleanup

**In the service (`ApplicationSectionCreateService.CreateSection`), not the MCP tool.** FR-01 and the
CLI-user story require both the CLI verb (`CreateAppSectionCommand`) and the MCP tool to be covered, and
both funnel through `CreateSection`. Guarding the tool only would leave the CLI insert unprotected.

Because the service is `AddTransient` and is injected **directly** (startup-time) into
`ApplicationSectionCreateTool` — not resolved per-environment via `IToolCommandResolver` — an instance
field `SemaphoreSlim` would not be shared across service instances or across the CLI/MCP surfaces. The
serialization primitive is therefore a **process-wide singleton guard** owning a keyed registry:

- New behavior type (interface + impl, DI-registered — see Decision "DI/wiring"):
  `ISectionCreateSerializationGuard` / `SectionCreateSerializationGuard`, registered
  **`AddSingleton`** so its one instance (and thus its registry) is process-wide. The registry is a
  plain instance field `ConcurrentDictionary<string, SemaphoreSlim>` — the singleton lifetime makes it
  process-shared without a `static` field (cleaner for CLIO005; the guard is injected into the service,
  so it is a live registration).
- **Key:** `$"{environmentName}␟{applicationCode}"` normalized with
  `StringComparer.OrdinalIgnoreCase` (build the key by lower-casing both parts with
  `ToLowerInvariant()`), i.e. **environment name + application code, case-insensitive** (FR-10).
  Different apps or different environments get different keys and stay fully parallel.
- **Semaphore:** `GetOrAdd(key, _ => new SemaphoreSlim(1, 1))` — a per-key mutex.
- **Lifetime / cleanup:** entries are **never evicted** (documented rationale, NFR-02): the entry count
  is bounded by the number of distinct `env + app-code` pairs touched in a process (tens at most, each
  `SemaphoreSlim` is a few dozen bytes), and ref-counted removal introduces a TOCTOU race between
  `Release` and the next `GetOrAdd`. This is the standard keyed-lock pattern; the memory is negligible
  and bounded, so never-remove is correct and safe.

**Critical section scope:** acquire **immediately before** the destructive
`client.ExecutePostRequest(Insert…)` and release in a `finally` **after** `LoadCreatedSection` /
`RecoverFromInsertTimeout` completes. The section-generation work the readback poll waits for is part of
the ~93–102 s span that the evidence shows must complete before the next insert starts (the successful
sequential run began only after the first *finished*). The preparation reads
(`GetApplicationInfo`, entity-schema existence probes, code resolution) stay **outside** the guard so
invalid inputs fail fast in parallel (CM-01 / FR-04) and the uncontended single-section happy path pays
only one uncontended `Wait`/`Release` (NFR-01 / CM-03).

### Q2 — Sync vs async wait, deadlock/heartbeat safety, cancellation

**Synchronous, bounded `SemaphoreSlim.Wait(TimeSpan)`.** `CreateSection` is synchronous and, on the MCP
path, already runs on a dedicated detached thread-pool thread via `Task.Run(work, CancellationToken.None)`.
A synchronous `Wait` on that dedicated thread is the natural fit and blocks a thread that is already
committed to this background work; making `CreateSection` async (`WaitAsync`) would force an async ripple
through the synchronous CLI command path and the whole service for no benefit. For a batch of N the guard
parks at most N background thread-pool threads (repro N=3); the pool grows and the parked threads are
released deterministically in `finally`.

- **No deadlock with the heartbeat/deadline:** the heartbeat pump runs on a *separate* task; the guard
  wait is on the work task only. The response-deadline mechanism does not cancel the work (it returns
  `in-progress` to the client and lets the work finish), so waiting on the guard cannot deadlock it —
  the client already has its `in-progress` envelope and polls `list-app-sections`.
- **Cancellation:** the guard wait deliberately does **not** honor the MCP response deadline/cancellation
  token. The work is intentionally detached on `CancellationToken.None` so the section commits in the
  background; cancelling the guard wait on the deadline would defeat the very serialization we want, and
  the service signature does not carry the token anyway. Threading a token through is unnecessary and is
  explicitly rejected.
- **Wait timeout (bounded, no indefinite block — NFR-02):** the guard `Wait` timeout equals the
  resolved insert budget for the call (`insertTimeoutMs` — 90 s on the CLI path, 600 s on the MCP
  background path via `BackgroundInsertTimeoutMs`). **On wait-timeout the guard degrades to best-effort:
  it proceeds WITHOUT the lock and logs a warning** (it does not fail). A deep queue therefore never
  becomes a hard failure — any resulting contention is caught and recovered by Option B. This keeps the
  guard from ever introducing a new failure mode.

### Q3 — Retry semantics for B (count, backoff, budget composition, retryable-vs-terminal predicate)

- **Count:** exactly **one** auto-retry (Jira "once").
- **Backoff:** a short **fixed** 2 s delay before the retry (reuse the existing `PollDelay`), no jitter.
  Jitter is unnecessary because Option A already removes the in-process trigger; the retry is the
  cross-process safety net and the contending insert has, per the evidence, already aborted (36–55 s)
  by the time we observe the failure.
- **Retryable-vs-terminal predicate (the precise rule):** in the parsed `InsertQueryResponseDto` with
  `Success == false`, let `msg = response.ErrorInfo?.Message?.Trim()`:
  - `msg` is null/empty **OR** `string.Equals(msg, "InsertQuery failed.", OrdinalIgnoreCase)` (also
    accept the message without the trailing period) ⇒ **detail-less ⇒ retryable `Contention`**.
  - any other non-empty `msg` (e.g. contains "already exists", "already bound", a real column/constraint
    detail) ⇒ **terminal `ServerError`** (FR-04, unchanged message/guidance).
  - Non-JSON / empty response bodies remain terminal `ServerError` (unchanged).
- **Verify-before-retry (FR-03, AC-03, CM-02):** on a `Contention` classification, call
  `TryVerifySectionExists` (already `Id`-matched, bounded to `VerificationTimeoutMs = 30 s`):
  - returns `true` ⇒ the section committed despite the aborted response ⇒ **do not retry**; return
    `LoadCreatedSection(...)` readback (no duplicate, no "already bound").
  - returns `false` ⇒ safe to retry ⇒ wait 2 s, run **one** more insert attempt; classify its result the
    same way.
  - returns `null` (verification itself failed) ⇒ **do not auto-retry blindly**; throw the classified
    `Contention` exception with `sectionCreated = null` and guidance to serialize/retry after checking
    `list-app-sections`.
  - If the single retry also yields a detail-less rejection ⇒ throw the classified `Contention` exception
    (retryable guidance, `sectionCreated` = last verify outcome). No unbounded loop (FR-05).
- **Budget composition (FR-05 / A-04):** worst case adds one verify (≤30 s) + 2 s + one insert. On the
  MCP background path the work already runs past the response deadline (client got `in-progress` and
  polls), so the retry never breaches the client's ~180 s ceiling. On the synchronous CLI path there is
  no client ceiling; total is bounded (≈ insert + 30 s + 2 s + insert) and finite. With Option A active,
  the retry rarely fires at all in the single-process case.

### Q4 — Classification change (new class, wire value, guidance, backward compatibility)

Introduce a new retryable class rather than overloading `CreatioTimeout` (it was a rejection, not a
timeout — reusing `creatio-timeout` would be semantically wrong and would misdirect guidance):

- `enum ApplicationSectionCreateFailureClass` (public): add member **`Contention`**. Adding an enum
  member is backward compatible; existing consumers that switch on the three known values are unaffected
  (the wire string is what agents read).
- `ApplicationSectionCreateFailureClassExtensions.ToWireValue`: add
  `ApplicationSectionCreateFailureClass.Contention => "contention"` (kebab-case wire value on the MCP
  `error-class` envelope). Keep the `_ => "server-error"` default so an unmapped value never leaks a
  non-kebab token.
- New guidance constant `ContentionRetryGuidance` (user-friendly, no stack traces), e.g.:
  > "Creatio aborted the section insert without a detailed reason, which is what happens when several
  > sections are created in the same application at once. No section was created (verified). Create
  > sections in this application **one at a time** (sequentially, not in parallel); clio also retries
  > this once automatically. If it recurs, wait a few seconds, run `list-app-sections` to confirm the
  > section is absent, then retry `create-app-section`."
- The `ApplicationSectionCreateException` type is unchanged (it already carries `FailureClass`,
  `SectionCreated`, `RetryGuidance`); a `Contention` failure sets `sectionCreated` to the verify outcome
  (`false` when verified absent, `null` when verification failed). The MCP envelope
  (`error-class`/`section-created`/`retry-guidance`) surfaces it with no envelope-shape change.

### Q5 — Cross-process limitation (explicit decision)

A singleton/process-wide `SemaphoreSlim` serializes only within **one** `clio` process. If the toolkit's
`scripts/mcp_client.py` spawns a fresh `clio mcp-server` per call (OQ-02, unconfirmed), Option A does not
serialize two separate processes or two agents against the same app. **Decision:** for ENG-93089,
**Option B (verify + reclassify + bounded retry) is the accepted cross-process mitigation**, and
cliogate/DB-level serialization is **OUT OF SCOPE** (OQ-01, deferred to a follow-up).
**Rationale:** B recovers correctly after contention regardless of which process caused it (it verifies
by `Id` and retries safely), so it delivers AC-01/AC-02 across processes without the cost and risk of a
new privileged cliogate endpoint plus a DB-lock/idempotency design. If a low-cost server-side option is
later proven necessary (OQ-02 shows per-call processes AND B proves insufficient under load), it is a
separate ticket. This is recorded as a deliberate decision, not a hand-wave.

### Q6 — MCP surface + docs impact (mandatory repo policy)

Files that MUST change (impl + MCP + docs + tests) are enumerated in the Implementation Plan below. No
separate `McpToolDescriptions.cs` exists — the tool descriptions are inline `[Description]` attributes in
`ApplicationTool.cs`. No guidance guide is added or renamed, so the routing map
(`Resources/RoutingGuidanceResource.cs`) does **not** change ("MCP reviewed: routing map unaffected").

---

## Implementation Plan

### Files to create

| File | Purpose |
|------|---------|
| `clio/Command/ApplicationSectionCreateSerializationGuard.cs` | `ISectionCreateSerializationGuard` + `SectionCreateSerializationGuard`: process-wide keyed-`SemaphoreSlim` registry; exposes a `Run<T>(string environmentName, string applicationCode, TimeSpan waitTimeout, Func<T> work)` wrapper that acquires the per-key mutex (bounded `Wait`, best-effort degrade on timeout with a warning) and releases in `finally`. Public API gets `///` XML docs. |

### Files to modify

| File | Change description |
|------|-------------------|
| `clio/Command/ApplicationSectionCreateFailure.cs` | Add `Contention` enum member (with `///` doc) and `Contention => "contention"` in `ToWireValue`. |
| `clio/Command/ApplicationSectionCreateCommand.cs` | Inject `ISectionCreateSerializationGuard` (extend the existing S107 suppression note); wrap the insert→readback span in `guard.Run(environmentName, resolvedRequest.ApplicationCode, insertBudget, …)`; refactor `EnsureInsertSucceeded` so the detail-less body returns a **retryable** signal instead of always throwing `ServerError`; add the verify-once-then-retry-once loop around the insert using `TryVerifySectionExists`; add `ContentionRetryGuidance`. Keep detailed rejections terminal `ServerError`. No raw `HttpClient` (NFR-03); no bare `catch(Exception)` beyond existing classified handlers. |
| `clio/BindingsModule.cs` | `services.AddSingleton<ISectionCreateSerializationGuard, SectionCreateSerializationGuard>();` (near the existing `IApplicationSectionCreateService` registration, line ~290). Singleton so the registry is process-wide; injected into the service, so CLIO005-alive. |
| `clio/Command/McpServer/Tools/ApplicationTool.cs` | Update `ApplicationSectionCreate` tool `[Description]`: add the sequential-only constraint and add `contention` to the `error-class` enumeration `(transport \| creatio-timeout \| server-error \| contention)`; note "clio serializes in-process and auto-retries a detail-less InsertQuery rejection once with verification — do not manually blast parallel create-app-section calls." Update the args `[Description]` detail-less-rejection sentence to mention contention/sequential. |
| `clio/Command/McpServer/Resources/AppModelingGuidanceResource.cs` | Update the `create-app-section` error-class bullet (line ~92) to include `contention` (retryable — serialize/retry); add a "create sections in one app sequentially, not in parallel" guardrail bullet. |
| `clio/Command/McpServer/Resources/ExistingAppMaintenanceGuidanceResource.cs` | Same edits on its error-class bullet (line ~51): add `contention` and the sequential-only note. |
| `clio/help/en/create-app-section.txt` | Add a "Concurrency" note: create sections sequentially per app; describe the `contention` error-class and the automatic verify+retry. |
| `clio/docs/commands/create-app-section.md` | Same sequential-only + error-class documentation (GitHub docs). |
| `clio/Commands.md` | Reflect the sequential-only constraint / new `contention` class in the command's section. |
| `docs/McpCapabilityMap.md` | Update the `create-app-section` row to note serialization + `contention` error-class. |

### Key interfaces / contracts

```csharp
// New behavior type (DI-registered singleton; injected into ApplicationSectionCreateService):
public interface ISectionCreateSerializationGuard {
    /// <summary>Runs <paramref name="work"/> under a process-wide mutex keyed by
    /// environment + application code (case-insensitive). The wait is bounded by
    /// <paramref name="waitTimeout"/>; on timeout the work runs unserialized (best-effort).</summary>
    T Run<T>(string environmentName, string applicationCode, TimeSpan waitTimeout, Func<T> work);
}

// Failure enum gains one retryable member (public API, additive/back-compatible):
public enum ApplicationSectionCreateFailureClass { Transport, CreatioTimeout, ServerError, Contention }
// ToWireValue: Contention => "contention"
```

### CLI flag specification

No new flag. The guard and verify+retry are transparent default behavior (PRD "CLI Impact"). If a tuning
knob is later required (OQ-04), it MUST be kebab-case (e.g. `--section-create-max-retries`) with a
default preserving current behavior — **not** in scope for this ADR. All existing flags stay kebab-case
(CLIO001).

### Test strategy

| Layer | Framework | What to cover | File |
|-------|-----------|---------------|------|
| Unit | NUnit / FluentAssertions / NSubstitute | Detail-less `InsertQuery failed` ⇒ `error-class=contention`, retryable (AC-02); detailed rejection ("already exists") stays `server-error`, non-retryable (AC-04); verify returns `true` ⇒ readback, no second insert POST (AC-03/CM-02); verify `false` ⇒ exactly one retry insert; verify `null` ⇒ no auto-retry, throws `Contention` with guidance; retry bounded to one (FR-05); `ToWireValue(Contention) == "contention"`; guard serializes two concurrent `CreateSection` calls for same env+app (assert non-overlapping insert POSTs via a recording `IApplicationClient` substitute) and allows overlap for different app-codes (FR-10); guard best-effort degrade on wait-timeout | `clio.tests/Command/ApplicationSectionCreateServiceTests.cs` (+ a new `SectionCreateSerializationGuardTests.cs`, `[Category("Unit")]`, `Module=Command`) |
| Unit (content) | NUnit / FluentAssertions | Tool `[Description]` and both guidance guides contain the sequential-only text and the `contention` error-class (AC-06) | `clio.tests/Command/McpServer/*` (existing description/guidance content-test fixture) |
| E2E | `clio.mcp.e2e` (real `clio mcp-server`) | N concurrent `create-app-section` calls against one seeded app ⇒ no `contention`/`InsertQuery failed` failure, all sections created (AC-01/AC-05); retry/serialization recovery path exercised | `clio.mcp.e2e/ApplicationSectionToolE2ETests.cs` (new `[Category("McpE2E.Sandbox")]` test, guarded by `AllowDestructiveMcpTests` + seeded `EnvironmentName`, mirroring the existing sandbox tests) |

All unit tests: `[Category("Unit")]` (never `UnitTests`), `MethodName_ShouldExpectedBehavior_WhenCondition`,
AAA with a `because` on every assertion and a `[Description]` on every test; command tests prefer
`BaseCommandTests<TOptions>` and resolve the SUT from DI. **MCP E2E is NOT in CI** — manual/release
execution only; flag this in the test plan.

## Consequences

- **Positive:** eliminates the documented in-process parallel-batch failure (A); recovers correctly
  after contention even across processes and fixes the misleading "do not retry" guidance (B); prevents
  agents from batching and guarantees regression coverage (C); no envelope-shape or CLI-flag change; the
  uncontended single-section happy path pays only one uncontended `Wait`/`Release`.
- **Trade-offs:** two mechanisms to maintain; the guard parks up to N background thread-pool threads for
  a batch of N (bounded, released in `finally`); the keyed-`SemaphoreSlim` registry is never evicted
  (bounded, tiny — justified); a wait-timeout degrades to best-effort unserialized execution (recovered
  by B); does not serialize across separate `clio` processes (accepted; B is the mitigation; cliogate/DB
  serialization deferred — OQ-01).
- **Breaking change:** No. `ApplicationSectionCreateFailureClass` gains an additive `Contention` member
  and a new `contention` wire value; the exception type and MCP envelope shape are unchanged. Not a
  breaking change, so no `RELEASE.md` migration entry is required (a normal release note is appropriate).

## Open questions carried from the PRD

- **OQ-01 / Q5:** cliogate/DB-level cross-process serialization — decided OUT OF SCOPE here (deferred).
- **OQ-02:** exact toolkit hosting topology (long-lived vs per-call `clio mcp-server`) — the A+B+C
  combination is correct either way, so this ADR does not block on it; A covers the long-lived case, B
  covers the per-call case.
- **OQ-03:** whether the server can distinguish contention-abort from a real collision in the response —
  mitigated by the detail-less-vs-detailed predicate (Q3) plus `Id`-verify (Q4); if the server later
  emits a distinguishing detail, tighten the predicate.
- **OQ-04:** tuning flag — not added; fixed internal defaults (retry=1, backoff=2 s, wait=insert budget).

## Pre-implementation Checklist

- [ ] No new CLI options (guard/retry are transparent); any future knob is kebab-case (CLIO001).
- [ ] New `ISectionCreateSerializationGuard` registered `AddSingleton` in `BindingsModule.cs`; injected
      into `ApplicationSectionCreateService` (CLIO005-alive; no `new` for behavior — CLIO001).
- [ ] No raw `HttpClient` — all Creatio calls stay on `IApplicationClient` (NFR-03).
- [ ] No bare `catch(Exception)` introduced; classified handlers only.
- [ ] Public API (`ISectionCreateSerializationGuard`, `Contention`, guidance) has `///` XML docs.
- [ ] Guard releases the semaphore in `finally`; bounded wait; never-evict registry documented.
- [ ] MCP surface updated: tool `[Description]` + both guidance guides; routing map unaffected
      (stated). Docs updated: help/txt, docs/commands md, `Commands.md`, `McpCapabilityMap.md`.
- [ ] `clio.mcp.e2e` concurrent-create scenario added (AC-05); noted as not-in-CI.
- [ ] Existing tests reviewed: `clio.tests/Command/ApplicationSectionCreateServiceTests.cs` (the
      current `ServerError`-classification expectations for the detail-less body must be updated to
      `Contention`).
