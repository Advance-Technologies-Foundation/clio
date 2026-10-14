---
description: the platform data value type NAMED "File" is the BLOB type BA40CFC5, not the FileLocator A33C9252 a process File parameter is; Binary cannot be held by the flow engine
applies-to:
  - clio/Command/McpServer/Tools/ProcessDesigner/CreateBusinessProcessTool.cs
  - clio/Command/McpServer/Tools/ProcessDesigner/ModifyBusinessProcessTool.cs
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
ticket: ENG-95984
date: 2026-10-14
---

**What is true** — `DataValueTypeManager.GetInstanceByName("File")` returns `FileDataValueType`
(`BA40CFC5`, "File (BLOB)"), the type of the `Data` column of a file entity. The process designer's
"File" parameter is a different type: `FileLocatorDataValueType` (`A33C9252`), a reference to stored
content. CrtProcessBuilder from 1.6.6.99 maps `type: File` to the FileLocator name itself, so "File"
never reaches the manager as written; `FileCollection` becomes a `CompositeObjectList` with one
FileLocator item named `File`. `Binary` stays refused: it is a Stream type the flow engine cannot hold,
and on the writer path the value is dropped without an error.

**Why it is this way** — two platform types share the friendly name, and the manager resolves names,
not intent. A process parameter carries a file by reference (a locator), never by content.

**What breaks if you ignore it** — passing the word "File" through to the platform builds a BLOB-typed
process parameter: no designer page offers it, the save is green, and at run time it holds nothing a
file element can read. "Just admit Binary" is the alternative someone will propose for a "binary file"
request; it was rejected for the same reason: the value is silently lost between elements. The clio
descriptions say `File` and mean the locator; keep that meaning if the type list is ever rewritten.
