---
description: a hand-written [#...#] meta path in a condition, expression or Formula body - a dot missing before [EntityColumn:] is refused at save with a message naming nothing, one missing after the prefix saves and runs, and a correctly spelled one on a column the Read data element does not load SAVES and reads empty at run time, and so does one with UPPER-case GUIDs; from 1.6.6.77 CrtProcessBuilder checks a hand-written meta path itself in a condition, a mapping, connection or value expression, a recordId and a Formula body, and from 1.6.6.84 stores it with its GUIDs lower-cased
applies-to:
  - clio/CrtProcessBuilder/
  - clio/Command/McpServer/Tools/ProcessDesigner/ModifyBusinessProcessTool.cs
  - clio/Command/McpServer/Tools/ProcessDesigner/CreateBusinessProcessTool.cs
ticket: ENG-102114
date: 2026-10-07
---

**What is true** — measured on a .NET Framework stand (Creatio 10.1.37, CrtProcessBuilder 1.6.6.75) with every
token written through `modify-business-process`, results read from `run-process` and `SysProcessElementLog`:
- in a Script value (flow condition, `expression` mapping, Formula body, Modify data value) a meta path missing
  the dot before `[EntityColumn:…]` is REFUSED at save, but with `Unhandled error occured while generating the
  process schema. Internal error: "Value for argument "parameterUId" must be specified."` - no flow, no token,
  no index. The generator's end-anchored property regex keeps only `[EntityColumn:]` of the glued segment and
  `new ProcessParameterMapInfo(element, null)` throws (`BaseFlowSchemaGenerator.TryGetParameterMapPath`);
- a dot missing between `[Element:…]` and `[Parameter:]` is refused telling the caller to add a PROCESS parameter
  (for a process parameter that dot is the one after the prefix, below);
- a dot missing after `[IsSchema:false]`, and the prefix-less `[Element:…].[Parameter:…]` form, save and RUN;
- a CORRECTLY spelled reference to a column outside the Read data element's `readData.columns` saves green and
  reads EMPTY at run time - a wrong branch, a null mapping, an empty Formula result - while the same column named
  `[#Read.ResultEntity.Column#]` is refused at build;
- a meta path whose GUIDs are UPPER case also saves green and reads EMPTY at run time - the branch goes the wrong
  way - and the designer shows the raw token instead of the parameter's caption (QA, 2026-10-07, on 1.6.6.79). The
  run time and the designer match the GUID text case-sensitively, against the lower case `GetMetaPath` writes: all
  189 907 GUID segments in the shipped PackageStore metadata are lower case.

The platform itself writes one spelling only: all 9 140 meta-path tokens in the shipped 7.8.0 process metadata
are `ProcessSchemaParameter.GetMetaPath()` (prefixed). The prefix-less one is what the published guidance taught
agents to assemble from the uids describe reports, so clio-built processes store it.

**Why it is this way** — the designer picks a parameter from a list and never writes anything else, so the
generator's grammar and its error messages were never designed for hand-written tokens; and the "column is
loaded" rule exists only in the designer CLIENT (`ProcessSchemaUserTaskUtilities.getResultInfo` offers only the
selected columns). Every parser of these tokens in `Terrasoft.Core` is `internal`, so a package cannot reuse it.
From 1.6.6.77 the package checks a hand-written meta path itself (`MetaPathTokenReference`): the prefixed or the
prefix-less spelling of an item of THIS process, built from the element the reference names, and a column only
when its record delivers it; anything else is refused naming the flow or field with the correct token handed back.
The GUIDs inside braces are compared case-insensitively, and from 1.6.6.84 every surface the check covers stores
an accepted reference with them lower-cased - the one rewrite the check makes. The check does NOT cover the Send
email recipient and template-entity expressions, the performer and approver contact formulas or the access-rights
grantee: those store a caller's formula as written, so a misspelling or an upper-case GUID there still reads empty. Only the reference's own GUIDs change: a
`Lookup` macro's record ids and GUIDs in string literals are data and keep their case.

**What breaks if you ignore it** — "it saved" is not evidence a hand-written reference works: the unread-column
case saves, describes back plausibly and takes the wrong branch at run time. Do not relax the package check to
"well-formed" alone, and do not drop the prefix-less spelling without first moving the guidance and checking what
clio-built processes store - every echo of such a process would be refused. Do not "keep the caller's text" for
the GUID case either: before 1.6.6.84 an upper-case GUID was stored as written and read empty. Values stored
that way are not repaired; re-sending them normalises them. Filters are a different rule (one
prefixed spelling; see `process-validation-and-runtime-parse-a-filter-reference-differently.md`).

The check reads the caller's text, and some edits re-send text the designer stored: `setFlow` needs `kind`, so
even a relabel re-sends the condition, and a Modify data `values` array replaces the whole set. A designer
condition or value that reads an unread column - or names an element the process no longer has - is therefore
refused on such an unrelated edit. Kept deliberately (gate-3 review, ENG-102114): the reference reads empty at
run time, or fails the platform's save anyway, so letting the edit through would re-store a defect silently.
