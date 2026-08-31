---
description: a viewConfigDiff legitimately carries several operations for one component name (move + merge), so append-merge identity is (operation, name, targets-properties) — keying on name alone silently drops existing operations
applies-to:
  - clio/Command/PageBodyMerger.cs
  - clio/Command/PageInsertDowngradeDetector.cs
  - clio/Command/JsonDiffApplier.cs
ticket: GH-1132
date: 2026-08-31
---

**What is true** — a `viewConfigDiff` is an ordered operation list in which one component `name` may
appear several times, each with a different `operation`: a `move` that places it and a `merge` that
patches it are both valid and both required. `PageBodyMerger.MergeViewConfigDiffOperations` therefore
identifies an operation by `(operation, name, targets-properties)` — both strings compared `Ordinal`,
the third flag distinguishing a `remove` carrying a `properties` array from an element `remove` — and
never deduplicates current-body entries against each other *on its own*. Only an incoming entry with
a matching identity replaces a current one, and it replaces the FIRST occurrence in place; a later
current entry of that same superseded identity is then dropped, because keeping it would re-apply
stale values after the replacement. An entry the merger cannot identify (a missing, empty, or
non-string `name`, or a non-object element) is preserved at its original index rather than moved.

**Why it is this way** — clio's own clone of the platform differ, `JsonDiffApplier`, groups operations
by name with `StringComparer.Ordinal` (`GetObjectNameOperationsGroup`, applied to the move list), splits
`remove` into two groups on `properties is JArray`, switches on the operation verb **exact-case with no
`default` branch**, and applies the groups in a fixed order — `Merge`, then `Remove`/`Insert`/`Move`,
then `RemoveProperties`, then `Set`. The identity mirrors exactly those distinctions. Case-folding the
verb would let a mis-cased `"Merge"` (which the differ discards) replace and therefore delete a working
`"merge"`.

**A consequence that is easy to get backwards** — preserving an incoming transform beside a current
`insert` for one name does *not* make both take effect, and neither does preserving a current element
`remove` beside an incoming `move`. Preserving them is still right — the alternative deletes the
insert and orphans the component, or lets the incoming `move` delete the `remove` — but the second
operation is **inert** at apply time. Do not read the merger as making both operations take effect.
The mechanism, the full set of affected shapes, and the advisory warning `update-page` emits for them
are recorded in
`docs/knowledge/Command/a-transform-beside-an-insert-for-one-name-is-inert.md`.

**What breaks if you ignore it** — the pre-#1132 merger flattened `current.Concat(incoming)` into a
single `name`-keyed dictionary. A page whose body held a `move` and a `merge` for one component lost
the `move` on **any** append, including an append whose fragment never referenced that component: the
save returned success, emitted no warning, and the panel silently left its intended container at
runtime. Do not "simplify" the identity back to `name`, do not fold the operation's case, and do not
default a missing `operation` to an assumed platform value — each reintroduces the replacement of an
operation the caller never named.
