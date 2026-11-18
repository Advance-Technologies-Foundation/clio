# Story 5: Prove the timer start end to end on a stand

**Feature**: eng-91849-timer-start-element
**Jira**: ENG-91849 Timer start element (start by time / schedule) (Story; epic ENG-92704 Create BP via AI Toolkit)
**Spec**: [spec-eng-91849-timer-start-element.md](../prd/spec-eng-91849-timer-start-element.md) - R1-R5, R7
**ADR**: [adr-eng-91849-timer-start-element.md](../adr/adr-eng-91849-timer-start-element.md) - D1-D3, consequences
**Test plan**: [tp-eng-91849-timer-start-element.md](../test-plans/tp-eng-91849-timer-start-element.md)
**Status**: ready-for-dev
**Repo**: clio (same PR as story 1)
**Depends on**: [story 1](story-eng-91849-timer-start-element-1.md), [story 2](story-eng-91849-timer-start-element-2.md),
[story 3](story-eng-91849-timer-start-element-3.md), [story 4](story-eng-91849-timer-start-element-4.md)

---

## As a

clio maintainer

## I want

MCP e2e coverage that builds, reads back, changes and refuses timer starts on a live stand

## So that

the tool path from clio through the bundled package to the platform scheduler is proven, not only the key sets

---

## Acceptance Criteria

- AC1: `day`, `week`, `month` (by `weekday`), `cron` and `once` build and read back in their write shape;
  `scheduled.nextFireTimeUtc` is asserted (TC-I-01, TC-I-02).
- AC2: a describe -> rebuild round trip reads back the same block (TC-I-03).
- AC3: a `timerStart` is added next to an existing Simple start, and a Simple start is replaced by a timer (TC-I-04,
  TC-I-05).
- AC4: `setTimer` changes the schedule in place, keeping the element UId and name; `setElement` with `timer` is refused
  (TC-I-06).
- AC5: with `timeZone` omitted the zone equals the e2e user's profile zone, else the All-Users `DefaultTimeZone` Code
  read from the stand, and the notice names the source; with neither set, the Q2 refusal (TC-I-07).
- AC6: validate-process-graph accepts the element; `run-process` on a timer-only process is refused (TC-I-08, TC-I-09).
- AC7: a past `once`, Feb 30, `week` + `every: 2`, `day` + `every: 14` and `cron` `0 0 8 */14 * ? *` are refused and
  create nothing (TC-I-10).
- AC8: every e2e timer has the far-future window of the test plan (2090), and every process a case creates is deleted
  in a finally block.

## Definition of Done

- [ ] TC-I-01 to TC-I-10 implemented in `CreateBusinessProcessToolE2ETests`, `ModifyBusinessProcessToolE2ETests` and
  `ValidateProcessGraphToolE2ETests`.
- [ ] e2e run green on a stand with the PR's final cut installed.
- [ ] No timer process left on the stand after the run (checked once by hand).
