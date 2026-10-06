---
description: the platform's save check refuses a malformed hand-written [#...#] meta path in a condition, expression or Formula body with a message naming nothing, and SAVES a correctly spelled one on a column the Read data element does not load, which reads empty at run time; CrtProcessBuilder 1.6.6.76 checks both itself
applies-to:
  - clio/CrtProcessBuilder/
  - clio/Command/McpServer/Tools/ProcessDesigner/ModifyBusinessProcessTool.cs
  - clio/Command/McpServer/Tools/ProcessDesigner/CreateBusinessProcessTool.cs
ticket: ENG-102114
date: 2026-10-06
---

**What is true** — measured on a .NET Framework stand (Creatio 10.1.37, CrtProcessBuilder 1.6.6.75) with every
token written through `modify-business-process`, results read from `run-process` and `SysProcessElementLog`:
- in a Script value (flow condition, `expression` mapping, Formula body, Modify data value) a meta path missing
  the dot before `[EntityColumn:…]` is REFUSED at save, but with `Unhandled error occured while generating the
  process schema. Internal error: "Value for argument "parameterUId" must be specified."` - no flow, no token,
  no index. The generator's end-anchored property regex keeps only `[EntityColumn:]` of the glued segment and
  `new ProcessParameterMapInfo(element, null)` throws (`BaseFlowSchemaGenerator.TryGetParameterMapPath`);
- a dot missing before `[Parameter:]` is refused telling the caller to add a PROCESS parameter;
- a dot missing after `[IsSchema:false]`, and the prefix-less `[Element:…].[Parameter:…]` form, save and RUN;
- a CORRECTLY spelled reference to a column outside the Read data element's `readData.columns` saves green and
  reads EMPTY at run time - a wrong branch, a null mapping, an empty Formula result - while the same column named
  `[#Read.ResultEntity.Column#]` is refused at build.

The platform itself writes one spelling only: all 9 140 meta-path tokens in the shipped 7.8.0 process metadata
are `ProcessSchemaParameter.GetMetaPath()` (prefixed). The prefix-less one is what the published guidance taught
agents to assemble from the uids describe reports, so clio-built processes store it.

**Why it is this way** — the designer picks a parameter from a list and never writes anything else, so the
generator's grammar and its error messages were never designed for hand-written tokens; and the "column is
loaded" rule exists only in the designer CLIENT (`ProcessSchemaUserTaskUtilities.getResultInfo` offers only the
selected columns). Every parser of these tokens in `Terrasoft.Core` is `internal`, so a package cannot reuse it.
From 1.6.6.76 the package checks a hand-written meta path itself (`MetaPathTokenReference`): the prefixed or the
prefix-less spelling of an item of THIS process, built from the element the reference names, and a column only
when its record delivers it; anything else is refused naming the flow or field with the correct token handed back.

**What breaks if you ignore it** — "it saved" is not evidence a hand-written reference works: the unread-column
case saves, describes back plausibly and takes the wrong branch at run time. Do not relax the package check to
"well-formed" alone, and do not drop the prefix-less spelling without first moving the guidance and checking what
clio-built processes store - every echo of such a process would be refused. Filters are a different rule (one
prefixed spelling; see `process-validation-and-runtime-parse-a-filter-reference-differently.md`).
