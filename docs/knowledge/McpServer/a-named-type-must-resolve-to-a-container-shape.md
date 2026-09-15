---
description: the converter's shape resolver recognised only the literal type strings array/object/map, so a registry that names a nested element slot by its CLASS (crt.List.itemLayout as ViewElementConfig) read as indeterminate and the row was walked out as a child-element array - measured at 71 inputs across 46 components in the generated mobile catalog
applies-to:
  - clio/Command/McpServer/Tools/MobilePageConverter/WebToMobileAnalysisService.cs
ticket: ENG-91859
date: 2026-09-07
---

**What is true** — `ShapeFromTypeAndDefault` decides whether an input holds a single object or a
collection, and it must resolve a NAMED type through the registry envelope's
`references.typeDefinitions`, not just the literal strings `array` / `object` / `map`. Two callers
depend on the answer and both degrade silently without it:

- `IsChildElementArray` short-circuits **only** on `JsonValueKind.Object`. Indeterminate means a web
  `"itemLayout": [ { "type": "crt.ListItem", … } ]` is treated as a child-element collection and
  walked out into its own element-map entries, so the diff ships an array into a slot that holds one
  config — and `InitializeContainerChildSlots` declares whatever slot a child names rather than
  validating it, so nothing downstream objects.
- `CoerceToDeclaredShape` leaves the web array wrapper in place.

`ViewElementConfig` is resolved without a lookup: it is the producer's sentinel for "a nested view
element", never registered as a schema because every component config is one.

**Why it is this way** — the hand-maintained mobile catalog described that slot as
`{"type": "unknown", "default": { … }}`, and the resolver's `default`-kind fallback answered
`Object` from the default. ENG-91859 replaced the producer with one that reads the Flutter runtime,
where the field's type IS a class — so the new payload says `{"type": "ViewElementConfig"}` with no
`default`. That is strictly more precise and strictly less legible to a resolver that only knows
literals. Measured on the generated catalog: **71 inputs across 46 components** declare a named type
with no `default`.

**What breaks if you ignore it** — the converted list renders with no row, which is ENG-95046
reproduced from the opposite direction, and the doc comment on `IsChildElementArray` promises the
exact opposite ("a property the mobile registry declares as a single object … is excluded even when
its elements are `crt.*`-typed"). The promise holds only while "declares as a single object" means
the literal string. Nothing errors, nothing warns; the page saves, validates and opens.

Two consequences worth carrying: a new producer-side type name is a consumer-side contract change,
so a named type that means "one object" must either carry `fields` or be a sentinel the resolver
knows; and enums or scalar aliases must stay indeterminate, because answering a container shape for
them would start coercing scalars.
