---
description: JsonDiffApplier applies whole operation GROUPS in a fixed order, never in viewConfigDiff array order, so a merge/move/element-remove beside an insert for one component name never applies - update-page warns about it since GH-1240 but cannot make it apply
applies-to:
  - clio/Command/JsonDiffApplier.cs
  - clio/Command/PageInertOperationDetector.cs
  - clio/Command/PageUpdateOptions.cs
ticket: GH-1240
date: 2026-08-31
---

**What is true** — `JsonDiffApplier.ApplyOperations` splits a `viewConfigDiff` into groups and runs
them in a fixed order — `Merge`, then `Remove`/`Insert`/`Move`, then `RemoveProperties`, then `Set` —
**never in array order**. Sorting the array therefore changes nothing. Three mechanisms then discard
an operation with no diagnostic: group ordering; `FilterMoveOperation`, which opens the position
pipeline by dropping every `move` whose `name` matches any element `remove` in the same body; and
source resolution, where an operation whose target name is absent resolves to nothing and is skipped
while `ApplyOperations` throws away the unsuccessful list each group returns. `set` is the only verb
applied after inserts. Since GH-1240 `PageInertOperationDetector` reports seven provable pairs as
advisory warnings on `update-page` — and on `sync-pages`, which shares `TryUpdatePage` and forwards
`PageUpdateResponse.Warnings` into its per-page `validation.warnings`. The operation is still inert;
the warning only says so.

**Why it is this way** — clio does not own the ordering. `JsonDiffApplier` is a clone of the platform
differ, so the group order is the server's, and reordering the groups would diverge from the thing
that actually renders the page. The signal is advisory rather than blocking because the detector reads
ONE schema body: a parent schema in the replacing chain may insert the same name, which puts the
component in the base and makes the transform apply after all, and an ancestor's `alias` carrying
`excludeOperations` can legitimately neutralise an operation. Two shapes that look like they belong in
the table are absent because the applier disproves them: `insert` + `set` (`Set` removes first and
reuses the removed item's `index`/`propertyName`, so only the insert's `values` are overwritten) and
`merge` + property `remove` (only the intersection of the merge's `values` keys with the named
properties is lost).

**What breaks if you ignore it** — you author `insert` then `merge` for one component, the save
returns success, and the merged values are silently absent at runtime; or you append a `move` to a
body that already element-removes that name and the `move` you just wrote is the one discarded. Fold
the transform's values into the `insert`, or use `set`. Do not sort the array, do not promote the
warning to a rejection, do not fold verb case in the detector (the differ's verb switch has no
`default`, so a mis-cased `"Merge"` is dropped whole and is not a live merge), and do not delete
either operation — the merger preserves both on purpose, see
`docs/knowledge/Command/viewconfigdiff-carries-multiple-operations-per-component-name.md`.
