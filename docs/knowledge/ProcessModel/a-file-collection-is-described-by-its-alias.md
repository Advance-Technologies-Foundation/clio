---
description: describe reports a process-level collection of exactly one file item as FileCollection, not CompositeObjectList - reporting the platform name would make a replay build a shapeless collection that saves green
applies-to:
  - clio/Command/McpServer/Tools/ProcessDesigner/DescribeProcessTool.cs
  - clio/Command/ProcessModel/IProcessDescriber.cs
  - clio.tests/Command/ProcessModel/ServerProcessDescriberTests.cs
ticket: ENG-95984
date: 2026-10-14
---

**What is true** — from CrtProcessBuilder 1.6.6.98, describe reports `type: FileCollection` for a
process-level `CompositeObjectList` with no tag and exactly one item of type FileLocator, whatever the
item is called, and keeps the real item in `itemProperties`. A designer-made collection qualifies
(measured 2026-10-14: a "Collection of records" with one File column `Attachment` describes as
`FileCollection` with item `Attachment`). A rebuild from `type: FileCollection` names the item `File`;
bindings are stored by UId, so the original keeps working, and a replayed mapping that names the old
item name is refused with the item list.

**Why it is this way** — `FileCollection` is the token the write side accepts, so describe then create
round-trips. The platform name `CompositeObjectList` has no shape: the only generic way back is
`typeFromElement`, and a file collection declared by type has no source element to mirror.

**What breaks if you ignore it** — the predictable "simplification" is to report the platform type
name. A caller that replays it gets a bare `Collection` with no items: it saves green, binds nothing
per item, and every file consumer behind it reads `null` (see
`../platform/a-collection-row-is-rebuilt-from-the-target-items.md`).
