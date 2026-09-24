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
CrtProcessBuilder refuses such a `sourceColumn`, filter `column` or `[#Read.ResultEntity.Column#]` at the
write that names it (`RecordColumnReference.ResolveColumn`, 1.6.6.24+). It does NOT re-check when a later
`setElement readData.columns` narrows the list under an existing reference — that direction is still silent.
Traced in source, not measured on a stand.
