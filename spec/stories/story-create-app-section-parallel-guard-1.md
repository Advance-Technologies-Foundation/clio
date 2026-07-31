# Story 1: In-Process Serialization Guard (Option A)

**Feature**: create-app-section-parallel-guard
**FR coverage**: FR-01, FR-10, NFR-02, NFR-04
**PRD**: [prd-create-app-section-parallel-guard.md](../prd/prd-create-app-section-parallel-guard.md)
**ADR**: [adr-create-app-section-parallel-guard.md](../adr/adr-create-app-section-parallel-guard.md)
**Jira**: ENG-93089 (JAC-1)
**Status**: ready-for-dev
**Size**: M (half day)
**Depends on**: none (behavioral core — prerequisite for stories 3, 4, 5)
**Blocks**: story-create-app-section-parallel-guard-2, -3, -4, -5

---

## As a

developer building the create-app section toolkit

## I want

concurrent `create-app-section` inserts against the same environment + application to be serialized in-process by a keyed mutex around the insert→readback span

## So that

parallel section-create calls in one `clio` process stop contending server-side into a spurious `InsertQuery failed`, while creates against different apps/environments stay fully parallel

---

## Acceptance Criteria

- [ ] **AC-01** — Given two `CreateSection` calls for the **same** env + application-code running concurrently in one process, when both execute, then their `InsertQuery` POSTs do **not** overlap (serialized) — asserted via a recording `IApplicationClient` substitute (traces AC-01 / FR-01).
- [ ] **AC-02** — Given two `CreateSection` calls for **different** application-codes (or different environments), when both execute concurrently, then their insert POSTs **are** allowed to overlap (traces FR-10).
- [ ] **AC-03** — Given the guard's per-key `SemaphoreSlim.Wait(TimeSpan)` exceeds `waitTimeout`, when the wait times out, then the guard proceeds **without** the lock (best-effort), logs a warning, and does NOT throw (traces NFR-02, ADR Q2).
- [ ] **AC-04** — Given `work` throws, when `Run<T>` executes, then the semaphore is released in `finally` (no leak) and the exception propagates unchanged (traces NFR-02).
- [ ] **AC-05** — Given the key is built from `environmentName` + `applicationCode`, when the two differ only by case, then they map to the **same** semaphore (case-insensitive, `ToLowerInvariant()` on both parts joined by `␟`) (traces FR-10, ADR Q1).
- [ ] **AC-ERR** — Given the guard wraps the insert→readback span, when `work` completes normally, then the single-section uncontended path pays exactly one uncontended `Wait`/`Release` and returns the result unchanged (traces NFR-01 / CM-03).

## Implementation Notes

Create the process-wide singleton guard and wire it into the service's destructive span. Preparation reads stay **outside** the guard.

Key file (new): `clio/Command/ApplicationSectionCreateSerializationGuard.cs`
- `ISectionCreateSerializationGuard` + `SectionCreateSerializationGuard`.
- Instance field `ConcurrentDictionary<string, SemaphoreSlim>` (NOT `static` — singleton lifetime makes it process-shared; cleaner for CLIO005).
- Contract:
  ```csharp
  public interface ISectionCreateSerializationGuard {
      T Run<T>(string environmentName, string applicationCode, TimeSpan waitTimeout, Func<T> work);
  }
  ```
- Key: `$"{environmentName.ToLowerInvariant()}␟{applicationCode.ToLowerInvariant()}"`.
- Semaphore: `GetOrAdd(key, _ => new SemaphoreSlim(1, 1))`.
- Acquire with bounded `Wait(waitTimeout)`; on `false` (timeout) log a warning and run `work` unserialized; release in `finally` **only if** the wait actually succeeded.
- **Never evict** registry entries (documented rationale in XML doc — bounded by distinct env+app-code pairs; ref-counted removal introduces a TOCTOU race). Add `///` XML docs on the public API.

Wire into: `clio/Command/ApplicationSectionCreateCommand.cs`
- Inject `ISectionCreateSerializationGuard` into `ApplicationSectionCreateService` (extend the existing S107 constructor suppression note).
- Wrap **only** the insert→readback span in `guard.Run(environmentName, resolvedRequest.ApplicationCode, insertBudget, () => { ... insert + LoadCreatedSection/RecoverFromInsertTimeout ... })`.
- `insertBudget` = the resolved insert timeout for the call (90 s CLI / 600 s MCP background via `BackgroundInsertTimeoutMs`).
- Keep `GetApplicationInfo`, entity-schema existence probes, and code resolution **outside** the guard (fail fast in parallel — CM-01 / FR-04).

DI: `clio/BindingsModule.cs`
- `services.AddSingleton<ISectionCreateSerializationGuard, SectionCreateSerializationGuard>();` near the existing `IApplicationSectionCreateService` registration (~line 290). Singleton so the registry is process-wide; injected into the service so it is CLIO005-alive.

Pattern to follow: existing `IApplicationSectionCreateService` registration + constructor-injection pattern in `ApplicationSectionCreateCommand.cs`. Do NOT use `new` for the guard; no MediatR; no raw `HttpClient`.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | Same-key serialization (non-overlapping POSTs via recording `IApplicationClient` substitute); different-key overlap allowed; wait-timeout best-effort degrade + warning; release-in-finally on throw; case-insensitive key | `clio.tests/Command/SectionCreateSerializationGuardTests.cs` (new) |
| Unit `[Category("Unit")]` | Service wraps only insert→readback in the guard; prep reads stay outside | `clio.tests/Command/ApplicationSectionCreateServiceTests.cs` (existing) |

Test naming: `MethodName_ShouldExpectedBehavior_WhenCondition`. AAA + a `because` on every assertion + `[Description]` on every test. Prefer resolving the SUT from DI; use NSubstitute for `IApplicationClient`.

## Definition of Done

- [ ] Code compiles without Roslyn analyzer warnings (CLIO001-CLIO005); guard is DI-registered singleton and injected (CLIO005-alive, no `new`)
- [ ] Public API (`ISectionCreateSerializationGuard`) has `///` XML docs
- [ ] Semaphore released in `finally`; bounded wait; never-evict registry documented in XML
- [ ] No new CLI flags (guard is transparent); no raw `HttpClient` (NFR-03)
- [ ] Unit tests added with `[Category("Unit")]` — never `[Category("UnitTests")]`
- [ ] Regression filter run: `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=Command" --no-build` (also full suite because `BindingsModule.cs` changed — DI composition root)
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
