---
description: the process designer writes a SysFile Process file record scope (SysFile.RecordId = X) as a lookup InFilter (filterType 4, dataValueType 10, referenceSchemaName = the record object, in the edit form only) when the record object has a primary display column, and as a Guid CompareFilter (filterType 1, dataValueType 0) when it has none - per object, not per element
applies-to:
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
  - clio/Command/McpServer/Tools/ProcessDesigner/DescribeProcessTool.cs
  - clio.mcp.e2e/ProcessFileElementToolE2ETests.cs
ticket: ENG-103937
date: 2026-10-20
---

**What is true** - measured in the designer on 2026-10-20 (M7, CrtProcessDesigner 7.8.0, `UsrFpSc1SfCapture`):
`SysFile.RecordId` is a plain Guid with no reference object, and the filter editor retypes it as a LOOKUP of the
record object only when that object has a `primaryDisplayColumn` (`ObjectFileProcessingUserTaskPropertiesPage`
`_initFilterManagerConfig`). App-created `UsrFpDoc` (`UsrName`) got `Terrasoft.InFilter`, `dataValueType` 10,
`referenceSchemaName: "UsrFpDoc"` and `rightExpressions`; Account address (none) got `Terrasoft.CompareFilter`,
`dataValueType` 0 and `rightExpression`, for a constant and a parameter alike. The RUNTIME form of the lookup leaf
carries no `referenceSchemaName`; only the edit form names the object. The bundled CrtProcessBuilder writes the same
two shapes and reads the reference back from the edit form.

**Why it is this way** - the designer's lookup picker needs a display column to render a record, so without one it
falls back to a Guid text input.

**What breaks if you ignore it** - a comparison, guide or test that expects one shape for every SysFile element
fails on half of them; a reader of the runtime form alone cannot tell which object a lookup scope names; a
hand-written lookup scope on an object with no display column is a shape the designer never produces.
