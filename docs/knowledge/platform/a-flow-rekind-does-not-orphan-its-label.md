---
description: a flow's diagram label is stored under a resource key built from the flow's NAME (BaseElements.<FlowName>.Caption), and CrtProcessBuilder re-derives that name on a re-kind - the row is re-materialised under the new name rather than orphaned, and an empty caption deletes it
applies-to:
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
  - clio/Command/ProcessModel/FlowLabelExpectation.cs
  - clio/Command/ProcessModel/IProcessDescriber.cs
ticket: ENG-91853
date: 2026-09-08
---

**What is true** — a sequence flow's diagram label is its `Caption`, a `LocalizableString`, so it is
not in `Schemas/<Process>/metadata.json` at all. It is extracted into the schema's resources as

```
Resources/<Process>.Process/resource.<culture>.xml
<Item Name="BaseElements.<FlowName>.Caption" Value="Above the threshold" />
```

and `SysLocalizableValue` carries the same row keyed on `SysSchemaId`. **The key is built from the
flow's `Name`** — and `CrtProcessBuilder` re-derives a generated flow's name on a re-kind
(`RenameForItsKind`, shipped in 1.4.0.66), so `DefaultFlow_Gateway_End` becomes
`ConditionalFlow_Gateway_End` and the key the label was written under no longer addresses the flow.

**It survives anyway.** Measured on a stand, through the exact route `FlowKindRules.FreeTheDefaultSlot()`
prescribes (`setFlow kind: conditional` on an existing default):

| before | after |
|---|---|
| `BaseElements.DefaultFlow_Threshold_EndLow.Caption` = `Everything else` | *(gone)* |
| — | `BaseElements.ConditionalFlow_Threshold_EndLow.Caption` = `Everything else` |

Exactly one row, keyed on the CURRENT name, and nothing left under the old one. Two mechanisms make
that work and neither mentions the other: `CarryOperatorState` CLONES `Caption` onto the replacement
object across the re-kind, and the platform re-materialises the whole resource set from the object
graph when the schema is saved. The row is therefore never "moved" — it is rewritten under whatever
name the flow has at save time, and the old key simply is not written again.

The same mechanism gives the CLEAR: assigning an empty caption writes no row, which **deletes** the
existing one rather than leaving it blank. That is why an empty `label` is the clear on `setFlow`, and
why `describe` reports a cleared label as `null` — there is nothing left to report.

**Why it is this way** — the label is text a human reads, so the platform made it localizable, and
localizable members are extracted from the schema into per-culture resources at save time. Nothing in
`metadata.json` hints that the field exists, and nothing in the rename code mentions labels, because
labels did not exist on this path when the rename was written.

**What breaks if you ignore it** — two opposite mistakes, both expensive.

1. **Guarding against the orphan that does not happen.** Reading the rename and the key together, the
   obvious conclusion is that a re-kind must strand the label, and the obvious fix is to refuse a
   re-kind on a labelled flow — or to carry the resource row by hand. Either one would break the
   remedy `FreeTheDefaultSlot()` tells callers to use, on the flows most likely to be labelled: 84.9%
   of the conditional flows in the shipped 7.8.0 corpus carry a label. The refusal would be a dead end
   invented to prevent a failure that does not occur.
2. **Concluding a label did not land because the metadata does not show it.** Diffing a clio-authored
   schema's `metadata.json` against a designer-authored one says nothing about labels — the whole
   difference is in `Resources/**`. `SysLocalizableValue` (join `SysSchema` on `SysSchemaId`) is where
   to look, and it is the only place that distinguishes "no label" from "a label under a stale key".

Related: `parameter-displayvalue-lives-in-the-schema-resources.md` records the same
metadata-versus-resources split for `ProcessSchemaParameterValue.DisplayValue`. The re-kind half is
specific to flows, because only a flow's name is re-derived.
