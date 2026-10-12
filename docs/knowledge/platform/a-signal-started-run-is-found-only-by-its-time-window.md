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
scheduled job. trigger-smoke therefore reports every run in the window by name, and cancels only runs the
calling user started that are still running; a filtered signal process keeps a shared stand from firing on
other users' records.
