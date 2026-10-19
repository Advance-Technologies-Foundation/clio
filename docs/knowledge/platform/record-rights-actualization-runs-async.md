---
description: applying default record rules to existing records = RunProcess ObjectRecordRightsActualizationProcess {EntitySchemaUId}; it returns at once with processId + status 1 (running) and keeps going, so the end is read from SysProcessLog(processId).Status (Running ed2ae277 / Completed 815c9586 / Error f942c08d / Canceled 1be78f3e); it replaces default-origin rights and keeps manual grants
applies-to:
  - clio/Command/ObjectRights/RecordRightsActualizationClient.cs
  - clio/Command/ObjectRights/ApplyDefaultRecordRightsCommand.cs
ticket: ENG-100406
date: 2026-10-04
---

**What is true** — the designer's "Update record permissions" is the process `ObjectRecordRightsActualizationProcess`,
started through `ServiceModel/ProcessEngineService.svc/RunProcess` with the parameter `EntitySchemaUId` (the base
schema's UId; `GetAdministratedObject` returns it as `uId`). Unlike most runs (see
`runprocess-returns-no-handle-while-a-process-runs`), the call returns in ~1.5 s with a `processId` and
`processStatus: 1` while the run continues, and its `SysProcessLog` row already exists, so its `Status` lookup can be
polled by primary key. A small table finished in under 10 s (4 s end to end on 10.2.370). The run deletes the rights
rows that came from default rules (including rules deleted since) and adds rows for the current rules; a grant made
with `set-record-rights` stays. Academy calls it resource-intensive ("3 minutes or more").

**Why it is this way** — the process works through the table in chunks in the background, so the launch cannot wait
for it.

**What breaks if you ignore it** — treating the `RunProcess` answer as the outcome reports success while the run is
still going (or about to fail); re-sending a launch that timed out starts a second heavy run. So
`apply-default-record-rights` sends it once (`maxAttempts: 1`), polls `SysProcessLog`, and reports a run still going
at its deadline as "still running, process <id>" instead of a failure or a retry.
