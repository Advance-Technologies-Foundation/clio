---
description: ProcessEngineService.svc/RunProcess resolves any version code to the family's ACTIVE version and answers success; RunSpecifiedProcessVersion runs the named version, and run-process uses it
applies-to:
  - clio/Command/RunProcessCommand.cs
  - clio/Common/ServiceUrlBuilder.cs
ticket: ENG-101880
date: 2026-10-12
---

**What is true** — `RunProcess` and `RunSpecifiedProcessVersion` are one implementation
(`ProcessEngineService.InternalRunProcess`) with one difference: the second sets
`ProcessExecutorOptions.UseSpecifiedVersion`. Without it `SchemaItemSelector.GetItem` returns
`GetActiveVersionItem(item)`, so `RunProcess` given the code of ANY family member runs the active one and
answers `completed` with that version's results. Request body, `resultParameterNames`, response, permission
check (`UseRunProcessAccessControl`) and the "Publish the ... process before starting it" refusal are
identical; the refusal names the version actually resolved. Measured 2026-10-12 on a .NET Framework stand (local core build, CrtProcessBuilder 1.6.6.93)
(`SysProcessLog.SysSchemaId` per run): with the root active, the codes of root/Custom1/Custom2 gave 107/107/107
through `RunProcess` and 107/207/307 through `RunSpecifiedProcessVersion`; with Custom2 active, 307/307/307
against 107/207/307. For the active version's own code the two routes run the same schema. The designer's Run
calls `RunSpecifiedProcessVersion` (`process-schema-designer-view-model.js`, `runSpecifiedVersion: true`). The
other `runprocess-*` records hold for both routes.

Three places still resolve to the active version whatever was launched: a sub-process element
(`BaseProcessElementFactory.CreateSubProcessInstance`), the platform's own triggers, and the background queue
(`StartProcessCommandConsumer` reads `GetActiveVersionItemByUId`; the actor path builds an executor without
`UseSpecifiedVersion`). The queue is reached only when `RunProcess` returns an empty id; no stand has done that
for this route yet, so the queue case is traced, not measured. .NET Core is not measured either; the
assembly is built for netstandard2.0 as well.

**Why it is this way** — the platform treats a version code as "this process" for every caller except the
designer, which is the one place a user deliberately runs a version before activating it.

**What breaks if you ignore it** — switching run-process back to `RunProcess` (for example to "keep the base
name launching the active version") makes a run of a new version silently execute the previous one: a caller
verifies the old logic, sees a plausible value and activates code that never ran (ENG-101880, reproduced twice).
The base name now runs the family ROOT; a caller who wants what the environment runs launches
`activeVersionName`.
