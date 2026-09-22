---
description: ApplicationSection virtual readback omits persisted card and entity binding identities; use SysModule before section deletion.
applies-to:
  - clio/Command/ApplicationSectionDeleteCommand.cs
date: 2026-09-21
ticket: clio-1634
---

**What is true** — On Creatio 10.1.585 (.NET 8, PostgreSQL), ApplicationSection SelectQuery returns the section and entity name but omits CardSchemaUId and SysModuleEntityId even when those values exist in SysModule. SysModule also uses empty strings for absent Guid-valued columns in DataService responses.

**Why it is this way** — ApplicationSection is a virtual application projection, not the persisted section table. Its query executor constructs a restricted result.

**What breaks if you ignore it** — A deletion plan silently omits the real card and entity binding, or Guid deserialization aborts discovery. Read persisted SysModule references and tolerate empty optional IDs before making any deletion decision.
