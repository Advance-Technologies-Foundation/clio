---
description: passing resultParameterNames to RunProcess forces a background-mode process to run synchronously, and an unknown result name aborts the launch before the process starts
applies-to:
  - clio/Command/RunProcessCommand.cs
ticket: ENG-95791
date: 2026-08-26
---

**What is true** — `resultParameterNames` on a `RunProcess` call is not only an output selector, it
switches how the process executes. `ProcessExecutor.TryCreateValueReceiver` returns a receiver only for a
non-empty list, and `ProcessExecutor.RunProcess` takes its background fire-and-forget branch only
`if (valueReceiver == null && isUseBackgroundMode)`. The same process therefore runs fire-and-forget
without result parameters and **synchronously** with them. The mode cannot be driven from the request
itself: `RunProcessRequest` inherits a `forceBackgroundMode` member, but it only sets
`ForbidOpeningPages`.

The list is also verified before anything runs. `Process.Run` calls
`ProcessParameterValueReceiver.Verify`, which does `schema.Parameters.GetByName(name)` per entry, and
`GetByName` throws `ItemNotFoundException` for an unknown name. `Verify` checks EXISTENCE only, not
direction — an `Input` parameter listed as a result passes it.

**Why it is this way** — a caller waiting for output values cannot be answered by a queued run, so asking
for outputs implicitly asks for a synchronous one.

**What breaks if you ignore it** — injecting a placeholder result parameter to "force" a synchronous run
and obtain a handle does not work: an unknown name aborts the launch before the process starts. And a tool
that quietly adds a result parameter of its own moves a deliberately backgrounded run into the web request
without being asked. `run-process` forwards exactly what the caller listed, rejects an unknown or
`Input`-direction code up front, and documents the switch instead of exploiting it.
