---
description: the process designer writes a SysFile Process file record scope (SysFile.RecordId = X) as a lookup InFilter (filterType 4, dataValueType 10, referenceSchemaName = the record object) ONLY for a constant on a record object with a primary display column; a parameter (any meta-path) on such an object is a lookup CompareFilter (filterType 1, dataValueType 10, single rightExpression), and an object with no display column gets a Guid CompareFilter (dataValueType 0) for both
applies-to:
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
  - clio/Command/McpServer/Tools/ProcessDesigner/DescribeProcessTool.cs
  - clio.mcp.e2e/ProcessFileElementToolE2ETests.cs
ticket: ENG-104198
date: 2026-10-24
---

**What is true** - measured in the designer on 2026-10-24 (stand with CrtProcessBuilder 1.6.6.119, process
`UsrScopeShapeProbe`; each of the four filters was set and saved in the Process Designer, then read from
`SysSchema.MetaData`):

| Record object | Right side | Edit form | Runtime form |
| --- | --- | --- | --- |
| `UsrFpDoc` (has `primaryDisplayColumn`) | constant record | `Terrasoft.InFilter`, filterType 4, dataValueType 10, `referenceSchemaName: "UsrFpDoc"`, `rightExpressions` | filterType 4, `rightExpressions` |
| `UsrFpDoc` | process parameter | `Terrasoft.CompareFilter`, filterType 1, dataValueType 10, `referenceSchemaName: "UsrFpDoc"`, one `rightExpression` (parameter dataValueType 26, meta-path) | filterType 1, one `rightExpression` |
| Account address (no display column) | constant Guid | `Terrasoft.CompareFilter`, filterType 1, dataValueType 0, one `rightExpression` | filterType 1 |
| Account address | process parameter | `Terrasoft.CompareFilter`, filterType 1, dataValueType 0, one `rightExpression` (meta-path) | filterType 1 |

`SysFile.RecordId` is a plain Guid with no reference object; the filter editor retypes it as a LOOKUP of the record
object only when that object has a `primaryDisplayColumn` (`ObjectFileProcessingUserTaskPropertiesPage`
`_initFilterManagerConfig`). The lookup picker is multi-select, so a constant becomes an InFilter; a parameter is a
single value, so it stays a CompareFilter even when retyped. The runtime form carries no `referenceSchemaName`; only
the edit form names the object. The first measurement (M7, 2026-10-20, ENG-103937) covered three of the four cells and
missed the parameter on a display-column object, so CrtProcessBuilder up to 1.6.6.119 wrote an InFilter there; the
ENG-104198 package writes the designer's CompareFilter and still reads the InFilter that older builds stored.

**Why it is this way** - the designer's lookup picker needs a display column to render a record, so without one it
falls back to a Guid text input; and a parameter right side is one value, never a list.

**What breaks if you ignore it** - a comparison, guide or test that expects one shape per object fails on the
parameter cell; a reader of the runtime form alone cannot tell which object a lookup scope names; a writer that emits
an InFilter for a parameter produces a shape the designer never does, and a reader that accepts only the new
CompareFilter stops recognising scopes saved by older builds.
