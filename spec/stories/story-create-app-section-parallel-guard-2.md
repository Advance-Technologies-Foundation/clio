# Story 2: Contention Classification + Retry-With-Verify (Option B)

**Feature**: create-app-section-parallel-guard
**FR coverage**: FR-02, FR-03, FR-04, FR-05, NFR-04
**PRD**: [prd-create-app-section-parallel-guard.md](../prd/prd-create-app-section-parallel-guard.md)
**ADR**: [adr-create-app-section-parallel-guard.md](../adr/adr-create-app-section-parallel-guard.md)
**Jira**: ENG-93089 (JAC-1)
**Status**: ready-for-dev
**Size**: L (full day)
**Depends on**: story-create-app-section-parallel-guard-1 (edits the same `CreateSection` span; guard wrap must land first)
**Blocks**: story-create-app-section-parallel-guard-3, -4, -5

---

## As a

AI no-code agent (MCP client) that hit the detail-less `InsertQuery failed`

## I want

that rejection classified as a **retryable** `contention` failure with serialize/retry guidance, and clio to auto-verify-then-retry once by the client-generated section `Id` before advising me

## So that

I recover the way that actually works (serialize/retry) instead of abandoning on the current wrong "do not retry" `ServerError` guidance — and never create a duplicate section

---

## Acceptance Criteria

- [ ] **AC-01** — Given a parsed `InsertQueryResponseDto` with `Success == false` and `msg` (trimmed `ErrorInfo?.Message`) that is null/empty **OR** equals `"InsertQuery failed."` (with or without trailing period, `OrdinalIgnoreCase`), when classified, then `error-class` is `contention` (retryable) and `retry-guidance` = the new serialize/retry text (traces PRD AC-02 / FR-02).
- [ ] **AC-02** — Given a detailed rejection (`msg` contains e.g. "already exists" / "already bound" / a real constraint detail), when classified, then it stays terminal `ServerError`, non-retryable, `section-created = false`, with the existing actionable message (traces PRD AC-04 / FR-04).
- [ ] **AC-03** — Given a `Contention` classification and `TryVerifySectionExists` returns `true`, when recovery runs, then the operation returns `LoadCreatedSection(...)` readback and issues **no** second insert POST — no duplicate, no "already bound" (traces PRD AC-03 / CM-02 / FR-03).
- [ ] **AC-04** — Given `TryVerifySectionExists` returns `false`, when recovery runs, then clio waits the fixed 2 s `PollDelay` and issues **exactly one** more insert attempt, classifying its result the same way (traces FR-05).
- [ ] **AC-05** — Given `TryVerifySectionExists` returns `null` (verification itself failed), when recovery runs, then clio does **not** auto-retry and throws the classified `Contention` exception with `sectionCreated = null` and serialize/retry guidance (traces FR-03, FR-05).
- [ ] **AC-06** — Given the single retry also yields a detail-less rejection, when recovery completes, then clio throws the classified `Contention` exception (retryable guidance, `sectionCreated` = last verify outcome) with **no** unbounded loop (traces FR-05).
- [ ] **AC-07** — Given `ToWireValue`, when called with `ApplicationSectionCreateFailureClass.Contention`, then it returns `"contention"`; unmapped values still default to `"server-error"` (kebab-case wire value).
- [ ] **AC-ERR** — Given invalid input, when `create-app-section` runs, then clio prints `Error: {message}` and exits non-zero (unchanged), and the classified failure carries `error-class` / `section-created` / `retry-guidance` on the MCP envelope.

## Implementation Notes

Add the retryable class and the reclassify → verify → retry-once loop. Reuse the existing `TryVerifySectionExists` (Id-matched, `VerificationTimeoutMs = 30 s`) infrastructure — do NOT alter the `CreatioTimeout` recovery flow beyond reusing it.

Key file: `clio/Command/ApplicationSectionCreateFailure.cs`
- Add public enum member `Contention` (with `///` doc) to `ApplicationSectionCreateFailureClass`.
- Add `ApplicationSectionCreateFailureClass.Contention => "contention"` to `ApplicationSectionCreateFailureClassExtensions.ToWireValue`; keep `_ => "server-error"` default.

Key file: `clio/Command/ApplicationSectionCreateCommand.cs`
- Refactor `EnsureInsertSucceeded` so the **detail-less** body returns a retryable signal (classified `Contention`) instead of always throwing `ServerError`. Predicate (ADR Q3):
  - `msg` null/empty OR `string.Equals(msg, "InsertQuery failed.", OrdinalIgnoreCase)` (also accept without trailing period) ⇒ **retryable `Contention`**.
  - any other non-empty `msg` ⇒ **terminal `ServerError`** (unchanged message/guidance).
  - non-JSON / empty response body ⇒ terminal `ServerError` (unchanged).
- Add the verify-once-then-retry-once loop inside the guarded span (from story 1):
  - `Contention` ⇒ `TryVerifySectionExists` by client-generated `Id`: `true` ⇒ readback (no retry); `false` ⇒ wait 2 s (`PollDelay`) + one insert; `null` ⇒ throw `Contention`, `sectionCreated = null`.
  - Bound to exactly one retry (Jira "once"); if the retry is also detail-less, throw `Contention`.
- Add `ContentionRetryGuidance` constant (user-friendly, no stack trace) per ADR Q4 wording (aborted-without-detail, no section created/verified, create one-at-a-time, clio retries once, else wait + `list-app-sections` + retry).
- Set `sectionCreated` on a `Contention` exception to the verify outcome (`false` verified-absent / `null` verify-failed).
- `ApplicationSectionCreateException` type is unchanged; MCP envelope shape unchanged. No bare `catch(Exception)`; classified handlers only.

**Flip existing tests:** the current `ApplicationSectionCreateServiceTests` expectations that assert the detail-less body ⇒ `ServerError` MUST be updated to expect `Contention` (ADR pre-impl checklist).

Pattern to follow: existing `ClassifyHttpStatus` + `RecoverFromInsertTimeout` / `TryVerifySectionExists` recovery already in the service.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | Detail-less ⇒ `contention` retryable (AC-01); detailed ⇒ `server-error` terminal (AC-02); verify `true` ⇒ readback, no 2nd POST (AC-03); verify `false` ⇒ exactly one retry (AC-04); verify `null` ⇒ no retry, throw with `sectionCreated=null` (AC-05); retry-also-detail-less ⇒ throw, bounded (AC-06); `ToWireValue(Contention)=="contention"` (AC-07) | `clio.tests/Command/ApplicationSectionCreateServiceTests.cs` (existing — FLIP `ServerError`→`Contention` expectations + add cases) |

Test naming: `MethodName_ShouldExpectedBehavior_WhenCondition`. AAA + `because` on every assertion + `[Description]` on every test; NSubstitute recording `IApplicationClient` to assert POST counts.

## Definition of Done

- [ ] Code compiles without Roslyn analyzer warnings (CLIO001-CLIO004)
- [ ] Enum member additive/back-compatible; `contention` wire value is kebab-case; guidance constant has no stack trace
- [ ] Existing `ServerError`-for-detail-less expectations flipped to `Contention`
- [ ] Retry strictly bounded to one; no raw `HttpClient`; no bare `catch(Exception)`
- [ ] Unit tests added with `[Category("Unit")]` — never `[Category("UnitTests")]`
- [ ] Regression filter run: `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=Command" --no-build`
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
