---
description: a viewConfigDiff legitimately carries several operations for one component name (move + merge), so append-merge identity is (operation, name) — keying on name alone silently drops existing operations
applies-to:
  - clio/Command/PageBodyMerger.cs
  - clio/Command/PageInsertDowngradeDetector.cs
  - clio/Command/JsonDiffApplier.cs
ticket: GH-1132
date: 2026-08-28
---

**What is true** — a `viewConfigDiff` is an ordered operation list in which one component `name` may
appear several times, each with a different `operation`: a `move` that places it and a `merge` that
patches it are both valid and both required. `PageBodyMerger.MergeViewConfigDiffOperations` therefore
identifies an operation by `(operation, name)` — `operation` compared case-insensitively, `name`
compared `Ordinal` — and never deduplicates current-body entries against each other. Only an incoming
entry with a matching identity replaces a current one, in place; an entry the merger cannot identify
(no `name`, or a non-object element) is preserved at its original index rather than moved.

**Why it is this way** — clio's own clone of the platform differ, `JsonDiffApplier`, groups operations
by name into per-name *lists* (`GetObjectNameOperationsGroup`, `StringComparer.Ordinal`) and applies
removes, moves, inserts and merges as separate ordered passes. Multiple operations per name is the
shape the platform expects, and the position of an operation decides when it applies relative to its
neighbours — so neither collapsing nor reordering is safe.

**What breaks if you ignore it** — the pre-#1132 merger flattened `current.Concat(incoming)` into a
single `name`-keyed dictionary. A page whose body held a `move` and a `merge` for one component lost
the `move` on **any** append, including an append whose fragment never referenced that component: the
save returned success, emitted no warning, and the panel silently left its intended container at
runtime. The same key also let an incoming `merge` destroy a current `insert`, orphaning the
component — the failure `PageInsertDowngradeDetector` was built to warn about after the fact. Do not
"simplify" the identity back to `name`, and do not default a missing `operation` to an assumed
platform value: that reintroduces the replacement of an operation the caller never named.
