---
description: The native CheckUniqueSchemaName rejects the base name required for an entity replacement, even though CreateNewSchema plus AssignParentSchema prepares that replacement successfully
applies-to:
  - clio/Command/EntitySchemaDesigner/RemoteEntitySchemaCreator.cs
ticket: "#1211"
date: 2026-09-11
---

**What is true** — On Creatio 10.1.585 (.NET 8, PostgreSQL), the entity designer's
`CreateNewSchema` with `extendParent: true`, followed by `AssignParentSchema` for
`Account`, returns a new same-name replacing schema in the requested package.
`CheckUniqueSchemaName` nevertheless rejects `Account` because the base exists.

**Why it is this way** — The name check is global, whereas a replacement deliberately
shares its parent's name. Its duplicate boundary is the destination package.

**What breaks if you ignore it** — Reusing the ordinary global uniqueness guard makes
every legitimate replacement fail before the native designer can prepare it. Omitting
duplicate checks altogether would allow repeated create-only requests to reach save;
check the target package instead, and use sync reconciliation for an existing replacement.
