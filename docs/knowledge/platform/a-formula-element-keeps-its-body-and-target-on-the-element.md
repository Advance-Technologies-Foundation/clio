---
description: a Formula element stores its formula in `body` and the parameter it writes, as a bare UId, in `resultParameterMetaPath` - on the flow element itself; its `parameters` array is empty
applies-to:
  - clio/Command/ProcessModel/Schema.cs
  - clio/Command/ProcessModel/ProcessResultSources.cs
ticket: ENG-105475
date: 2026-11-16
---

**What is true** — in a process schema read with `ProcessSchemaRequest`, a Formula element
(`ProcessSchemaFormulaTask`, manager `D334D28F-...`) carries `"body": "[#...[Element:{...}].[Parameter:{...}]#]"` and
`"resultParameterMetaPath": "<parameter UId>"` as members of the flow element, and an empty `parameters` array
(measured 2026-11-16 on env `Creatio`, CrtProcessBuilder 1.6.6.172, on a Formula element built by
create-business-process).

**Why it is this way** — the platform serializes the element's own properties next to its identity; only elements
with a parameter list (user tasks, web service calls) keep their values in `parameters`.

**What breaks if you ignore it** — code that follows where a value comes from through the element parameters finds
nothing for a Formula element, so a result a formula copied from a web service response is treated as clean and
run-process returns the echo in clear.
