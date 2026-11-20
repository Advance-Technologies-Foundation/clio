---
description: a timer start's Quartz job is registered inside the schema save only for an enabled process whose saved version is the active one, keyed by the ELEMENT UId in a group shared by every version of the family - so a saved timer can silently never fire, a replace (new UId) is a new job, and a test timer left behind is a real scheduled job
applies-to:
  - clio/Command/ProcessModel/IProcessDescriber.cs
  - clio/Command/ProcessModel/ProcessDigest.cs
  - clio.mcp.e2e/Support/Mcp/TimerStartE2ESupport.cs
ticket: ENG-91849
date: 2026-11-18
---

**What is true** — the platform registers (or re-registers) the Quartz job of a timer start (`ProcessSchemaStartTimerEvent`,
designer item `c735ed92-…`) inside the process schema SAVE, and only when the process is enabled and the saved version is
the family's active one. The job is keyed by the timer ELEMENT's UId, in a job group shared by every version of the
family. Nothing in the save says whether a job was written: a disabled process, a non-active version or a one-off whose
start is already past all save green with no job. CrtProcessBuilder 1.6.6.187 therefore reads the scheduler after the save
and in describe (`timer.scheduled`: `scheduled` with `nextFireTimeUtc`, or `processDisabled`, `notActiveVersion`,
`staleTrigger`, `noJob`, `schedulerUnavailable`) - clio only relays it.

**Why it is this way** — the platform's synchronizer owns the job (package research for ENG-91849, verified in the core
sources); the designer never shows it. The family-shared group is why a version save that fails can take the family's jobs
with it, which the package re-syncs for the active version.

**What breaks if you ignore it** — reading a successful save as "the timer runs" reports a schedule that never fires.
Treating `scheduled` as content makes the checksum change on every fire, although the schema did not change.
A `removeElement` + `addElement` replace gives the timer a NEW UId and therefore a new job, while `setTimer` keeps the UId
and the job key. And a test timer that loses its cleanup is a real job on a shared stand, run as the System operations
user whenever its schedule comes due.
