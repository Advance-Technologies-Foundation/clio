---
description: an OData POST answered "The request is invalid." (.NET Framework) or "The input was not valid." (.NET Core) failed model binding before the controller ran, so odata-create reports record-created=false and side-effect none; the answer names no column, so a binary column is found through $metadata (Edm.Stream)
applies-to:
  - clio/Command/McpServer/Tools/ODataCreateTool.cs
  - clio/Common/DataWriteDiagnostic.cs
ticket: ENG-104665
date: 2026-10-27
---

**What is true** — Creatio's OData POST rejects a body that fails model binding with one fixed
`error.message`, and it creates no record. The text depends on the runtime:

- .NET Framework (ASP.NET Web API): `"The request is invalid."` with an `innererror` such as
  `item : The property 'labNope' does not exist on type ...` (build `394_15918340_0922`, 2026-08-28).
- .NET Core (ASP.NET Core): `{"error":{"code":"","message":"The input was not valid.","details":[...]}}`
  with no column name (stand `se102760`, 10.2.760, 2026-10-27).

On 10.2.760 the SAME body came back for an unknown property, a malformed GUID, an array for a binary
column and a base64 string for `SysImage.Data`. A readback after each call found no record.

Binary columns are `Edm.Stream` in `$metadata`, not `Edm.Binary`: `SysImage.Data` and
`KnowledgeBaseFile.Data` are `<Property Name="Data" Type="Edm.Stream"/>`. OData v4 does not accept a
stream property value in an entity body, so ANY value for such a column is a binding failure. The
file API (`upload-file`) and the image API (`upload-image`) are the write paths for these columns.

`odata-create` therefore classifies those two messages as a confirmed no-write: `record-created=false`,
a "not inserted" `retry-guidance`, and diagnostic `write-attempted=true` with `side-effect=none`.
Because the answer names no column, the tool reads the CSDL type map ONCE per batch, after the first
such rejection, and names the `Edm.Stream`/`Edm.Binary` columns of the rejected row. A batch with no
rejection and no date-time literal never downloads the CSDL (2.7 MB on 10.2.760).

**Why it is this way** — before ENG-104665 every server error was `record-created=null` ("side effect
unknown"), because a post-insert entity event handler that throws is also reported as a failed POST.
That rule is right for `"An error has occured."` with an `innererror` (for example a required-field
check: `Type field must be filled in`), which runs inside the save. Model binding runs before the
controller action, so it cannot have written anything. Issue #1856 reported the "unknown" answer for a
binary column: the agent then had to read the entity back to learn that nothing was written.

**What breaks if you ignore it** — widening the match beyond these two exact messages (for example to
any 400 or any `error.message`) reports `record-created=false` for a row that a throwing post-insert
handler already wrote, and the caller re-sends it: the row is duplicated. Narrowing it back makes the
agent read back every rejected row and gives no pointer to `upload-file`. If a platform build changes
either message, the rejection falls back to `null` (safe but noisy); add the new text here and to
`ODataCreateTool.ModelValidationMessages` with the build it was seen on.
