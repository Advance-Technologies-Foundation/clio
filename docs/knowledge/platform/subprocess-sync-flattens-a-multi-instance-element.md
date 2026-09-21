---
description: Synchronizing a multi-instance sub-process element rebuilds it as two collections plus three counters, with the callee's parameters as the collections' item properties - it is the platform's own idempotent refresh, not data loss, but the element still carries none of the names a contract would map onto
applies-to:
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
  - spec/eng-92707-sub-process-element/
ticket: ENG-92707
date: 2026-09-16
---

**What is true** — the `SchemaUId` setter calls the explicit-interface member, which routes through
`ProcessSchemaActivity.SynchronizeParametersInternal`. For an element with
`IsMultiInstanceModeEnabled` that method clones the two collection parameters and the three counters,
calls **`Parameters.Clear()` unconditionally**, re-runs the ordinary diff, copies every resulting
parameter into the collections' `ItemProperties`, clears again, and re-loads exactly the three counters
and the two collections. So after any synchronization such an element carries
`CompletedIterationsCount`, `TerminatedIterationsCount`, `TotalIterationsCount`,
`InputRecordCollection` and `OutputRecordCollection`, and nothing else.

**Measured 2026-09-16, and it corrects an earlier reading of this file.** The round trip is
*idempotent and non-destructive*: `LoadCollectionParameters` puts the collections' item properties back
onto `Parameters` before the diff and `FillCollectionParameters` puts them back afterwards, so the
callee's parameters and their values survive - one level down. It could not be otherwise:
`ProcessSchema.SynchronizeParameters` walks every parametrized element through the same
`IParametrizedProcessSchemaElement.SynchronizeParameters`, and `BaseProcessSchemaManager` calls it on
**every design-time read**, so a genuinely destructive rebuild would have destroyed all 61 shipped
multi-instance elements the first time anybody opened one.

**61 of the 416 sub-process elements in the shipped 7.8.0 corpus (14.7 %) are multi-instance** — the
element used as a loop body over a collection. That count was reproduced by two independent passes.

**Why it is this way** — multi-instance is derived, never declared: mapping one incoming parameter to a
data collection converts the element, and its parameter set then stops mirroring the callee. The
rebuild is how the platform re-derives the collection wrapper on every read.

**What breaks if you ignore it** — not the schema, but every contract that addresses parameters **by
name**. A multi-instance element carries none of the callee's parameter names, so a mapping, a
`typeFromElement` parameter or a drift report written in those terms addresses nothing on it, and
`describe` reports a structurally valid element whose parameter list has no relation to the called
process. CrtProcessBuilder therefore refuses such an element as a **pre-condition on
`MultiInstanceOptions != null`**, before any path that can reach the setter, and reports it as
`subProcess.multiInstance` in describe so a caller sees the refusal coming. The incidental path - a
`setElement` that touched the element for some other reason - skips it with a notice instead of
refusing the whole edit.

Source: `Terrasoft.Core/Process/ProcessSchemaActivity.cs`; measured by
`CrtProcessBuilder.Tests/SubProcessPlatformProbeTests`. Full write-up:
`spec/eng-92707-sub-process-element/eng-92707-sub-process-element-traps.md` (T-25), whose
"discarding the callee's parameters and every value mapped into them" is what this record corrects.
