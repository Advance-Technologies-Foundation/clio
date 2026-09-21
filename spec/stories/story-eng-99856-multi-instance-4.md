# Story 4: `executionMode` and `ignoreErrors` — string-only, default-suppressed, updated in place

**Feature**: eng-99856-multi-instance
**FR coverage**: FR-04, FR-05
**AC coverage**: AC-05, AC-06, part of AC-ERR
**PRD**: [prd-eng-99856-multi-instance.md](../prd/prd-eng-99856-multi-instance.md)
**ADR**: [adr-eng-99856-multi-instance.md](../adr/adr-eng-99856-multi-instance.md) — OQ-06
**Contract**: [eng-99856-multi-instance-contract-answers.md](../eng-99856-multi-instance/eng-99856-multi-instance-contract-answers.md) — Q5
**Platform facts**: [eng-99856-multi-instance-platform-facts.md](../eng-99856-multi-instance/eng-99856-multi-instance-platform-facts.md) — §1.4, §1.5, §5, §6
**Jira**: ENG-99856
**Status**: ready-for-dev
**Size**: M (half day)
**Repo**: crt-process-builder — `packages/CrtProcessBuilder/Files/src/cs/`
**Depends on**: stories 2, 3
**Blocks**: stories 11, 12

---

## As a

Creatio developer editing a multi-instance element

## I want

to set the execution mode and the error policy by name, and to change one of them without touching the
other

## So that

I can move an element to Parallel or make it ignore errors without de-converting and re-converting it, and
without being silently given the mode I did not ask for

---

## Owner decision — SETTLED

**OQ-06 — in-place update of `executionMode` / `ignoreErrors` on an already-multi-instance element.**
**DECIDED by the owner 2026-09-21: supported IN PLACE.** `enabled: true` on an element that already
carries `MultiInstanceOptions` updates the mode fields and does not re-convert. Omitted on update means
"left as is, never reset"; omitted on create means Sequential / false, both suppressed at their defaults.
A different collection source in the same call is still refused — that is a retarget of the iteration,
not a mode change.

Accepted cost: one field (`enabled`) carries two behaviours, create and update. AC-04 and AC-05 below
stand as written and are no longer conditional.

One correction to this story's earlier framing: it claimed a hard dependency on OQ-01 being YES. That was
wrong — in-place update needs no de-conversion to exist. The real coupling, now moot, was that answering
**both** OQ-01 and OQ-06 "no" would have left `executionMode` unchangeable once set.

## Acceptance Criteria

- [ ] **AC-01** (AC-05/FR-04) — Given `executionMode: "PARALLEL"`, when applied, then `JE4` is written as
  `1`. Matching is **case-insensitive** over exactly two tokens, `sequential` and `parallel`.
- [ ] **AC-02** (AC-05/FR-04) — Given `executionMode: 1` or `executionMode: 0` (numeric), when applied,
  then clio prints `Error: {message}` and exits non-zero. The refusal is explicit, not a parse failure:
  a caller who read the raw metadata must not silently get a different mode.
- [ ] **AC-03** (AC-05/FR-04) — Given `executionMode` omitted on **create**, then the element is Sequential
  and `JE4` is **absent** from the saved metadata; given `ignoreErrors` omitted or `false`, then `JE1` is
  absent. Write-suppression at the default is what the platform's own writer does
  (`ProcessSchemaMultiInstanceOptions.cs:145-155`) and what the corpus shows (`JE4` only ever written as
  `1`, `JE1` only ever as `true`; 9 of 61 Parallel, 9 of 61 ignoring).
- [ ] **AC-04** (AC-05/FR-04, OQ-06) — Given an already-Parallel element and an update that omits
  `executionMode`, then it stays Parallel — omitted means "left as is", never "reset to default".
- [ ] **AC-05** (OQ-06) — Given an already-multi-instance element and a block carrying
  `enabled: true` plus one mode field, when applied, then that field is updated in place and the other is
  untouched; the five parameters and their UIds are unchanged (no re-mint, no UId churn).
- [ ] **AC-06** (AC-06/FR-05) — Given an element that is **not** multi-instance, when a caller sends
  `executionMode` or `ignoreErrors` **without** `enabled: true`, then clio prints `Error: {message}`, exits
  non-zero, and the element is unchanged — **no `BP6` is created**. Accepting it would mint an empty
  `ProcessSchemaMultiInstanceOptions`, which turns the mode ON with five empty UIds
  (`IsMultiInstanceModeEnabled` is the bare null check `MultiInstanceOptions != null`,
  `ProcessSchemaActivity.cs:84`) and puts the element permanently on the throwing `GetByUId` path at design
  time (`:378-379`) and run time (`ProcessParameterValueProvider.cs:720-721`).
- [ ] **AC-07** — Given the refusal message of AC-02 and AC-06, when a caller reads it, then it names the
  accepted tokens (AC-02) or names `enabled: true` as the thing to add (AC-06). A refusal that does not
  name the working alternative is a bug report waiting to happen.
- [ ] **AC-ERR** — Given any refusal above, when the batch runs, then **nothing is persisted** (the single
  save point is after the batch, `ProcessModifyHandler.cs:91`, and the catch at `:113` skips it).

## Implementation Notes

Key files: `Contracts/ProcessDescriptorContracts.cs` (`MultiInstanceOptionsDescriptor` from story 3),
`Elements/MultiInstanceApplier.cs`.

`MultiInstanceExecutionMode` is `Sequential`(0) | `Parallel`(1), declared without explicit values
(`Terrasoft.Core/Process/ProcessEnum.cs:239-251`). `JE4` is `{59364F37-A3A1-4C3C-A8E9-B0521BD65283}`,
`JE1` (`IgnoreErrors`) is `{F105FC11-26B7-467D-9B48-B6B2276DED87}` (platform facts §2).

Parse the token yourself against the two names; do not use `Enum.TryParse`, which would silently accept
`"0"`, `"1"` and any other member name a future platform release adds.

**Do not gate Parallel.** `FlowSchemaGenerator.cs:379` is the only place in `Terrasoft.Core` that reads
`MultiInstanceExecutionMode`, with no condition around it, and
`GlobalAppSettings.FeatureUseMultiInstanceProcessElement` (`:377`) defaults false but is **dead** —
declaration and config read only, no consumer.

**Say what the contract does about `useBackgroundMode`, and do not silently set it.** Parallel does not by
itself mean concurrent threads: `FlowVisitor` drives a single-threaded FIFO queue, and genuine concurrency
comes only from `FlowBackgroundToken`, inserted when `activity.UseBackgroundMode` is true. 39 of the 61
shipped multi-instance elements run in background mode, so the combination is the **majority** shape.
`useBackgroundMode` is already a first-class element field — this block neither sets it nor defaults it,
and the tool text (story 13) says so.

`IgnoreErrors` transfers onto exactly one generated element, the Error iteration token, and changes only
what `Compensate` does: with it the flow continues to the next iteration; without it the element log is set
to Error and the process fails. The failed-iteration increment happens either way. Do not describe it as
"errors are hidden".

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` (package) | case-insensitive `"PARALLEL"`/`"Sequential"`; numeric refused; unknown token refused; default write-suppression of `JE1`/`JE4`; omitted-on-update leaves the value; in-place update touches neither the five parameters nor their UIds; mode-without-`enabled` refused and no `BP6` created | `tests/UnitTests/CrtProcessBuilder.Tests/MultiInstanceApplierTests.cs` |
| Unit `[Category("Unit")]` (package) | the two refusal messages name the accepted tokens / `enabled: true` | same |
| E2E | one Parallel conversion in the story 13 fixture | `clio.mcp.e2e/SubProcessMultiInstanceToolE2ETests.cs` |

Test naming: `Apply_ShouldWriteExecutionModeParallel_WhenTokenIsUppercase`,
`Apply_ShouldRefuseNumericExecutionMode_WhenCallerSendsOne`.

## Definition of Done

- [ ] `executionMode` is string-only, case-insensitive, two tokens; numeric refused with a message naming
      the accepted tokens
- [ ] `JE1` / `JE4` are write-suppressed at their defaults, pinned by a metadata-level assertion
- [ ] Omitted-on-update never resets; in-place update causes no UId churn
- [ ] The mode-without-`enabled` refusal exists and a test proves no `BP6` is created
- [ ] The OQ-06 assumption is restated in the PR description with the one-line consequence if reversed
- [ ] Tests: AAA, `because` on every assertion, `[Description]` on every method, `[Category("Unit")]`
- [ ] No new CLI flag (kebab-case rule vacuous)
- [ ] Validated locally: `dotnet test tests/UnitTests/CrtProcessBuilder.Tests/CrtProcessBuilder.Tests.csproj -c dev-nf`
      in the **main checkout, not a worktree**
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
