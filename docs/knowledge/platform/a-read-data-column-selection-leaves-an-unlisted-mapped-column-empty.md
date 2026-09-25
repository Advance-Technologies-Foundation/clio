---
description: a Read data element with a non-empty column selection fetches ONLY those columns, so a column of its ResultEntity that a mapping, a filter or a condition reads but the selection omits arrives null at run time with no error; an empty selection reads all columns, or exactly the mapped ones
applies-to:
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
  - clio/Command/McpServer/Tools/ProcessDesigner/CreateBusinessProcessTool.cs
ticket: ENG-91844
date: 2026-09-25
---

**What is true** — `ReadDataUserTask.AddESQColumns` (CrtProcessDesigner, `Schemas/ReadDataUserTask`) builds
the query from `EntityColumnMetaPathes` — clio's `readData.columns` — whenever that list is non-empty, and
from nothing else. Only when the list is EMPTY does it fall back: every column, or, with the
`FetchOnlyUsedColumnValues` feature on, exactly the columns something maps
(`ProcessUserTask.FindMappedEntityColumnMetaPaths`). So a three-segment reference to a column the selection
omits resolves at design time, saves, validates, and delivers null.

**Why it is this way** — the selection is the designer's "columns to read" list; the platform treats it as
the query, not as a hint. The mapped-columns fallback exists only for the no-selection case.

**What breaks if you ignore it** — `OwnerId <- ReadContact.ResultEntity.Owner` with `readData.columns:
["DoNotUseCall"]` builds green and runs with a null OwnerId - not the owner anybody chose; what the task
then falls back to was not measured - and a branch on an unlisted column reads null every time.
CrtProcessBuilder (1.6.6.27+) refuses it where it can see a NAME: a `sourceColumn`, a filter `column`, a
`[#Read.ResultEntity.Column#]` condition or Formula name (`RecordColumnReference.ResolveColumn`), and a later
`setElement readData.columns` that drops a column something still reads (`ReadDataConfigApplier`). The UId
form bypasses the write-side check — a hand-written `expression` or a modify-path `setFlowCondition` carrying
`[EntityColumn:{uid}]` is stored as given, so the column has to be listed (or the list omitted) by the author.
The primary column is always selected, so `Id` never needs listing. Traced in source, not measured on a stand.
