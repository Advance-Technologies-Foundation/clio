---
description: RunProcess answers success=true for a failed run unless a platform feature flag is on, and a refusal to start shares the empty-id zero-status shape with a background queueing
applies-to:
  - clio/Command/RunProcessCommand.cs
ticket: ENG-95791
date: 2026-08-26
---

**What is true** — two things about a `RunProcess` response that cannot be read off the obvious field.

`success` is not the run verdict. `BaseResponse` initializes it to `true`, and
`ProcessEngineService.SetErrorInfoWhenIsNeeded` flips it to `false` only when the status is
`ProcessStatus.Error` **and** `GlobalAppSettings.FeatureSetErrorInfoIfProcessHasFailedExecution` is on.
That flag defaults to true but is per-instance configuration, so on an instance where it is off a failed
run answers `success: true` with `processStatus: 3`. The verdict lives in `processStatus`, whose scale is
`Terrasoft.Core.Process.ProcessStatus` — `0 Inactive, 1 Running, 2 Done, 3 Error, 4 Cancelled,
5 Cancelling` — the same integers stored in `SysProcessStatus.Value` (verified against a live 8.3.4
stand), so an immediate response and a later log read are directly comparable.

Separately, `processId = Guid.Empty` with `processStatus = 0` is **ambiguous**. It is what the background
fire-and-forget branch returns (`new ProcessDescriptor()`), and it is equally what a startup REFUSAL
returns: `ProcessExecutor.CheckCanExecute` runs `managerItem.Verify(ProcessStartType.Manual)` before
anything executes, so a process whose only start events are automatic answers with that same shape plus
`success: false` and `errorInfo.errorCode = ProcessCannotBeManuallyStartedException`. `success` and
`errorInfo` are the ONLY discriminators.

Note that `errorInfo` deserializes into a `System.Text.Json` `JsonElement` on the `object`-typed field of
`ProcessStartResponse`, so handing it to a reflection-based serializer emits `{"ValueKind":1}` and loses
the message; read its `errorCode` / `message` members instead.

**Why it is this way** — the error-info behavior is behind a feature flag for backward compatibility,
and the empty descriptor is a single "nothing to report" value the executor reuses for both a queued run
and an unstarted one.

**What breaks if you ignore it** — a failed run is reported as a success, and a process that can never
be started manually is reported as "successfully queued in the background", telling the caller to wait
for something that will never happen. Both were observed on a live stand before the discrimination was
added.
