---
description: a process launched through ProcessEngineService.svc/RunProcess exposes no id and no SysProcessLog row until the RunProcess call itself returns, so there is nothing to poll or correlate at an MCP response deadline
applies-to:
  - clio/Command/RunProcessCommand.cs
  - clio/Command/McpServer/Tools/ProcessDesigner/RunProcessTool.cs
ticket: ENG-95791
date: 2026-08-26
---

**What is true** — the instance id exists only in the `RunProcess` response, and the run's
`SysProcessLog` row reaches the database only when that call returns: `Process.Run` wraps its body in
`RunWithEventBuffer`, which flushes the buffer by disposing the event writer in its `finally`, and
buffering is on by default (`ProcessFeatures.UseEventBuffering`). `SysProcessData`, which
`GetRunningProcessesCount` reads, is written only when a process persists state, so a straight-through run
never appears there either. The flush point is the end of the CALL, not of the process — once
`RunProcess` returns the row is present whatever state the run reached, and polling it is a primary-key
read, because the core writes the log item with `Id = Process.UId`.

**Why it is this way** — the id is generated inside the process instance, and the log is buffered to
avoid a write per element on a long run.

**What breaks if you ignore it** — any promise to return a `processId` while the run is still in
flight. A newest-row-by-timestamp lookup silently picks another caller's run, and a before/after diff
finds nothing because no row exists yet.
