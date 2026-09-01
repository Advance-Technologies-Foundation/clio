---
description: the designer stores a lookup constant on an ELEMENT PARAMETER as ConstValue + bare Guid, and only in a CHANGE-DATA COLUMN MAPPING as a [#Lookup...#] macro — so the macro found in a designer-authored schema must not be carried across to an element parameter
applies-to:
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
  - clio/Command/McpServer/Tools/ProcessDesigner/CreateBusinessProcessTool.cs
  - clio/Command/McpServer/Tools/ProcessDesigner/ModifyBusinessProcessTool.cs
ticket: ENG-96325
date: 2026-09-01
---

**What is true** — both encodings are the designer's own, and which one is correct depends on WHERE
the value sits, not on its type. Measured over 1658 designer-authored process schemas in
`PackageStore` (3936 lookup-typed element parameters carrying a value):

| Location | Source | Value | DisplayValue |
|---|---|---|---|
| Element parameter (`ActivityCategory`, `ActivityPriority`, …) | `ConstValue` | bare record Guid | the record's NAME (`To do`) |
| Change-data column mapping (`RecordColumnValues`) | `Script` | `[#Lookup.{objectUId}.{recordId}#]` | `[#Lookup.{object caption}.{record name}.{recordId}#]` |

For the three elements where a human actually picks a category, it is overwhelmingly `ConstValue`:
`ActivityUserTask` 272 of 287 (5 `Script`, 10 with no value), `OpenEditPageUserTask` 120/120,
`UserQuestionUserTask` 71/71.

Where the `[#Lookup...#]` macro DOES sit on an `ActivityCategory` parameter, it is not a designer
choice. On `CallUserTask`, `EmailUserTask` and `EmailTemplateUserTask` it always carries that
element type's own fixed category — a default inherited from the user-task schema, identical in
every schema that uses the element. Do not read those as precedent, and do not read the four
remaining counterexamples as precedent either: every `ActivityUserTask` carrying the macro
(`SysProcessElementLogDurationColumn`, `SysProcessElementLogProcessColumn`,
`SysProcessLogDurationColumn`, `SysProcessLogProcessColumn`) lives in the `ProcessTests` package and
encodes the ordinary "To do" category the other 272 store as a plain Guid. By the rule below they
have silently lost their result list; they are the defect this record describes, not an exception
to it.

Both display forms of the macro exist in the wild — with the record id as a fourth segment (253 of
407 captured) and without it (154). The longer one is the majority and the only one that stays
unambiguous when two records share a name.

**Why it is this way** — a change-data column value is a formula slot: only a `Script` value routes
through the typed generated property at run time, so a lookup there MUST be a macro (a plain
constant is stored as raw text and fails the cast). An element parameter is read as a value, and
`ActivityUserTask.GetResultParameterAllValues` — client-side in
`ActivityUserTaskPropertiesPage.js` and server-side in the `ActivityUserTask` schema — reads the
category ONLY when `SourceValue.Source == ConstValue`.

**What breaks if you ignore it** — you grep a designer-authored schema for a lookup constant, find
`[#Lookup.961e2086-….f51c4643-…#]`, and adopt it as "what the designer does". Written onto a Perform
task's `ActivityCategory` it saves green, compiles, sets the Activity's category column correctly at
run time — and silently degrades the task page's result dropdown to the default "Execute" set,
because the derivation stopped recognising the source. `Activity.AllowedResult` will not show you
this: that column derives from outgoing conditional flows, not from the category.
