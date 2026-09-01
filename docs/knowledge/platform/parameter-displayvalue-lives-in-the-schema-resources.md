---
description: ProcessSchemaParameterValue.DisplayValue is a LocalizableString, so it serializes into the process schema's RESOURCES and never appears in metadata.json — and the designer shows a non-empty one verbatim, resolving a lookup record's name itself only when it is empty
applies-to:
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
  - clio/Command/ProcessModel/IProcessDescriber.cs
ticket: ENG-96325
date: 2026-09-01
---

**What is true** — a process parameter's stored value has two halves that live in two different
files. `ProcessSchemaParameterValue.Value` and `.Source` land in `Schemas/<Process>/metadata.json`
(as the obfuscated `L8.GS2` / `L8.GS1` keys). `.DisplayValue` does not: it is backed by
`LocalizableString` (`LocalizableDisplayValue`), so it is written to
`Resources/<Process>.Process/resource.<culture>.xml` as
`BaseElements.<Element>.Parameters.<Param>.DisplayValue`. The `GS4` key that *does* appear in
metadata beside the value is the localizable member's null marker, not the text.

The designer reads that resource and **shows it verbatim when it is non-empty**. It resolves the
referenced record's name itself only when the display value is EMPTY — see
`CrtProcessDesigner/.../ActivityUserTaskPropertiesPage.js`:

```js
let displayValue = parameter.getParameterDisplayValue();
this.setActivityCategory(value, displayValue);
if (!displayValue) { this.loadLookupDisplayValue("ActivityCategory", value, ...) }
```

So for a lookup constant there are exactly two correct states — the record's NAME, or nothing at
all. Writing the record id there is the one state that is wrong, and writing *nothing* is strictly
safer than guessing.

**Why it is this way** — the display value is what a human reads, so the platform made it
localizable, and localizable members are extracted from the schema into per-culture resources at
save time. Nothing in the metadata hints that the field exists.

**What breaks if you ignore it** — you compare a clio-authored schema's `metadata.json` against a
designer-authored one, find them identical in every load-bearing field, and conclude clio already
writes what the designer writes. It does not; the whole difference is in a file you did not open.
That is how `ProcessMappingService.BuildSourceValue` shipped with `DisplayValue = descriptor.Value`
for years: the runtime resolved the right record, every unit test passed, and the designer's "Task
category" field rendered `03df85bf-6b19-4dea-8463-d5d49b80bb28` where a designer-authored process
showed `To do`. When diffing two process schemas, diff `Resources/**` too.
