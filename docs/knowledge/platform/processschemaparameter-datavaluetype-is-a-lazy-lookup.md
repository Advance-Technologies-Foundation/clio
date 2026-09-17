---
description: ProcessSchemaParameter.DataValueType resolves lazily through the owner's manager and answers null when that manager is out of reach, so comparing type NAMES can silently report no change
applies-to:
  - clio.tests/Command/ProcessModel/ServerProcessDescriberTests.cs
ticket: ENG-92707
date: 2026-09-17
---

**What is true** — `ProcessSchemaParameter.DataValueTypeUId` is the stored value (metadata property
`L1`). `ProcessSchemaParameter.DataValueType` beside it is not stored at all: its getter resolves the UId
through `DataValueTypeManager`, which it obtains from `ProcessSchema ?? ParentMetaSchema as
BaseProcessSchema`. Where that chain yields nothing the property answers `null`, and setting the object
writes the UId while setting the UId clears the cached object.

**Why it is this way** — the platform keeps schema metadata small and resolves manager-owned objects on
demand; the parameter carries the identity, not the instance.

**What breaks if you ignore it** — code that compares two parameters by `DataValueType?.Name` compares
`null` with `null` wherever the manager is out of reach, which is equality, so an actual type change is
reported as no change. Nothing throws and no test built on an in-memory schema can see it, because there
the manager IS reachable and the names resolve. CrtProcessBuilder's sub-process drift snapshot had this
shape and `PreconfiguredPageParameterSync` did not — it compares `DataValueTypeUId` with a
`!= Guid.Empty` guard, which is the pattern to copy. Note the inverse trap in the same area: the comment
on `ProcessParameterService.ToDescribeParameter` states the object property "would always be null" on a
read-back schema, and that is too strong — it is null only when the manager cannot be reached.
