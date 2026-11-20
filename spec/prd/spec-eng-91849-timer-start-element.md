# SPEC: Timer start element (ENG-91849)

**Jira**: ENG-91849 Timer start element (start by time / schedule) (Story; epic ENG-92704 Create BP via AI Toolkit)
**ADR**: [adr-eng-91849-timer-start-element.md](../adr/adr-eng-91849-timer-start-element.md)
**Stories**: [1](../stories/story-eng-91849-timer-start-element-1.md) (rebundle and floor),
[2](../stories/story-eng-91849-timer-start-element-2.md) (key sets),
[3](../stories/story-eng-91849-timer-start-element-3.md) (describe, digest, data id, start detection),
[4](../stories/story-eng-91849-timer-start-element-4.md) (tool texts within the budget),
[5](../stories/story-eng-91849-timer-start-element-5.md) (e2e),
[6](../stories/story-eng-91849-timer-start-element-6.md) (knowledge re-pin, docs and change-summary statements)
**Test plan**: [tp-eng-91849-timer-start-element.md](../test-plans/tp-eng-91849-timer-start-element.md)
**Status**: decided. The task owner decided Q1-Q13 on 2026-11-14 (Q2, Q5, Q12 and Q13 were revised in the review of
the plan the same day; the rows below carry the current decisions). The plan, the designer captures (2026-11-14, and
the variants captured on 2026-11-18) and the stand run are kept with the team's research notes outside this
repository; this file and the ADR carry every decision the clio side depends on.

> Three PRs, one per repository: crt-process-builder (the element, `setTimer`, the scheduler read, the message-start
> describe fix), clio-knowledge (the `process-timer-start` guide and the read-only sweep) and clio (this set and the
> stories below). The package and the knowledge sides are dependencies of the clio PR, not stories here. Merge order
> (human merges): package -> knowledge -> clio.

## Problem

The AI toolkit cannot build, change or explain a process that starts on a schedule. The create contract has no timer
type, `validate-process-graph` lists timer starts as not buildable, and the knowledge base calls them read-only. Worse,
a designer-built timer start is described today as `buildType: startevent` with no schedule, so a describe -> rebuild
clone silently turns it into a Simple start; a message start is mis-described the same way. The platform registers the
Quartz job for a timer inside the schema save only when the process is enabled and the saved version is the active one,
and writes no new trigger for a one-off whose start is already past, all without an error: a timer can be "saved but
never fire" and nobody is told.

## Users and use cases

An agent building a business process for a user who wants it to run on a schedule: a nightly cleanup, a weekly
report, a reminder on the first work day of the month, a one-off run at a given instant. The agent must state the
schedule, the zone and the first run, and get the user's yes before a save that schedules runs.

Out of scope: "every N weeks" (Q12; refused with the reason, no workaround recipe); a timer start inside an event
sub-process (stays unhandled as today); intermediate timer events (ENG-92722 Intermediate events (timer / signal /
message)); a duplicate-timer refusal (Q6: several timer starts are allowed).

## Requirements

| # | Requirement | Owner repo |
|---|---|---|
| R1 | A dedicated element type `timerStart` with a `timer` block on create and `addElement`: `schedule` (`once`, `minute`, `hour`, `day`, `week`, `month`, `year`, `cron`), `every`, `at`, `fromHour`, `toHour`, `daysOfWeek`, `dayOfMonth`, `weekday` (`position`, `day`), `workday`, `month`, `cron`, `start`, `end`, `timeZone`, `repeatOnMisfire` (Q1: structured kinds plus `cron`; the server builds the designer's cron shapes). | package (+ clio key set) |
| R2 | A `setTimer` op `{op, elementName, timer}` changes a top-level timer start in place; the schedule group is replaced as a whole, `start`/`end`/`timeZone`/`repeatOnMisfire` update individually (Q3). `setElement` with a `timer` key is refused with the pointer "use setTimer". | package + clio |
| R3 | Every check runs before the save: past `once`, a cron that never fires (Feb 30), an end before the first fire, an unresolvable zone, a DST-gap time, unsafe characters (the codegen sink). "Every N weeks" is refused on `week`, on a `day` step that is a multiple of 7 and on a `cron` day-of-month step that is a multiple of 7 (Q12); every step N > 1 is accepted with a notice naming the real cadence (Q13); a differing past `start` on a recurring kind is dropped with a notice (Q5); `useBackgroundMode` is refused (Q8). | package |
| R4 | An omitted `timeZone` on create/`addElement` defaults to the caller's profile zone, else the `DefaultTimeZone` setting, else the request is refused; the notice names the zone and its source (Q2). | package |
| R5 | `describe-business-process` reports the block in its write shape, plus the read-back keys `storedCron`, `designerKind` and `scheduled` (Q4: whether this version's Quartz job exists and its next fire time, or why not). | package + clio DTO |
| R6 | The summary digest lists timer starts (`startTimers[]`); `timer.scheduled` is left out of the checksum and of the write digest's change list, because it is live scheduler state, not content. | clio |
| R7 | The data id `timerstart` resolves to the timer start event type, so `validate-process-graph` and the binding warnings know the build token. | clio |
| R8 | A message start no longer describes as `startevent` (Q7: package P4). clio's start detection falls back to the runtime `Type` when `buildType` is null, so a message start still counts as a start. | package + clio |
| R9 | The tool contracts name only the type, the block, `setTimer` and the guide pointer, paid by named swaps with no ceiling raise (Q10). | clio |
| R10 | The guide `process-timer-start` owns every rule of the block and the confirm-before-save gate; it stays off the guidance digest card (Q11). The read-only sentences are swept. | clio-knowledge |
| R11 | Post-save notices ("scheduled, next run …" / "saved but NOT scheduled because …" / "the edit was written but the save did not finish …") reach the caller unchanged. | package (clio relays) |
| R12 | clio refuses a stand whose CrtProcessBuilder predates the block, with the install hint (floor raise to the PR's final cut). | clio |

## Acceptance (clio side)

- The rebundled archive declares every timer wire name clio sends or reads; the pins and the drift tests pass; the
  floor names the PR's final cut (TC-U-18 to TC-U-20).
- The key check accepts the block on create, `addElement` and `setTimer`, tolerates the read-back keys, refuses unknown
  keys with the block's accepted keys and points `setElement` + `timer` to `setTimer` (TC-U-02 to TC-U-08).
- The describe DTO reads the block and `scheduled` and keeps unknown members; the digest lists timer starts and its
  checksum ignores `scheduled`; `timerstart` resolves; a message start counts as a start (TC-U-01, TC-U-09 to TC-U-12).
- The contracts and prompts name the type, the block, `setTimer` and the guide; the budgets hold with no ceiling raise
  (TC-U-13 to TC-U-17).
- The e2e cases build each kind, round-trip, add and replace a start, change it in place, read `scheduled`, cover the
  default zone and the refusals, every timer with a far-future window and deleted in a finally block (TC-I-01 to
  TC-I-10).
