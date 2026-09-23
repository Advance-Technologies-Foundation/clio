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

`BaseProcessSchemaManager.FindDesignItem` and `GetItemFromMetaData` re-run it on every **design-time**
read. A RUNTIME read does not — no runtime getter calls `SynchronizeParameters()` — so a stale
sub-process element IS a state the object model hands you, through
`ProcessSchemaManager.FindInstanceByUId` / `FindInstanceByName`. That is the difference between the two
clio paths: `ProcessModifyHandler` always takes `GetDesignInstance` and sees a converged element, while
`ProcessSchemaRepository.LoadForDescribe` can report the stale name -
see `subprocess-insync-depends-on-the-schema-instance.md` for when, which is not restated here - and
reports the stale name with `inSync: false`. An earlier revision of this record said the stale state was
unreachable; a stand measured it on 2026-09-17.

Three ways it is a **silent** no-op (`GetCanSynchronizeParameters`): the host schema has no `UId`, the
element has no `UId` yet, or `SchemaUId` equals the host schema's UId — a process calling itself.
And assigning `SchemaUId` while the element is still detached throws, because
`ClearParametersSourceValue` dereferences `ParentMetaSchema.UId` with no null guard.

The copy constructor assigns `SchemaUId` in its own body too, and **that one does not throw** - measured
2026-09-16, correcting T-26 in the analysis. `MetaItem(MetaItem source)` copies `ParentMetaSchema` from
the source first, so `Clone()` is safe wherever the original was. Only a copy taken from an element that
never had a `ParentMetaSchema` fails, and that is the detached case above rather than a copy-specific
one.

`ApplyMetaDataValue` writes the FIELD (`_schemaUId`), so deserializing metadata — and a whole-schema
save through `ProcessSchemaManagerService.Post` — bypasses the setter and does **not** synchronize.

**Why it is this way** — the Pre-configured page needed its own 545-line synchronizer because a page's
parameters are unreachable through `GetSchemaParameters()`; a user task's `SchemaUId` points at the
user-task schema. A sub-process references the callee through the element's own `SchemaUId`, so the
platform's generic activity diff already has the right source collection.

Measured by `CrtProcessBuilder.Tests/SubProcessPlatformProbeTests`, which pins the detached throw, the
copy constructor NOT throwing, and the mechanism that separates them.

**What breaks if you ignore it** — porting `PreconfiguredPageParameterSync` produces a second,
divergent diff over the same data, and the platform's runs afterwards anyway on every read. Assigning
`SchemaUId` inside `Create()` — the shape every existing user-task handler uses — throws an NRE the
moment the callee declares a parameter, which is every real callee. Writing the element for a process
that calls itself produces an element with zero parameters and no complaint. And a re-sync implemented
as "load, compare" reports nothing every time, because the load already converged it: the in-memory
window is a snapshot taken before the write. The other window is the database - what the caller had STORED -
which is how the caption report finds a caption the callee changed between two requests (ENG-100077,
CrtProcessBuilder 1.6.6.17).

Source: `Terrasoft.Core/Process/ProcessSchemaSubProcess.cs`, `ProcessSchemaActivity.cs`. Full
write-up with line numbers: `spec/eng-92707-sub-process-element/`.

**One more way it produces nothing, added 2026-09-17** — the setter resolves the called process through
`GetInstanceFromMetaData` (a design-time instance is deserialized from metadata), while a reader that
loads it with `GetInstanceByUId` asks a different question. If those two disagree — the process reads
fine by UId and does not resolve from metadata — the write is accepted, `GetSchemaParameters()` answers
with an EMPTY collection, and the platform then removes every parameter and mapping row the element
carried. Probing readability with the reader before the write does not cover it, and the check cannot be
moved onto `element.Schema` beforehand either, because until the assignment the element still references
the PREVIOUS callee. The only place to catch it is after the write and before `Save`, by comparing the
element's parameters against what the callee was read to declare. Left unchecked it is silent on a first
selection and actively MISLEADING on a retarget, where every previously carried parameter appears as
removed by the new callee.
