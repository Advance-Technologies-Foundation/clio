---
description: RunProcess answers a run that failed inside an element with only "check the process log" and no errorCode; the exception is on the run's SysProcessLog row, written before RunProcess returns
applies-to:
  - clio/Command/RunProcessCommand.cs
  - clio/Command/ProcessRunLogReader.cs
ticket: ENG-92711
date: 2026-10-02
---

**What is true** - when an element throws while a process runs (a script task's own exception, a formula
that cannot be computed), the platform swallows it in the element (`ProcessFlowElement.HandleExecutionError`
logs `exception.ToString()` and returns), so `descriptor.Exception` stays null and `SetErrorInfoWhenIsNeeded`
answers with the resource text "An error has occurred during the process execution. Please check the process
log for details" and no `errorCode`. The exception itself is written to `SysProcessLog.ErrorDescription` of the
run's root row (`Id` = the `processId` RunProcess returns), synchronously, before RunProcess returns; the first
line is `Type: message`, the rest the stack. Measured on a .NET Framework stand: a formula dividing by zero
logs `System.InvalidOperationException: Unable to compute expression ...`.

**Why it is this way** - the element-level handler is how a process keeps its log and its error status;
RunProcess builds its answer from the descriptor afterwards and has nothing but the status to go on.

**What breaks if you ignore it** - a caller told only "check the process log" cannot fix the script that
threw, and an agent re-runs or edits blindly. Reading the log needs the run's `processId`, which a background
launch does not return, and DataService read rights on `SysProcessLog`, so the read is best effort: when it
fails, the platform's answer has to stay.
