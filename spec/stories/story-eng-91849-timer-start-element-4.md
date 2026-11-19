# Story 4: Name the timer start in the tool contracts within the payload budget

**Feature**: eng-91849-timer-start-element
**Jira**: ENG-91849 Timer start element (start by time / schedule) (Story; epic ENG-92704 Create BP via AI Toolkit)
**Spec**: [spec-eng-91849-timer-start-element.md](../prd/spec-eng-91849-timer-start-element.md) - R2, R9
**ADR**: [adr-eng-91849-timer-start-element.md](../adr/adr-eng-91849-timer-start-element.md) - D6, D7
**Test plan**: [tp-eng-91849-timer-start-element.md](../test-plans/tp-eng-91849-timer-start-element.md)
**Status**: review
**Repo**: clio (same PR as story 1)
**Depends on**: [story 1](story-eng-91849-timer-start-element-1.md); the guide pointer depends on the clio-knowledge
release that publishes `process-timer-start` ([story 6](story-eng-91849-timer-start-element-6.md))
**Blocks**: [story 5](story-eng-91849-timer-start-element-5.md)

---

## As a

coding agent choosing an element from the tool contracts

## I want

the create, modify, describe and validate contracts to tell me that `timerStart` exists, that `setTimer` changes it in
place, and which guide to read

## So that

I find the element and its rules without the contracts growing past their ceiling

---

## Acceptance Criteria

- AC1: create (contract, prompt and short-form call shape) names `timerStart`, the `timer` block, the
  `process-timer-start` pointer and one confirm-before-save sentence (a timer runs unattended; the user confirms the
  schedule first), which the short form keeps (TC-U-08, TC-U-13).
- AC2: modify names `timerStart` in the `addElement` list, `setTimer`, the `setElement` pointer, the guide and the same
  confirm-before-save sentence (TC-U-08, TC-U-14); modify-business-process-as-new-version says what a FAILED save does
  to the family's timers (TC-U-14b).
- AC3: describe names the `timer` read-back clause with `storedCron`, `designerKind` and `scheduled` (TC-U-15).
- AC4: validate-process-graph (tool and prompt) lists `timerStart` as buildable; the not-buildable sentence keeps
  message starts and intermediate events only (TC-U-16).
- AC5: every added byte is paid by named, reviewer-approved swaps recorded in the `ToolContractPayloadBudgetTests`
  comment and re-measured by lowering the ceiling; no ceiling raise (Q10). If a swap does not fit, the item goes back
  to the owner with the numbers (TC-U-17).
- AC6: no contract carries the schedule vocabulary; it lives in the guide. The guidance digest card is unchanged (Q11).
- AC7: the guide pointer is added only after `curated-knowledge-names.json` names `process-timer-start` (the guidance
  name oracle).

## Definition of Done

- [ ] TC-U-08 and TC-U-13 to TC-U-17 implemented and green.
- [ ] The swaps are listed in the PR description for the reviewer's approval.
- [ ] Guidance-name oracle green after the re-pin.
- [ ] MERGE GATE: the `process-timer-start` pointer is in the create and modify contracts (asserted by TC-U-13/14),
  paid from the bytes reserved in the `ToolContractPayloadBudgetTests` comment, once the clio-knowledge release that
  publishes the guide is out. The PR does not leave draft or merge before it.
