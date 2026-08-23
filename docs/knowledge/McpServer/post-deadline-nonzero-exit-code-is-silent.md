---
description: McpProgressHeartbeat.ObserveInBackground observes only faults, so detached post-deadline work that signals failure by RETURNING a non-zero exit code reports nothing anywhere
applies-to:
  - clio/Command/McpServer/Tools/McpProgressHeartbeat.cs
  - clio/Command/McpServer/Tools/CommandExecutionResult.cs
  - clio/Command/McpServer/Tools/InstallProcessBuilderTool.cs
ticket: ENG-94385
date: 2026-08-19
---

**What is true** — when a long-running tool answers the caller at the MCP response deadline and lets
its work continue detached, the only thing watching that work is
`McpProgressHeartbeat.ObserveInBackground`. Its continuation is registered with
`TaskContinuationOptions.OnlyOnFaulted`, so it fires for an **exception** and for nothing else. A
clio command that reports failure the usual way — by returning a non-zero exit code from `Execute`
— completes its `Task` successfully. Nothing is logged, nothing reaches stderr, and the caller has
already been told `CommandExecutionResult.FromInfo(...)`, which is exit code 0.

**Why it is this way** — the continuation exists to stop an `UnobservedTaskException`, not to judge
outcomes; a returned exit code is a value inside a completed task, invisible to the task machinery.
`FromInfo` documents itself as "accepted and still running, poll for status", which is only honest
when the accepted part is genuinely finished before the deadline.

**What breaks if you ignore it** — a tool whose deadline race spans the whole operation (upload,
install, a readiness wait, an outcome probe) turns every post-deadline failure into a silent
success: the agent is told the work was accepted, the work fails, and no surface in the process ever
says so. Either keep the pre-deadline part small enough that `FromInfo` is true, or have the
detached path throw (or record its verdict in an operation registry the caller can poll).
