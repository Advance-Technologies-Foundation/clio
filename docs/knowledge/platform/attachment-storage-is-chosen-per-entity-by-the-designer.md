---
description: the process designer picks Process file attachment storage PER ENTITY by name - an object with its own <X>File (FileLead for Lead) is dedicated storage, every other object is SysFile + RecordId + *DataEntitySchemaUId; UseSysFileInObjectFileProcessing is a client-only code flag (default true) the server never reads; 0 of 30 shipped elements use SysFile
applies-to:
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
  - clio/Command/McpServer/Tools/ProcessDesigner/CreateBusinessProcessTool.cs
  - clio/Command/McpServer/Tools/ProcessDesigner/ModifyBusinessProcessTool.cs
  - spec/eng-92719-file-processing-element/
ticket: ENG-103937
date: 2026-10-20
---

**What is true** — measured in the designer, in memory, on 2026-10-01 (UO-2, UO-3, UO-4) and 2026-10-02 (M10); the
SysFile keys and the orphan encoding are source (`EntitySchemaDesignerUtilities.js:314-325`, `:343-387`, `:424-446`,
`:509-515`, `:526-549`, `BaseFileProcessingUserTaskPropertiesPage.js:527-540`, CrtProcessDesigner 7.8.0;
`Terrasoft.Core/Process/ProcessFeatures.cs:252-272`, core 10.1.37). With `ProcessFeatures.UseSysFileInObjectFileProcessing`
on (a code `FeatureMetadata`, `IsEnabled = true`) the Process file card lists objects per entity:

* **Dedicated:** a BaseFile descendant outside the SysFile family pairs with a record object by NAME (strip `File`;
  `FileLead` pairs with `Lead`), listed as "`<Entity>` (`<file object caption>`)"; the element stores
  `*EntitySchemaUId = <X>File`, the file object's column named after AND referencing the record as the link, and no
  `*DataEntitySchemaUId`.
* **SysFile:** every other eligible object, listed by its plain caption, stores `*EntitySchemaUId = SysFile`,
  `*DataEntitySchemaUId = <record object>` and `ConnectedObjectColumnUId = SysFile.RecordId` (a Guid, no FK).
* **Orphan file objects** (`DNSGuideFile`) are listed by their plain caption too, but as their OWN file object:
  dedicated, not SysFile (a handoff note calling them SysFile rests on the caption only).

No server code reads the flag. 0 of 30 shipped elements use SysFile (corpus, 2026-10-01); 22 of 22 product Freedom UI
attachment lists store into the dedicated `<X>File` (source).

**Why it is this way** — inference, no design note found: the flag added SysFile for objects with no attachment
object of their own, and objects that had one kept it, so their record pages keep listing the files.

**What breaks if you ignore it** — writing SysFile for an object that has `<X>File` stores files where its record
page never shows them; writing a data UId on a dedicated source makes the runtime filter `AccountFile.RecordSchemaName`
and throw. The bundled CrtProcessBuilder follows the rule without reading the flag (D15), treats an orphan named
directly as dedicated (as a target it needs an explicit `linkColumn`), writes SysFile as the Object source and every
variant's target, and keeps a stored SysFile pair of an object that HAS its own `<X>File`, naming it as a describe
issue. A designer re-save writing a data UId onto a dedicated element (H-G3-1) was not reproduced (M11, DT-04).
