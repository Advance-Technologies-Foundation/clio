---
description: nothing links a record to the process runs its "record added" signal started - trigger-smoke finds them as SysProcessLog rows created after a baseline, which also catches unrelated runs (scheduled jobs) in the same window
applies-to:
  - clio/Command/RuntimeVerification/TriggerSmokeRunner.cs
ticket: ENG-103024
date: 2026-10-13
---

**What is true** — a run a signal start launches carries no reference to the record that fired it:
`SysProcessLog` has the schema, owner, status and times, `SysProcessEntity` is empty unless the process opts
in, and the element log has no record ids. The only link is time: rows created after the trigger. Measured on
a .NET Framework sandbox on 2026-10-13: a synchronous signal process (Contact added -> Add data Activity) wrote
its `SysProcessLog` row within the insert request, `CreatedOn` equal to its start, and the poll found it on
the first read; on that stand "Lead generation. Sending notifications" also runs about every minute as
Supervisor, so it can appear in the same window.

**Why it is this way** — the signal is an entity event; the process engine logs execution, not causes.

**What breaks if you ignore it** — reading every new run as caused by the trigger blames or cancels a
scheduled job, and a scheduled job runs as Supervisor - the usual clio login - so "started by the caller" does
not mean "started by this call" either. trigger-smoke therefore bases `success` only on attributable runs - the
caller's own, and with `process` only runs whose caption or code (`SysProcessLog.SysSchema.Name`) is that process,
plus their sub-processes by `RootId` - and lists every other run of the window under `unrelated`. It deletes
only the trigger and the caller's new records whose lookup points at it (or at another such record), and
cancels a run only when it holds one of those records: the record's `ProcessElementId` is a
`SysProcessElementData` row whose `SysProcess` is the run's `SysProcessLog` id (checked on the same stand).
Everything else is left and listed in `cleanup.skipped`.
