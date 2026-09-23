---
description: de-converting a multi-instance Sub-process element KEEPS a value that arrived through a mapping row and drops an unmapped one the callee declares - GetRemovedSchemaParameters decides it, and the shipped guidance asserted the opposite
applies-to:
  - clio/Command/McpServer/Tools/ProcessDesigner/ModifyBusinessProcessTool.cs
  - docs/McpCapabilityMap.md
ticket: ENG-99856
date: 2026-09-22
---

**What is true** — a de-conversion (`subProcess.multiInstanceOptions.enabled: false`) restores the
callee's parameters from the element's two collections and then lets the platform re-derive the element
against the callee. That re-derivation is `ProcessSchemaActivity.SynchronizeParameters`, whose first
step is `GetRemovedSchemaParameters`, and it decides per parameter:

* a restored parameter that HAS a mapping row (`schema.Mappings.FindByTargetUId(target.UId)`) whose
  source still resolves is KEPT, and `UpdateParameters` re-synchronizes it. The row survives the whole
  round trip because a conversion moves the same parameter OBJECTS into the collections' item
  properties without changing their UIds, and the de-conversion clones them back out with theirs;
* a restored parameter with NO mapping row is REMOVED when its `CreatedInSchemaUId` equals the
  element's `SchemaUId` — that is, when it came from the callee, which is every restored one — and
  `FillNewSchemaParameters` then re-creates it from the callee under a FRESH UId and no value.

So a value a caller MAPPED survives; a value stored directly on a parameter with no mapping row does
not. **The exception is a callee that dropped the parameter since the conversion:** the row's source no
longer resolves, so `GetRemovedSchemaParameters` removes the parameter AND its row, value included. The
de-conversion used to carry only the two callee-failure flags out of that synchronization report, so this
loss was silent beside a notice saying the values came back; since crt-process-builder#75 (`e3fe770`,
`81f941e`) the removal is named and that sentence is qualified. Every route this contract offers (`addMapping`, `mappings[]`) writes a row, so in practice the
mapped value comes back. What genuinely does not come back is the OUTPUT collection's non-`Out` items:
`FillCollectionParameters` mints them as XOR-derived twins of `Variable` parameters with the value
already cleared, and `RestoredCalleeParameters` drops them because the original returns from the input
side.

**Why it is this way** — the platform pairs an element parameter with its source through the schema's
mapping row and by UId, never by name; the row is what makes a parameter worth keeping across a
re-derivation. Nothing special-cases the multi-instance round trip: it is the ordinary single-instance
diff, run against a root that the de-conversion has just repopulated.

**What breaks if you ignore it** — the shipped guidance told agents that "a value mapped onto the
callee's own parameters does not survive the round trip" while the server's own response notice said the
values come back "with the values mapped into them". In one turn an agent's standing instruction and the
reply to its own call asserted opposite facts, and the ticket's manual case TC-09 was written to the
wrong one, so a correct run read as a failure. The cost of believing the pessimistic version is real
work: re-mapping every per-item binding by hand after a de-conversion that preserved them.
