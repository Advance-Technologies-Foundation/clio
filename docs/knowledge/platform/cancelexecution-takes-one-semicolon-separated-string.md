---
description: ProcessEngineService.svc/CancelExecution takes processDataIds as ONE ';'-separated string, answers 200 to any other shape and cancels nothing, and cancels asynchronously
applies-to:
  - clio/Command/RuntimeVerification/ProcessRunMonitor.cs
  - clio/Common/ServiceUrlBuilder.cs
ticket: ENG-103024
date: 2026-10-12
---

**What is true** — `CancelExecution(Stream data)` reads the member `processDataIds` with
`JsonDataReader.GetStringValue()` and splits it on `';'`, so the body is
`{"processDataIds":"<id1>;<id2>"}`. A JSON array (`["<id>"]`) or an array serialized into the string
(`"[\"<id>\"]"`) is answered 200 with an empty body and cancels nothing - measured on a .NET Framework
stand on 2026-10-12 and read from the decompiled `Terrasoft.Core.ServiceModel.ProcessEngineService`. The id
is the run's process id, which is also its `SysProcessLog` and `SysProcessData` key. The cancellation is
`CancelExecutionAsync`: the log row moves to Canceled about a second after the call returns.

**Why it is this way** — the method predates JSON contracts on that service and parses its stream by hand.

**What breaks if you ignore it** — a cleanup that "cancelled" a parked run with an array body then fails to
delete the activity of its Perform task with "Item cannot be deleted because it is being used in a process.",
and nothing in the cancel answer said why. Reading the status before the cancel completes reports the run as
still running.
