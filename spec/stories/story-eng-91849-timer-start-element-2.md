# Story 2: Accept the timer block and setTimer in clio's key check

**Feature**: eng-91849-timer-start-element
**Jira**: ENG-91849 Timer start element (start by time / schedule) (Story; epic ENG-92704 Create BP via AI Toolkit)
**Spec**: [spec-eng-91849-timer-start-element.md](../prd/spec-eng-91849-timer-start-element.md) - R1, R2, R5
**ADR**: [adr-eng-91849-timer-start-element.md](../adr/adr-eng-91849-timer-start-element.md) - D1, D2, D3
**Test plan**: [tp-eng-91849-timer-start-element.md](../test-plans/tp-eng-91849-timer-start-element.md)
**Status**: in-progress
**Repo**: clio (same PR as story 1)
**Depends on**: [story 1](story-eng-91849-timer-start-element-1.md)
**Blocks**: [story 5](story-eng-91849-timer-start-element-5.md)

---

## As a

coding agent building or changing a scheduled process through the clio MCP process tools

## I want

clio to send a `timer` block and a `setTimer` op to the server, and to accept a described block back unchanged

## So that

the timer reaches the package at all (clio refuses an undeclared block key before sending), and a describe -> replay
does not trip on the read-back keys

---

## Acceptance Criteria

- AC1: `ProcessBlockKeyCheck` has a `TimerBlock` key set with the write members of the spec (R1) and the read-back
  keys `storedCron`, `designerKind` and `scheduled`; the nested `weekday` has its own key set and label (TC-U-02,
  TC-U-04, TC-U-05).
- AC2: the `Element` list carries `timer`; the check walks `setTimer` (`op`, `elementName`, `timer`) on modify and
  modify-as-new-version (TC-U-02, TC-U-06).
- AC3: an unknown key is refused with its path and the accepted keys; `setElement` with `timer` is refused with the
  "use setTimer" pointer, like `signal` (TC-U-03, TC-U-06).
- AC4: the key sets equal the members of the bundled contracts and the read-backs are describe-only members (TC-U-07).
- AC5: clio does not validate or merge the schedule; it checks keys only (D3).

## Definition of Done

- [ ] TC-U-02 to TC-U-07 implemented and green.
- [ ] TC-U-07 green on the rebundled archive (story 1).
- [ ] No new `CLIO*` warnings in `ProcessBlockKeyCheck.cs`.
