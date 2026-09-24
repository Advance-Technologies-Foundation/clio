---
description: Synchronizing a multi-instance sub-process element rebuilds it as two collections plus three counters, with the callee's parameters as the collections' item properties - it is the platform's own idempotent refresh, not data loss, and since ENG-99856 clio DRIVES that rebuild instead of refusing the element; the callee's names live one level down, addressed by a dotted path
applies-to:
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
  - spec/eng-92707-sub-process-element/
ticket: ENG-92707, ENG-99856
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
name**. A multi-instance element carries none of the callee's parameter names at its ROOT, so a mapping,
a `typeFromElement` parameter or a drift report written in those terms addresses nothing on it: the
callee's contract is one level down, in the collections' `ItemProperties`, and a per-item value is
addressed by a DOTTED path (`InputRecordCollection.<Param>`).

**This paragraph replaced a refusal, and the difference matters.** Until ENG-99856 CrtProcessBuilder
refused such an element outright, as a pre-condition on `MultiInstanceOptions != null` before any path
that could reach the setter. It no longer does. The rebuild described above is now DRIVEN rather than
avoided: a conversion, a de-conversion, a retarget and a re-synchronization all de-convert the element,
let the ordinary applier work against a single-instance one with every guard it carries, and re-convert
it around the SAME five parameter objects so their UIds survive. `EnsureNotMultiInstance` still exists
and still refuses — but only on the LEGACY path, and its message says so, naming the `subProcess` block
on `setElement` as the route that works. Do not read that refusal as the product's answer.

One consequence of the rebuild is load-bearing for anyone driving it: because `Parameters.Clear()` runs
unconditionally, a FIRST conversion must move the element's existing parameters into the input
collection BEFORE handing the element to the platform, or their mapped values are gone with no error
anywhere. That is a separate record —
`platform/multi-instance-conversion-must-move-parameters-before-the-rebuild.md`.

Source: `Terrasoft.Core/Process/ProcessSchemaActivity.cs`; measured by
`CrtProcessBuilder.Tests/SubProcessPlatformProbeTests`. Full write-up:
`spec/eng-92707-sub-process-element/eng-92707-sub-process-element-traps.md` (T-25), whose
"discarding the callee's parameters and every value mapped into them" is what this record corrects.
