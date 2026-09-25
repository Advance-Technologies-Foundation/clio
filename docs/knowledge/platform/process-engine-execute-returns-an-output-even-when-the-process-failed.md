---
description: ProcessEngineService.svc/<process>/Execute?ResultParameterName=X answers with X's value even when the process then FAILED - a returned output is not evidence the run succeeded; read BusinessProcess.log / SysProcessLog
applies-to:
  - spec/eng-92711-script-task/
ticket: ENG-92711
date: 2026-09-25
---

**What is true** — `GET 0/ServiceModel/ProcessEngineService.svc/<ProcessName>/Execute?ResultParameterName=X&<In>=<v>`
returns the value parameter `X` holds when the call ends, whether or not the process completed. Measured on
the local .NET Framework stand `Creatio` (CrtProcessBuilder 1.6.6.28, 2026-09-25): a Script task ran
`Set("Total", ...)` and then threw `InvalidCastException` (it read the Lookup setting `PrimaryCulture` as a
string). The endpoint answered `42` - the value `Set` had already written - while `BusinessProcess.log`
recorded `Error in process "Name = UsrTc92711ScriptProbe ..."` with the script's stack trace.

**Why it is this way** — the endpoint reads the parameter off the process instance after `Execute` returns,
and a script task's exception ends the instance in the error state without failing the HTTP call. Nothing in
the response says which of the two happened.

**What breaks if you ignore it** — a stand test that asserts only the returned value reports a broken script
as a pass. The first measurement of this ticket did exactly that and was caught only by reading the log.
Check `C:\Windows\Temp\Creatio\<site>\0\Log\<date>\BusinessProcess.log` (a local stand) or the instance's
`SysProcessLog.StatusId` / `ErrorDescription` alongside the value.
