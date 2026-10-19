---
description: a user-task element built on the server (SchemaUId assignment, parameter sync) copies a collection parameter's NESTED items with the TEMPLATE's UIds - two server-built Process file elements share ObjectFiles.File 1ddb6de7..., which the designer never produces (it mints fresh ones); re-minting on modify would orphan mapping tokens
applies-to:
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
  - clio/Command/McpServer/Tools/ProcessDesigner/CreateBusinessProcessTool.cs
ticket: ENG-96505
date: 2026-10-15
---

**What is true** — measured with M14 on 2026-10-02 (CrtProcessBuilder 1.6.6.54, a disposable process built through
the generic `userTask` route and deleted the same day); mechanism read in `Terrasoft.Core/Process/ProcessSchemaActivity.cs:308-322`
and `ProcessSchemaParameter.cs:218-219` (core 10.1.37). The parameter sync gives each element parameter a fresh
top-level UId, but its nested items are copied with it (the `ItemProperties` copy constructor), so they keep the
TEMPLATE's UIds: `ObjectFiles.File` is `1ddb6de7…`, `Files.File` `e5903ef0…` and `ObjectFiles.ObjectFile`
`2d5e0436…` on every server-built Process file element. `IL2` is present on every element parameter and nested
item (= the element UId).

**Why it is this way** — the sync clones the template parameter whole and re-keys only the top level; the designer
mints nested UIds itself, client-side.

**What breaks if you ignore it** — two server-built elements in one process hold nested items with the same UId,
which the designer never produces; what that breaks downstream was not measured. A consumer's mapping token embeds
the nested UId, so the ENG-96505 package cut re-mints nested items on CREATE only: re-minting on modify would
orphan every mapping already pointing at them.
