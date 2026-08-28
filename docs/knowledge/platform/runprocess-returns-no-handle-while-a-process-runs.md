---
description: a process launched through ProcessEngineService.svc/RunProcess exposes no id and no SysProcessLog row until the RunProcess call itself returns, so there is nothing to poll or correlate at an MCP response deadline
applies-to:
  - clio/Command/RunProcessCommand.cs
  - clio/Command/McpServer/Tools/ProcessDesigner/RunProcessTool.cs
ticket: ENG-95791
date: 2026-08-26
---

**What is true** — the instance id of a run started by `RunProcess` exists only in that call's response,
and the run's `SysProcessLog` row is not in the database until the call returns either. `Process.Run`
wraps its whole body in `RunWithEventBuffer`, which disposes the event writer — flushing the buffer — in
its `finally`; `WriteSysProcessLog` only *publishes* a `ProcessLogStartEvent` into that buffer while a
writer is active, and event buffering is on by default (`ProcessFeatures.UseEventBuffering`).
`SysProcessData`, which `GetRunningProcessesCount` counts, is written only when a process persists state,
so a run that goes straight through inside the HTTP request never appears there either.

The flush point is the end of the **call**, not the end of the process: once `RunProcess` has returned,
the row is present whatever state the process reached — including `Running`, when it suspended on a user
task, a timer or a signal. Polling is then a primary-key read, because the core writes the log item with
`Id = Process.UId`, the same Guid the response carried. (Measured for a completed run on 8.3.4; the
suspended case follows from the same flush path.)

**Why it is this way** — the id is generated inside the process instance, and the log is buffered to avoid
a write per element on a long run. Neither is a contract the service exposes mid-flight.

**What breaks if you ignore it** — any promise to hand back a `processId` when answering before Creatio
does. There is no source to read it from: a newest-row-by-timestamp lookup silently picks another
caller's run, and a before/after set diff finds nothing because no row exists yet. `run-process` therefore
answers `status: accepted-still-running` with `processId: null` and says so.
