# Story 2: Fix the multi-instance test harness before any new test is offered as evidence

**Feature**: eng-99856-multi-instance
**FR coverage**: FR-19
**AC coverage**: AC-18
**PRD**: [prd-eng-99856-multi-instance.md](../prd/prd-eng-99856-multi-instance.md)
**ADR**: [adr-eng-99856-multi-instance.md](../adr/adr-eng-99856-multi-instance.md) — *Files to modify*, `SubProcessTestSupport.cs`
**Platform facts**: [eng-99856-multi-instance-platform-facts.md](../eng-99856-multi-instance/eng-99856-multi-instance-platform-facts.md) — §8.1
**Jira**: ENG-99856
**Status**: ready-for-dev
**Size**: M (half day — the helper is shared with the single-instance suites, so the blast radius is the
whole sub-process fixture family, not just the multi-instance tests)
**Repo**: crt-process-builder — `tests/UnitTests/CrtProcessBuilder.Tests/`
**Depends on**: nothing
**Blocks**: stories 3, 4, 5, 6, 7, 8, 9 — every story that offers a unit test as evidence

---

## Why this is early and not last

`SubProcessTestSupport.AParameterOn` hardcodes
`DataValueType = schema.DataValueTypeManager.GetInstanceByName("Text")` (`:265`), and `MakeMultiInstance`
(`:211-233`) builds **both collections and all three counters** through it. Every existing multi-instance
test in the package therefore runs over an element whose collections are not collections and whose
counters are not integers.

That matters because the type is exactly what the feature validates on and what the platform degrades on:
`DataValueTypeUId == CompositeObjectList` is one of only two things the applier will check (story 3), and
four run-time paths degrade **silently** on a wrong type rather than throwing
(`BaseFlowSchemaGenerator.cs:465-489`, `ProcessInstanceParametersDataReader.cs:471-489`,
`ProcessInstanceParameterStore.cs:219-233`, `ProcessGenerator.cs:94-97`). A suite built on `Text`
collections cannot fail the way production fails, so any evidence resting on it is weaker than it looks.

## As a

QA engineer reviewing the evidence for this feature

## I want

the shared sub-process test helper to build correctly typed parameters

## So that

no assertion in the multi-instance suite rests on a collection that is not a collection, and the tests the
later stories add mean what they claim

---

## Acceptance Criteria

- [ ] **AC-01** — Given `AParameterOn`, when it is called, then the data value type is a **parameter of the
  helper** with an explicit default, not a hardcoded `"Text"`; existing call sites that genuinely want text
  keep text by passing it or by the default, and that choice is stated in the helper's XML doc.
- [ ] **AC-02** — Given `MakeMultiInstance`, when it builds a fixture element, then `InputRecordCollection`
  and `OutputRecordCollection` carry `DataValueTypeUId == {651EC16F-D140-46DB-B9E2-825C985A8AC2}`
  (`CompositeObjectList`) and the three counters carry `{6B6B74E2-820D-490E-A017-2B73D4CCF2B0}` (`Integer`).
- [ ] **AC-03** — Given the fixture, when directions are inspected, then `InputRecordCollection` is `In`,
  `OutputRecordCollection` is `Out` and the three counters are `Out` — the shipped shape
  (61/61 in the corpus; `L12=0` on input, `L12=1` on output and the counters).
- [ ] **AC-04** — Given a guard test in the harness's own fixture, when it runs, then it asserts the five
  parameter types directly, so a future edit that re-introduces `Text` reddens a named test rather than
  silently weakening every other one.
- [ ] **AC-05** — Given the existing sub-process suites (single- and multi-instance), when they run after
  the change, then every test that changes result is listed in the PR description with one line saying
  whether it was **wrong before** or is **wrong now** — a test that only passed because its collection was
  `Text` is a finding, not noise to be silenced.
- [ ] **AC-06** — Given the helper, when it builds the five parameters, then it does **not** stamp
  `MultiInstanceOptions` UIds or assign `SchemaUId` in an order the platform forbids; the fixture stays a
  data builder, and the forced construction order is the applier's job (story 3).
- [ ] **AC-ERR** — Given a data value type name the platform's `DataValueTypeManager` cannot resolve, when
  the helper is called with it, then it fails loudly at fixture-build time with the name in the message —
  never falls back to a default type.

## Implementation Notes

Key file: `tests/UnitTests/CrtProcessBuilder.Tests/SubProcessTestSupport.cs` — `AParameterOn` at `:265`,
`MakeMultiInstance` at `:211-233`.

Pattern to follow: the helper is a test-support data builder, so a plain `new` is fine here — CLIO001's
no-`new`-for-behaviour rule governs behaviour-bearing classes in clio, and this file is neither in clio nor
behaviour. Do not introduce an interface for it.

Type UIds (platform facts §2): `CompositeObjectList {651EC16F-D140-46DB-B9E2-825C985A8AC2}`,
`Integer {6B6B74E2-820D-490E-A017-2B73D4CCF2B0}`, `CompositeObject {632E4371-0A7F-46CD-A284-A623B3933027}`
(distinct from the list — do not confuse them).

Two properties of the platform the fixture must not contradict:

- `BP2` order is **not** canonical: 23 of the 61 shipped elements are in client order (input, output, then
  counters) and 38 in server order (counters, input, output). Assert on names/UIds, never on position.
- Presence of all five is not required by the platform — 4 of the 61 shipped elements carry only two root
  parameters — so keep a variant of the helper that builds **only** the two collections, for story 3's
  FR-09 coverage.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` (package) | the five parameter types and directions produced by `MakeMultiInstance`; the two-collection variant; the loud failure on an unresolvable type name | `tests/UnitTests/CrtProcessBuilder.Tests/SubProcessTestSupportTests.cs` |
| Unit `[Category("Unit")]` (package) | the existing sub-process suites re-run unchanged; any result change triaged in the PR | existing fixtures |

Test naming: `MakeMultiInstance_ShouldTypeCollectionsAsCompositeObjectList_WhenBuildingFixtureElement`.

## Definition of Done

- [ ] `AParameterOn` no longer hardcodes a type; the default is explicit and documented
- [ ] `MakeMultiInstance` produces `CompositeObjectList` collections and `Integer` counters (AC-18)
- [ ] A guard test pins the five types so the defect cannot return unnoticed
- [ ] Every existing test whose result changed is listed and triaged in the PR description
- [ ] Tests: AAA with explicit Arrange/Act/Assert, a `because` on every assertion, `[Description]` on every
      method, `[Category("Unit")]` — never `[Category("UnitTests")]`
- [ ] No new `CLIO*` diagnostics — vacuous here, the CLIO analyzers run in the clio repo only; state that
      in the PR rather than leaving it unsaid
- [ ] No new CLI flag (kebab-case rule vacuous — this feature adds no CLI surface at all)
- [ ] Validated locally: `dotnet test tests/UnitTests/CrtProcessBuilder.Tests/CrtProcessBuilder.Tests.csproj -c dev-nf`,
      run in the **main checkout, not a git worktree**
- [ ] PR description references this story file and states the before/after test count

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Tests whose result changed (and why):
- Notes:
