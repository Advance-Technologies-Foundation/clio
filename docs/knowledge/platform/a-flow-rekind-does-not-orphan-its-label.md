---
description: a flow's diagram label is stored under a resource key built from the flow's NAME (BaseElements.<FlowName>.Caption), and CrtProcessBuilder re-derives that name on a re-kind - the label is re-materialised under the new name and survives; the old-name row is deleted before platform build 10.1.409 and left as an orphan from it; an empty caption deletes the row
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

The label keeps working because `CarryOperatorState` CLONES `Caption` onto the replacement object across
the re-kind, and the save writes the caption under whatever name the flow has at save time. The row is
never "moved". What happens to the OLD key depends on the platform build: that measurement (2026-09-08)
ran before build 10.1.409, whose save rewrote the whole resource set, so the old row went. From 10.1.409
a save rewrites only the keys it carries and never judges a `BaseElements.*` key obsolete
(`a-schema-save-keeps-a-removed-element-parameters-resource-rows.md`), so the old-name row stays as an
orphan - by that code path; not measured for flows. It is harmless while no later flow takes the old name.

The CLEAR: an empty caption **deletes** the existing row rather than leaving it blank - measured before
10.1.409; from it, the key-scoped delete removes a key the save carries with an empty value (describe reads
the cleared label back as `null` on 10.2.984; the row itself is not measured there). That is why an empty `label` is the clear on `setFlow`, and
why `describe` reports a cleared label as `null` — there is nothing left to report.

**`removeFlow`.** Measured before 10.1.409: removing a labelled flow took
`BaseElements.ConditionalFlow_Threshold_EndHigh.Caption` with it. From 10.1.409 the removed flow's row
stays as an orphan, for the same reason as the re-kind's old key.

So the rule to carry: **the label follows the flow's current name**, and a clear is an empty caption.
Whether the row under a name the flow no longer has is deleted is the platform build's, not the
builder's.

**Why it is this way** — the label is text a human reads, so the platform made it localizable, and
localizable members are extracted from the schema into per-culture resources at save time. Nothing in
`metadata.json` hints that the field exists, and nothing in the rename code mentions labels, because
labels did not exist on this path when the rename was written.

**What breaks if you ignore it** — two opposite mistakes, both expensive.

1. **Guarding against a label loss that does not happen.** Reading the rename and the key together, the
   obvious conclusion is that a re-kind must strand the label, and the obvious fix is to refuse a
   re-kind on a labelled flow — or to carry the resource row by hand. Either one would break the
   remedy `FreeTheDefaultSlot()` tells callers to use, on the flows most likely to be labelled: 84.9%
   of the conditional flows in the shipped 7.8.0 corpus carry a label. The refusal would be a dead end
   invented to prevent a failure that does not occur: the label survives, and at most an unread row
   under the old name stays.
2. **Concluding a label did not land because the metadata does not show it.** Diffing a clio-authored
   schema's `metadata.json` against a designer-authored one says nothing about labels — the whole
   difference is in `Resources/**`. `SysLocalizableValue` (join `SysSchema` on `SysSchemaId`) is where
   to look, and it is the only place that distinguishes "no label" from "a label under a stale key".

Related: `parameter-displayvalue-lives-in-the-schema-resources.md` records the same
metadata-versus-resources split for `ProcessSchemaParameterValue.DisplayValue`. The re-kind half is
specific to flows, because only a flow's name is re-derived.
