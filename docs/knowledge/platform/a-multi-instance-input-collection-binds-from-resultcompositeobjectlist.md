---
description: a Read data element exposes two collection outputs and only ResultCompositeObjectList is type-compatible with a multi-instance element's InputRecordCollection - ResultEntityCollection is the obvious name and the wrong one
applies-to:
  - spec/eng-99856-multi-instance/
ticket: ENG-99856
date: 2026-09-22
---

**What is true** — a Read data element in `collection` mode carries two collection-valued outputs, and
they are different data value types:

| Parameter | Data value type UId | |
|---|---|---|
| `ResultEntityCollection` | `51fb23ba-3eb2-11e2-b7d5-b0c76188709b` | not compatible |
| `ResultCompositeObjectList` | `651ec16f-d140-46db-b9e2-825c985a8ac2` | **the one to bind** |
| `InputRecordCollection` on a multi-instance element | `651ec16f-d140-46db-b9e2-825c985a8ac2` | — |

`ResultCompositeObjectList` is the one whose type matches, and it is the one that carries the selected
columns as its item properties — so it is also where the per-item sources come from. The shipped
`ExpireLicenseNotificationProcess` confirms both halves: its multi-instance `InputRecordCollection`
sources from `ResultCompositeObjectList` (`0f431e75-…` on its Read data element), and each of its four
item properties sources either from one of that collection's own items or from a process parameter.

The designer displays this binding as `[#<element caption>.Collection of records:<column caption>#]` — so
"Collection of records" in the UI is `ResultCompositeObjectList`, not `ResultEntityCollection`.

**Why it is this way** — `ResultEntityCollection` is an `EntityCollection`: live `Entity` objects with
the platform's whole column machinery behind them. `ResultCompositeObjectList` is a flat composite-object
list, which is what a multi-instance iterator can hand to a called process one item at a time. The two
exist side by side because different consumers want different things from the same read.

**What breaks if you ignore it** — nothing silent, which is why this is short: the type check refuses the
mapping and names the incompatibility. But `ResultEntityCollection` is the name a caller reaches for
first, the refusal costs a round trip against a stand, and the refusal message names the two parameters
rather than telling you which one to use instead. It cost one on this ticket.
