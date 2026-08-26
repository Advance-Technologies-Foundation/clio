---
description: a synchronous process launched through ProcessEngineService.svc/RunProcess exposes no id, status or log row while it runs, so there is nothing to poll and nothing to correlate at an MCP response deadline
applies-to:
  - clio/Command/RunProcessCommand.cs
  - clio/Command/McpServer/Tools/ProcessDesigner/RunProcessTool.cs
ticket: ENG-95791
date: 2026-08-26
---

**What is true** — while a process launched by `ServiceModel/ProcessEngineService.svc/RunProcess` is
running, the platform exposes **nothing** a caller can hold on to. The instance id comes back only in
the `RunProcess` response, and the run's `SysProcessLog` row is written when the run **ends**, not when
it starts: `Process.WriteSysProcessLog` publishes a buffered `ProcessLogStartEvent` when an event writer
is active, and `Process.RunWithEventBuffer` disposes that writer — flushing the buffer — in its
`finally`, after the whole run. Event buffering is on by default
(`ProcessFeatures.UseEventBuffering.IsEnabled = true`). `SysProcessData`, which
`GetRunningProcessesCount` counts, is only written when a process persists state, so a run that goes
straight through inside the HTTP request never appears there either.

Once `RunProcess` HAS returned, polling works and is cheap: the core writes the log item with
`Id = Process.UId`, so `SysProcessLog.Id` IS the returned `processId` and a poll is a primary-key read.

**Why it is this way** — the id is generated inside the process instance, and the log is buffered to
avoid a write per element on a long run. Neither is a contract the service exposes mid-flight.

**What breaks if you ignore it** — any design that promises to hand back a `processId` when it answers
before Creatio does. There is no source to read it from: a newest-row-by-timestamp lookup is a guess
that silently picks another caller's run, and a before/after set diff finds nothing because no row
exists yet. `run-process` therefore answers `mode: accepted-still-running` with `processId: null` and
says so, rather than reporting a handle it cannot know.
