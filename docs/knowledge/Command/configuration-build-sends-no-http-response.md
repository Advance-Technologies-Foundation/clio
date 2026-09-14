---
description: Creatio answers WorkspaceExplorerService Build/Rebuild with zero response bytes and a connection reset, because the build ends by reloading the runtime that would have sent the response
applies-to:
  - clio/Command/CompileConfigurationCommand.cs
  - clio/Common/CompilationCompletionDecider.cs
  - clio/Common/CompilationActivityWatcher.cs
ticket: 1422
date: 2026-09-09
---

**What is true** — a configuration build requested through
`ServiceModel/WorkspaceExplorerService.svc/Build` or `/Rebuild` does not answer. Measured on a live
stand (Creatio core 10.0.0.934, .NET Framework, on-prem), six of six builds ended with **zero
response bytes** and a connection reset (`WinError 10054`) at the moment the application reloaded.
The reload is the last step of the build, and it tears down the connection the response would have
travelled on. `PackageBuilder.CompileWithPolling` records the same platform change for the package
route ("In Creatio 8.3.3+, RebuildPackage no longer sends back an HTTP response"); the configuration
route behaves the same way and was simply never converted.

There is no server-side "is a build running" endpoint to ask instead. On this platform every
`Is*`/`Get*` status method on `WorkspaceExplorerService` answers 404, including
`IsODataBuildRunning`, which newer platforms do expose. The only verdict source is
`api/ConfigurationStatus/GetLastCompilationResult`, and it carries **no timestamp**.

**Why it is this way** — the build's own last action is what destroys its reporting channel, so the
synchronous request/response shape cannot express the operation. The platform moved to
fire-and-observe and the client has to follow.

**What breaks if you ignore it** — waiting on that response is what produced
[issue #1422](https://github.com/Advance-Technologies-Foundation/clio/issues/1422). Two distinct
failures come out of it:

- On a plain reset, `RemoteCommandOptions.MaxAttempts` (3) re-sent the request. Measured: one
  `clio cc --all` ran the full configuration rebuild **three times**, forced three runtime reloads,
  took 8m42s, and finished by tripping IIS rapid-fail protection, which stopped the application
  pool. The "success" clio reported on the incremental route came from the retry, not from the build
  it started.
- Where an intermediary holds the socket open instead of resetting it — a load balancer in front of
  a cloud instance — the request never fails, so no retry fires and, with the old
  `Timeout.Infinite`, nothing ever terminated: `compile-status` reported `running` forever and the
  configuration-build reservation was not released until its 65-minute reclaim ceiling.

Completion is therefore derived from the environment: activity in `CompilationHistory`, then the
runtime reload observed as the environment going unreachable and coming back, then
`GetLastCompilationResult` read **after** that reload. Reading the undated verdict before the reload
returns the PREVIOUS build's result while this one is still running.

**A crashed runtime leaves the same evidence a finished build leaves.** Both end with compilation
history rows that simply stop, so every completion verdict is gated on the environment answering at
that instant, and an inferred completion whose verdict cannot then be read is reported as a FAILURE.
Without both of those, a prolonged outage after one clean history row exits 0 and prints "Compilation
finished" for a build that never completed.

**The reload is visible on the verdict endpoint and NOT on the compilation-history channel.** Measured
directly across an application-pool recycle: `api/ConfigurationStatus/GetLastCompilationResult` stopped
answering for 44 seconds (requests hung until their own timeout rather than failing fast), while
`CompilationHistoryPoller`, polling the same site over the same window, reported not one failed read.
An implementation that infers the reload from history-poll failures therefore never observes one, and
falls through to whatever its slower fallback is.
