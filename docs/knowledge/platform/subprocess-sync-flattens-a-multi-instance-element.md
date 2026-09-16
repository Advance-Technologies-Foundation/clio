---
description: The self-reference and empty-UId guards sit one method too deep, so any synchronization of an ALREADY multi-instance sub-process element clears its parameters and rebuilds it as two collections plus three counters
applies-to:
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
  - spec/eng-92707-sub-process-element/
ticket: ENG-92707
date: 2026-09-16
---

**What is true** — the `SchemaUId` setter calls the explicit-interface member, which routes through
`ProcessSchemaActivity.SynchronizeParametersInternal`. For an element with
`IsMultiInstanceModeEnabled`, that method clones the two collection parameters and the three counters,
calls **`Parameters.Clear()` unconditionally**, and only then delegates to the inner
`SynchronizeParameters` where `GetCanSynchronizeParameters()` lives.

So the three guards documented in
[the setter record](subprocess-schemauid-setter-runs-the-parameter-sync.md) — empty host UId, empty
element UId, self-reference — **do not protect a multi-instance element at all**. Any code path that
reaches the setter or the interface method rebuilds it as `InputRecordCollection` +
`OutputRecordCollection` + three counter parameters, discarding the callee's parameters and every value
mapped into them.

**61 of the 416 sub-process elements in the shipped 7.8.0 corpus (14.7 %) are multi-instance** — the
element used as a loop body over a collection. That count was reproduced by two independent passes.

**Why it is this way** — multi-instance is derived, never declared: mapping one incoming parameter to a
data collection converts the element, and its parameter set then stops mirroring the callee. The
rebuild is how the platform re-derives the collection wrapper; the guard was written for the
single-instance path and never moved.

**What breaks if you ignore it** — a `setElement` that merely touches such an element flattens it,
silently, and `describe` afterwards reports a structurally valid element with a plausible parameter
list. The mapped values are gone and nothing says so. A refusal therefore has to be a **pre-condition
on `IsMultiInstanceModeEnabled`, evaluated before any code path that can reach the setter** — including
a drift snapshot and any applier that runs after `GetDesignInstance` — not a validation of what the
caller asked for. Refusing only "you asked to map a collection" leaves the destructive path wide open.

Source: `Terrasoft.Core/Process/ProcessSchemaActivity.cs`. Full write-up:
`spec/eng-92707-sub-process-element/eng-92707-sub-process-element-traps.md` (T-25).
