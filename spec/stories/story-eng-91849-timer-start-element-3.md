# Story 3: Describe, digest, data id and start detection for timer and message starts

**Feature**: eng-91849-timer-start-element
**Jira**: ENG-91849 Timer start element (start by time / schedule) (Story; epic ENG-92704 Create BP via AI Toolkit)
**Spec**: [spec-eng-91849-timer-start-element.md](../prd/spec-eng-91849-timer-start-element.md) - R5, R6, R7, R8
**ADR**: [adr-eng-91849-timer-start-element.md](../adr/adr-eng-91849-timer-start-element.md) - D2, D4, D5
**Test plan**: [tp-eng-91849-timer-start-element.md](../test-plans/tp-eng-91849-timer-start-element.md)
**Status**: review
**Repo**: clio (same PR as story 1)
**Depends on**: [story 1](story-eng-91849-timer-start-element-1.md)
**Blocks**: [story 5](story-eng-91849-timer-start-element-5.md)

---

## As a

coding agent reading a process before changing it

## I want

describe to give me the timer's schedule, its stored cron and whether the scheduler will actually run it, the digest
to list timer starts, and a message start to stay recognised as a start after its describe changes

## So that

I can tell a "saved but never fires" timer from a scheduled one, the checksum of an unchanged process stays stable
when its timer fires, and the binding warnings do not misjudge a process that starts by message

---

## Acceptance Criteria

- AC1: `IProcessDescriber` has a `DescribedTimer` (with `scheduled`) on the described element; unknown members are kept
  in the bags; it is null when the server omits it (TC-U-09).
- AC2: `ProcessDigest` lists timer starts in `startTimers[]` (TC-U-10).
- AC3: `timer.scheduled` is excluded from the checksum and from the write digest's change list, as the element
  counterpart of `FamilyStateMembers`; a schedule change still changes both (TC-U-11).
- AC4: the data id `timerstart` resolves to the timer start event type in `Schema.cs`; the existing designer data id
  keeps resolving (TC-U-01).
- AC5: `ProcessParameterBindingWarnings` counts `timerstart` as a start and, when `buildType` is null, falls back to
  the element's runtime `Type`, so a message start counts as a start (Q7) (TC-U-12).

## Definition of Done

- [ ] TC-U-01 and TC-U-09 to TC-U-12 implemented and green.
- [ ] The XML docs of the checksum name `timer.scheduled` among the excluded members.
- [ ] No new `CLIO*` warnings in the changed files.
