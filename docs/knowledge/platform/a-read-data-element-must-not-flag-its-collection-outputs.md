---
description: clio sets IsResult on BOTH of a Read data element's collection outputs where the platform sets it on neither, and the designer client throws on more than one - so ResultCompositeObjectList disappears from every mapping picker and a clio-built Read data element cannot be used as a mapping source by hand
applies-to:
  - spec/eng-99856-multi-instance/
ticket: ENG-99967
date: 2026-09-22
---

**What is true** — a Read data element written by clio in `collection` mode carries `IsResult = true` on
**both** collection outputs (`ResultEntityCollection` and `ResultCompositeObjectList`). The platform
carries it on **neither**. Measured on one stand, 2026-09-22:

| Read data element in collection mode | parameters with `IsResult = true` |
|---|---|
| built in the designer | **0** |
| shipped `ExpireLicenseNotificationProcess` (two elements) | **0** and **0** |
| built by clio | **2** |

The designer client refuses more than one
(`Terrasoft/manager/process-flow-element-schema-manager/parametrized-process-schema-element.js:334`):

```js
getResultParameter: function() {
    const resultParameters = this.parameters.filterByFn(p => p.isResult === true);
    if (resultParameters.getCount() > 1) {
        throw new Terrasoft.InvalidObjectState();
    }
    return resultParameters.first();
}
```

The visible effect is **inverted visibility**. Beside a clio-built Read data element every mapping picker
of another element lists only *"Resulting collection"* (`ResultEntityCollection`) and never *"Collection
of records"* (`ResultCompositeObjectList`) with its columns; beside a designer-built one it lists the
opposite. The *Select parameter* dialog phrases the absence as **"There are no parameters of required
type"**.

**Why it is this way** — clio's binder treats the mode's outputs as "the element's result" and moves the
flag there on every write, on the reasoning that a downstream mapping needs the output to be
advertised. The platform advertises those collections some other way, and flags nothing.

**What breaks if you ignore it** — two things, and the second is why this record exists rather than a
one-line fix note.

First, a multi-instance sub-process element **cannot be built by hand** next to a clio-built Read data
element. The designer has no dedicated control for multi-instance: the conversion is a side effect of
mapping a collection into one of the element's parameters
(`ProcessFlowElementPropertiesPage.js`, `collectionMappingSet` → `_convertElementToMultiInstanceMode`).
With the collection hidden there is nothing to map, and the failure is silent — an empty picker reads as
"this element has no usable outputs", not as a defect.

Second, **removing the flag naively hides the collections from `describe-business-process`**, which
selects an element's outputs by `IsResult` or `Direction = Out`. A Read data element's collections are
neither, so describe has never reported them for a designer-built element; clio's flag was masking that
gap for clio-built elements only. `ResultCompositeObjectList` is exactly the name a caller needs to write
a multi-instance mapping, so the binder fix and the describer fix belong in the same change.

Both are tracked by ENG-99967. Until it lands, a Read data element that must be usable from the designer
has to be created in the designer.
