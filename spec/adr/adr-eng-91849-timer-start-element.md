# ADR: Timer start element (ENG-91849)

**Status**: Accepted. The task owner decided Q1-Q13 on 2026-11-14 (Q2, Q5, Q12 and Q13 revised in the review of the
plan the same day).
**Date**: 2026-11-18
**Jira**: ENG-91849 Timer start element (start by time / schedule) (Story; epic ENG-92704 Create BP via AI Toolkit)
**Spec**: [spec-eng-91849-timer-start-element.md](../prd/spec-eng-91849-timer-start-element.md)
**Test plan**: [tp-eng-91849-timer-start-element.md](../test-plans/tp-eng-91849-timer-start-element.md)

Sources outside the public repositories (the CrtProcessBuilder package, CrtProcessDesigner, the core platform) are
cited by type name, not by path. The package keeps its own records for the decisions it alone owns (the default zone
Q2, the scheduler read Q4, the past-start rule Q5); this ADR records the ones that are hard to reverse on the clio side.

## Context

A timer start is `ProcessSchemaStartTimerEvent` (a subclass of the plain start event) with the designer manager item
`c735ed92-e545-4699-b3c6-f8f57dd8c529`; the core has no constant for that item, so the designer and the builder tell a
timer from a plain start by the manager item. It stores six properties: the cron expression, a misfire flag, the
schedule kind, the window start and end, and the time zone (a `TimeZone` lookup Code). Designer captures on a test
stand (2026-11-14, and the variants not captured that day on 2026-11-18) fixed the cron shape the designer stores for
every kind: for example Minute/Hour keeps an hour step of `/1` (`0 0 0-23/1 * * ? *`) and writes the full window as
`0-23`, and a Month "first calendar day" stores the same bytes as "day of the month 1" and reopens as the former.
The platform registers the Quartz job inside the schema save only for an enabled process whose saved version is the
active one; the job is keyed by the element UId within a group shared by every version of the family. Today the
package claims timer and message starts in its plain-start handler, so describe reports both as `startevent`.

## Decisions

**D1 - A dedicated type and block on the Signal start pattern, structured kinds plus `cron` (Q1).** `type:
"timerStart"` with a `timer` block (members in the spec, R1). The server builds the designer's cron shape from the
structured fields, so the designer reopens the element on the matching sub-page; `cron` is the escape hatch (6 or 7
Quartz fields, a 5-field Unix expression refused, not converted). *Rejected*: `once` + `cron` only (agents hand-write
Quartz and every timer opens as "Other frequency"); cron with the kind inferred.

**D2 - The clio key set includes the read-back keys `storedCron`, `designerKind` and `scheduled`.** They are
describe-only: `storedCron` is the cron as stored (`cron` itself is emitted only for `schedule: cron`), `designerKind`
the stored kind when the cron does not match its shape, `scheduled` the live scheduler state. clio's
`ProcessBlockKeyCheck` accepts them on create, `addElement` and `setTimer` so a described block replays unchanged; the
package ignores them with its standard read-back warning. The nested `weekday` object gets its own key set and label.
`setElement` with a `timer` key is refused with the pointer "use setTimer" (as for `signal`). The drift tests read the
key sets from the bundled archive, so a member added on one side only fails the build. *Rejected*: stripping the
read-back keys in clio before sending (the package's warning is the single place that tells the caller they were
ignored).

**D3 - `setTimer` replaces the schedule group as a whole (Q3).** If any schedule member is sent, the schedule is rebuilt
from what is sent (`schedule` required then); `start`, `end`, `timeZone`, `repeatOnMisfire` update individually. clio
does not merge with the described schedule and does not validate the schedule; it checks keys only. *Rejected*: a
field-level merge like `setSignal` (variant-conflict rules); no `setTimer`, the replace recipe only.

**D4 - `timer.scheduled` is excluded from the digest checksum and the change list.** It is live state read from the
scheduler (the next and previous fire times, or the reason there is no job: `processDisabled`, `notActiveVersion`,
`staleTrigger`, `noJob`, `schedulerUnavailable`); it changes when the timer fires and when the process is enabled or
another version is activated, while the schema's content does not. It is the element counterpart of
`ProcessDigest.FamilyStateMembers` (`tracing`). *Rejected*: hashing it (every fire would change the checksum of an
unchanged process and the write digest would report a phantom change).

**D5 - The message-start describe change ships here, and clio's start detection falls back to the runtime `Type`
(Q7).** The package fixes its plain-start handler to the exact plain-start type, so a message start has no
`buildType` and describes as read-only (and a describe -> create clone no longer turns it into a Simple start). This
changes the describe of every message-start process; the owner accepted it in this task's scope. clio's
`ProcessParameterBindingWarnings` decides "is a start" by `buildType` today; it adds `timerstart` and, when `buildType`
is null, falls back to the element's runtime `Type`, so a message start keeps counting as a start. *Rejected*: a
separate ticket (the timer handler needs the plain-start handler to stop claiming timers anyway).

**D6 - The contracts name only the type, the block, `setTimer` and the guide; paid by swaps, no ceiling raise
(Q10).** The full vocabulary lives in `process-timer-start`. Every added byte of the create, modify, describe and
validate contracts and the short-form shapes is paid by named swaps, recorded in the `ToolContractPayloadBudgetTests`
comment and re-measured by lowering the ceiling. If a swap does not fit, the item goes back to the owner with the
numbers. *Rejected*: raising the ceiling; a separate clean-up ticket for the process contracts (not opened).

**D7 - The guide stays off the guidance digest card (Q11).** As with `webService`, agents reach `process-timer-start`
through the create contract's type list, `process-element-catalog` and routing; the card does not change. The guide
carries the confirm-before-save gate (state schedule, zone, first run, run-as user and the data written, then get a
yes). *Rejected*: the card via swaps; a short hint in rule 1.

**D8 - Floor.** create, modify and modify-as-new-version raise their `[RequiresPackage]` literal to the PR's final cut:
an older server refuses the type and block as unknown keys, in a message that does not say the package is behind.

## Consequences

- One package cut, one knowledge generation and one clio rebundle; merge order package -> knowledge -> clio; the clio
  PR merges only after the TeamCity process-designer lane is green.
- The contract budgets are close to spent again after this change; the swaps that paid for it are a finite source.
- The e2e timers register real Quartz jobs on the stand. Each gets a far-future window (2090) so a job exists and
  `nextFireTimeUtc` is asserted while no run can happen even if cleanup is lost, and the processes are deleted in a
  finally block.
- The describe of every message-start process changes (no `buildType`); consumers keyed on `startevent` for those
  elements see a read-only element instead.
- A timer job belongs to the version family, not to one version: a failed new-version save can drop the family's jobs
  (the package re-syncs the active version), and `scheduled` reports `notActiveVersion` or `staleTrigger` instead of
  a time when the described version is not the one the scheduler runs.
