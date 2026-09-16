---
description: Assigning ProcessSchemaSubProcess.SchemaUId synchronously runs the platform's own add/drop/preserve parameter diff, it re-runs on every design-time read, and it is a silent no-op in three cases including a self-reference
applies-to:
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
  - spec/eng-92707-sub-process-element/
ticket: ENG-92707
date: 2026-09-16
---

**What is true** — a sub-process element does not need a parameter-synchronization algorithm written
for it. `ProcessSchemaSubProcess.SchemaUId`'s **setter** calls
`((IParametrizedProcessSchemaElement)this).SynchronizeParameters()`, and
`ProcessSchemaActivity.SynchronizeParameters()` is the three-phase diff the ticket asks for:
`GetRemovedSchemaParameters` → `RemoveSchemaParameters` → `UpdateParameters` →
`FillNewSchemaParameters`, run against the called process's `Schema.Parameters`
(`ProcessSchemaSubProcess.GetSchemaParameters()` returns them). Pairing is by the caller schema's
`ProcessSchemaMapping` rows, not by name; name is only the adoption fallback in the add phase.

`BaseProcessSchemaManager.FindDesignItem` and `GetItemFromMetaData` re-run it on **every read of a
process schema**, so a "stale" sub-process element is not a state the object model will hand you.

Three ways it is a **silent** no-op (`GetCanSynchronizeParameters`): the host schema has no `UId`, the
element has no `UId` yet, or `SchemaUId` equals the host schema's UId — a process calling itself.
And assigning `SchemaUId` while the element is still detached throws, because
`ClearParametersSourceValue` dereferences `ParentMetaSchema.UId` with no null guard; the copy
constructor assigns it too, so `Clone()` throws for the same reason.

`ApplyMetaDataValue` writes the FIELD (`_schemaUId`), so deserializing metadata — and a whole-schema
save through `ProcessSchemaManagerService.Post` — bypasses the setter and does **not** synchronize.

**Why it is this way** — the Pre-configured page needed its own 545-line synchronizer because a page's
parameters are unreachable through `GetSchemaParameters()`; a user task's `SchemaUId` points at the
user-task schema. A sub-process references the callee through the element's own `SchemaUId`, so the
platform's generic activity diff already has the right source collection.

**What breaks if you ignore it** — porting `PreconfiguredPageParameterSync` produces a second,
divergent diff over the same data, and the platform's runs afterwards anyway on every read. Assigning
`SchemaUId` inside `Create()` — the shape every existing user-task handler uses — throws an NRE the
moment the callee declares a parameter, which is every real callee. Writing the element for a process
that calls itself produces an element with zero parameters and no complaint. And a re-sync implemented
as "load, compare" reports nothing every time, because the load already converged it: the only window
is a snapshot taken before the write.

Source: `Terrasoft.Core/Process/ProcessSchemaSubProcess.cs`, `ProcessSchemaActivity.cs`. Full
write-up with line numbers: `spec/eng-92707-sub-process-element/`.
