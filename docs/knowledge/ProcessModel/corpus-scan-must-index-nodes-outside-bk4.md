---
description: a process-corpus scan that resolves a flow's source element from the schema's own BK4 list gets a plausible but wrong answer - entity schemas inherit process elements that live outside BK4, so sources silently fail to resolve, are counted as non-gateways anyway, and are reported under an invented element class
applies-to:
  - spec/eng-91853-gateways-and-flows/implicit-parallel-split-corpus-scan.py
  - spec/eng-91853-gateways-and-flows/flow-caption-corpus-scan.py
  - docs/knowledge/ProcessModel/shipped-processes-break-the-designers-own-connection-rules.md
  - clio/Command/ProcessModel/ProcessGraphValidator.cs
ticket: ENG-91853
date: 2026-09-14
---

**What is true** — in a process schema's `metadata.json`, `BK4` holds the schema's *own* flow
elements. A flow's source is a UId (`CI1`), and that UId does **not** always name something in the
same `BK4`. Entity schemas inherit process elements from their parent, and those elements sit
elsewhere in the JSON tree:

| Schema | Source element the flows point at |
|---|---|
| `CrtCoreBase/BaseEntity` | `BaseEntityStartMessage1` (`ProcessSchemaStartMessageEvent`) |
| `CrtBase/BaseEditPage` | `OpenMessageUserTask223` (`ProcessSchemaUserTask`) |
| `CrtBase/BaseModulePage` | `BaseModuleInit` (`ProcessSchemaStartMessageEvent`) |

So the UId index a scan resolves sources against must be built by walking **every** dict in the tree
that carries a `UId` and a `BL1`, not by walking `BK4`.

**Why it is this way** — `BK4` is the container for elements this schema declares. Inherited process
behaviour is not re-declared per entity; the flows reference it where it already lives. Nothing in the
metadata marks the difference, and a lookup miss is an ordinary dictionary miss.

**What breaks if you ignore it** — the scan answers, and the answer is wrong in **both directions at
once**, which is why nothing looks broken:

- an unresolved source is **still counted**, because "unresolved" is not "gateway" — and one of the 14
  unresolved sources in this measurement *was* a gateway, so it was counted when it should have been
  excluded;
- the element-class histogram grows a category (`<unresolved>`, or whatever placeholder the script
  uses) that is not a platform class at all, and the real classes are under-reported by the same
  amount.

Measured on the 7.8.0 `PackageStore` corpus, scanning for clio's R12 shape (a non-gateway source with
more than one outgoing plain `sequence` flow): the `BK4`-only index reported **75** sources with 14
unresolved; the full-tree index reports **74** with zero. One source moved out of the count because it
resolved to a gateway, and 13 moved between element classes.

A single-digit error is exactly the size that survives review. This number was being used to choose a
**severity** — refuse the shape, or notice it — so it had to be right for the right reason, not merely
close. Print the unresolved count unconditionally; a scan that cannot say it resolved everything has
not measured anything.

Related: [`flow-kind-is-four-fields`](flow-kind-is-four-fields.md) for the other corpus-probe trap —
reading a flow's kind from one field instead of the class-then-enum order the run time uses.
