---
description: RunProcess answers success=true for a failed run unless a platform feature flag is on, and a refusal to start is indistinguishable from a background queueing by process id and status alone
applies-to:
  - clio/Command/RunProcessCommand.cs
ticket: ENG-95791
date: 2026-08-26
---

**What is true** — `success` is not the run verdict. `BaseResponse` initializes it to `true` and
`SetErrorInfoWhenIsNeeded` clears it only when the status is `Error` AND
`GlobalAppSettings.FeatureSetErrorInfoIfProcessHasFailedExecution` is on (per-instance, default true), so
a failed run can answer `success: true` with `processStatus: 3`. The verdict is `processStatus`, scale
`Terrasoft.Core.Process.ProcessStatus`: `0 Inactive, 1 Running, 2 Done, 3 Error, 4 Cancelled,
5 Cancelling` — the same integers as `SysProcessStatus.Value`, whose row for 2 reads "Completed".

Separately, `processId = Guid.Empty` with `processStatus = 0` is AMBIGUOUS: it is both the background
fire-and-forget branch's `new ProcessDescriptor()` and a startup refusal, which
`ProcessExecutor.CheckCanExecute` raises as `ProcessCannotBeManuallyStartedException` for a process with
no manual start event. `success` and `errorInfo` are the only discriminators.

**Why it is this way** — the error-info behaviour sits behind a feature flag for backward
compatibility, and the empty descriptor is one "nothing to report" value reused for both cases.

**What breaks if you ignore it** — a failed run reads as a success, and a process that can never be
started manually reads as "queued in the background", telling the caller to wait for something that will
never happen. Both were observed on a live stand.
