---
description: Creatio records no list of the records a process run created - SysProcessEntity is empty, the element log has no record ids, element data is deleted on completion - so "created by this run" can only be a before/after diff
applies-to:
  - clio/Command/RuntimeVerification/CreatedRecordWatcher.cs
  - clio/Command/RuntimeVerification/RunRecordProvenance.cs
ticket: ENG-103024
date: 2026-10-12
---

**What is true** — on a stand with 165 process runs, `SysProcessEntity` held 0 rows (it is filled only for
processes that opt into a record link), `SysProcessElementLog` carries element captions, types and
statuses but no record ids, and `SysProcessElementData` is deleted when its element completes (see
`a-running-process-parameter-value-is-not-readable-from-sql.md`). The only general way to name what a run
created is to diff the entities it writes: the newest `CreatedOn` before the run, every row at or after it
afterwards, minus the ids that were already there.

**Why it is this way** — the process engine logs execution, not data lineage.

**What breaks if you ignore it** — an implementation that waits for a platform source reports nothing, and
one that diffs without care deletes other users' rows: the diff window also catches a record another user
created at the same moment, and `CreatedBy` cannot tell apart a parallel session under the SAME login (QA
found run-process cleanup deleting such an activity). So cleanup deletes only what it can tie to the run: a
record whose `ProcessElementId` names a `SysProcessElementData` row whose `SysProcess` is the run (or one of
its sub-processes), or a record whose lookup points at such a record. That tie exists only while the element
is live, so it is read BEFORE the run is cancelled; a record an element wrote and then completed (Add data)
has no tie and stays in `cleanup.skipped`.
