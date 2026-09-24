---
description: A value on a sub-process element parameter survives the next sync only when CreatedInSchemaUId differs from SourceValue.ModifiedInSchemaUId AND the direction is In or Variable, so the Preconfigured-page stamping rule erases every value here
applies-to:
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
  - spec/eng-92707-sub-process-element/
ticket: ENG-92707, ENG-99856
date: 2026-09-16
---

**What is true** — `ProcessSchemaSubProcess.ClearParametersSourceValue()` runs after **every**
synchronization and wipes a parameter's `SourceValue` unless both hold:

* `parameter.CreatedInSchemaUId != parameter.SourceValue.ModifiedInSchemaUId` — the provenance test,
  which is not a value comparison at all; and
* the direction is `In` or `Variable` (`ProcessSchemaParameterDirectionUtils.GetIsAssignable`). `Out`
  and `Internal` are cleared regardless, and `GlobalAppSettings.FeatureClearSubProcessParametersSourceValue`
  defaults to `true`.

So a synced element parameter must keep the **callee's** schema UId as its `CreatedInSchemaUId`, while
a value the caller writes carries the **host** schema UId in `SourceValue.ModifiedInSchemaUId`.

Measured against the shipped 7.8.0 corpus: of 1 650 element parameters, `A3 == CK4` (the callee) on
1 321; of the 329 that differ, 293 sit on multi-instance elements whose collection and counter
parameters are legitimately caller-created — on the single-instance path the rule holds 1 321 / 1 357
= 97.3 %. Of 629 parameters carrying a value, 581 (92.4 %) stamp `L8.GS5` with the caller.

**Why it is this way** — the rule is inverted relative to the Pre-configured page, where
`PreconfiguredPageParameterSync` deliberately re-asserts `existing.CreatedInSchemaUId = schema.UId`
because the page runtime delivers a value only when that stamp equals the process schema UId. For a
sub-process the same assignment makes the provenance test false for every parameter.

**What breaks if you ignore it** — copy the page's stamping rule onto a sub-process element and the
next synchronization erases every mapped value. Nothing throws, nothing warns, and `describe` reports a
healthy element — and because the platform re-syncs on every design-time read, "the next
synchronization" is the next time anything opens the schema. The same applies to writing a value onto
an `Out` or `Internal` parameter: the call succeeds and the value is gone by the next read, which is
why a mapping onto a non-assignable direction must be refused at write time.

**ENG-99856 made this rule load-bearing for writes, not only for reads.** A multi-instance conversion
moves the element's existing parameters into the input collection before handing the element to the
platform, and a de-conversion moves them back; a retarget and a re-synchronization do both around the
caller's work. Every one of those hops must carry each parameter's `CreatedInSchemaUId` and its
`SourceValue.ModifiedInSchemaUId` ACROSS UNCHANGED. Re-stamping either one - which is the natural thing
to do when you are building a parameter rather than moving one - makes the provenance test false and the
next synchronization erases the value, silently, exactly as this record describes. That is why the
applier re-converts around the SAME five parameter objects instead of minting new ones: their UIds and
their stamps both survive the round trip. The corpus figure above already anticipated this shape - the
293 differing parameters sit on multi-instance elements whose collections and counters are legitimately
caller-created.

Source: `Terrasoft.Core/Process/ProcessSchemaSubProcess.cs`,
`Terrasoft.Core/Process/ProcessSchemaParameterDirectionUtils.cs`,
`Terrasoft.Core/GlobalAppSettings.cs`. Corpus method and figures:
`spec/eng-92707-sub-process-element/eng-92707-sub-process-element-serialization-capture.md`.
