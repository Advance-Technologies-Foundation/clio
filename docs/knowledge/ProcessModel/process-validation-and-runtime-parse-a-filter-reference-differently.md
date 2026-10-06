---
description: platform process validation scans a filter's parameter reference with a looser grammar than the runtime, so a wrapped, malformed or dangling filter expression saves green and fails at run time
applies-to:
  - clio/CrtProcessBuilder/
  - clio/Command/McpServer/Tools/ProcessDesigner/ModifyBusinessProcessTool.cs
ticket: ENG-102110
date: 2026-10-06
---

**What is true** — the platform reads a process filter's right-hand parameter reference twice, with two
different grammars. Save-time validation (`ParameterValuesValidationRule` -> `FlowSchemaGenerator.TryGenerate`,
the `ConstValue` branch, `BaseFlowSchemaGenerator.FillDataSourceFilterMapPaths`) runs UNANCHORED regexes over the
filter JSON and picks a well-formed `[Element:{..}].[Parameter:{..}](.[EntityColumn:{..}])` fragment out of
anywhere in the string. The runtime (`ProcessFilterFactory` -> `ProcessComponentSet.GetParameterValueByMetaPath`
-> `TryGetParameterMapPath`) splits the WHOLE string on `.` and matches each segment end-anchored. Measured on a
.NET 8 stand on 2026-10-06: a `[#...#]`-wrapped parameter reference saves and then fails the element with
`FormatException`; the same wrapper around a column reference saves and completes with 0 rows and no error; a
reference to a parameter that does not exist saves (validation skips it) and throws `ItemNotFoundException`; only
an unknown column on an existing parameter is refused at save.

**Why it is this way** — the platform's own designer never writes anything but the canonical
`ProcessSchemaParameter.GetMetaPath()` spelling (a parameter is picked from a list), so the two grammars never
disagree on designer-made processes. Every disagreement comes from a value written by something else. That is
why CrtProcessBuilder 1.6.6.72 accepts a filter `expression` only as the exact canonical token of a reference
that resolves, instead of relying on the platform's save validation.

**What breaks if you ignore it** — "Process validation passed" is not evidence that a filter reference works.
A process built by an agent or a script can save green, read back plausibly through `describe-business-process`
and still fail, or silently act on no records, at the first real run. Do not relax the package's exact-spelling
check on the strength of the platform's validation, and when a filter reference looks suspect, run the process.
