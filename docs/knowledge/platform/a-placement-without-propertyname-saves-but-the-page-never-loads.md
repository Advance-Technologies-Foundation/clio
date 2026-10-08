---
description: a viewConfigDiff insert/move with a parentName but no propertyName is saved by SaveSchema without complaint, but the client differ has no default slot and throws "Item X is not a container for other items", so the page never loads; get-page skips it to stay readable
applies-to:
  - clio/Command/PagePlacementSlotValidation.cs
  - clio/Command/JsonDiffApplier.cs
  - clio/Command/PageGetOptions.cs
  - clio/Command/PageUpdateOptions.cs
ticket: ENG-102501
date: 2026-10-07
---

**What is true** — `ClientUnitSchemaDesignerService/SaveSchema` stores a `viewConfigDiff` without
checking it. The client differ (`creatio-ui` `json-applier.service.ts`) applies a `move` as remove +
`insert`, and `_insert` places the element into `parent[config.propertyName]`. There is no default
slot: with a `parentName` and no `propertyName` it reads `parent[undefined]` and throws
`Item "<parent>" is not a container for other items`. Reproduced on Creatio 10.2.414 (.NET
Framework): a mobile page saved with `{"operation":"move","name":"AreaProfileContainer","parentName":"FeedTabContainer","index":0}`
left the Mobile Page Designer canvas spinning, with exactly that error from `_insert` in the browser
console. Without a `parentName` the element goes to the root list and `propertyName` is not read.
All 424 `move` operations in the platform's own package-store schemas carry `propertyName`.

**Why it is this way** — the server treats the diff as opaque data; only the client applies it, so
the failure appears at render time, not at save time. clio's clone (`JsonDiffApplier`) used to reach
`JToken.this[object key]` with a null key there and threw `ArgumentNullException (key)`, which escaped
every `JsonDiffApplierException` handler. That is why `get-page` answered
`Value cannot be null. (Parameter 'key')` in issue #1752.

**What breaks if you ignore it** — the save reports success and the page is dead in the designer and
at runtime. The rule is therefore mandatory on every save path (`PageUpdateCommand.TryValidatePlacementSlots`,
also behind `validate=false`) and in `validate-page`. Do not "fix" it by defaulting the slot to
`items`: the platform does not, so a clio that defaulted would resolve a bundle the platform refuses
to render. `get-page` skips such an operation only when the strict build fails with exactly that
rejection and reports it in `warnings`; its `bundle.json` for that page is therefore NOT what the
platform renders (the platform renders nothing).
