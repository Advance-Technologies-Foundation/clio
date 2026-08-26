---
description: passing resultParameterNames to RunProcess forces a background-mode process to run synchronously, and an unknown result name aborts the launch before the process starts
applies-to:
  - clio/Command/RunProcessCommand.cs
  - clio/Command/StartProcess/ProcessArgs.cs
ticket: ENG-95791
date: 2026-08-26
---

**What is true** — `resultParameterNames` on a `RunProcess` call is not only an output selector, it
switches how the process executes. `ProcessExecutor.TryCreateValueReceiver` returns a receiver only when
the list is non-empty, and `ProcessExecutor.RunProcess` takes its background fire-and-forget branch only
`if (valueReceiver == null && isUseBackgroundMode)`. So the SAME process runs fire-and-forget without
result parameters and **synchronously** with them. `RunProcessRequest` itself carries no mode flag: the
`forceBackgroundMode` member it inherits only sets `ForbidOpeningPages`, and the executor's real
background decision cannot be driven from the request at all.

The list is also verified before anything runs. `Process.Run` calls
`ProcessParameterValueReceiver.Verify`, which does `schema.Parameters.GetByName(name)` per entry, and
`GetByName` throws `ItemNotFoundException` for an unknown name. `Verify` checks EXISTENCE only, not
direction — an `Input` parameter listed as a result passes it.

**Why it is this way** — a caller waiting for output values cannot be answered by a queued run, so
asking for outputs implicitly asks for a synchronous one.

**What breaks if you ignore it** — two things. Injecting a placeholder result parameter to "force"
synchronous execution and get a handle does not work: an unknown name aborts the launch before the
process starts, so the trick fails outright. And a tool that silently adds a result parameter of its own
changes how the user's process executes — moving a deliberately backgrounded run into the web request —
without being asked. `run-process` therefore forwards exactly what the caller listed, rejects an
unknown or `Input`-direction code up front with the accepted codes, and documents the mode switch
instead of exploiting it.
