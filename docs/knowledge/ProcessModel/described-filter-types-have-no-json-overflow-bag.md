---
description: this record OWNS which Described* types in IProcessDescriber.cs carry a [JsonExtensionData] overflow bag and which drop an undeclared field in silence - the filter types, DescribedConnection, DescribedSignal and DescribedParameter have none, the membership MOVES (DescribedFlow left the bagless set) and this record has been wrong about that twice, so recount it against the file rather than trusting a list
applies-to:
  - clio/Command/ProcessModel/IProcessDescriber.cs
ticket: ENG-91842
date: 2026-08-19
---

**What is true** — `describe-business-process` deserializes the server payload into the
`Described*` types and re-serializes them for the caller. The types that model an ELEMENT and its
per-kind configuration blocks each hold a `[JsonExtensionData]` overflow bag, so an unknown field
survives the round trip. **Counted against the file, not remembered** — this membership has now gone
stale TWICE, and both times in a record whose whole point is that the list goes stale. Counted at the
merge of ENG-91853 and ENG-94374: 14 types carry a bag, 8 do not.

- **bag, an undeclared field survives:** `DescribeProcessResult` (the graph root),
  `DescribedElement`, `DescribedFlow`, `DescribedEmail`, `DescribedPerformer`, `DescribedApproval`,
  `DescribedOpenEditPage` (+ `ResultsByColumn`, `LogActivity`, `ActivityInterval`),
  `DescribedPreconfiguredPage` (+ `Performer`, `DataSource`, `Button`)
- **no bag, still dropped in silence:** `DescribedFilter`, `DescribedFilterGroup`,
  `DescribedFilterCondition`, `DescribedFilterElementRef`, `DescribedConnection`,
  `DescribedSignal`, `DescribedParameter`
- **no bag, and NOT a gap:** `DescribedProcessVersion`. clio builds every family entry itself from
  the process library, so there is no server field to drop. `ServerProcessDescriber.ApplyVersionFacts`
  records that this inverts the day the server starts reporting the family, and that adding the bag
  belongs to that change.

**`DescribedFlow` carries a bag**, and it is worth saying loudly because two successive versions of
this record got it wrong in opposite directions. It LEFT the bagless set with the
`branchesOnActivityResult` nullability fix, and this record's body went on listing it as bagless
afterwards while its own description line said otherwise. The version-readback work then rewrote the
paragraph and put it back among the bagless types. Neither edit was careless — the list simply cannot
be maintained from memory, which is the argument for recounting it against
`IProcessDescriber.cs` every time this paragraph is touched. A configuration block added later is
expected to carry one; a filter type is not.

Every filter field therefore needs a property on both sides: the descriptor in the ProcessBuilder
package *and* a matching `[JsonPropertyName]` property here. `Macro`, `MacroArgument` and `DatePart`
exist for exactly that reason. And a bag is no licence to skip the property where the value is READ
by name — the parameter-delete and element-retarget guards both read `condition` that way, and a bag preserves an undeclared field without making it addressable. See `describe-process-output-is-capped-by-the-described-dtos.md`, which owns that half. The filter types listed above have NEITHER a property nor a bag, which is why this record exists.

**Why it is this way** — the filter DTOs were hand-mirrored from the package's
`FilterConditionDescriptor` when the vocabulary was small, and `System.Text.Json` discards members
it cannot bind. The remark on `DescribedConnection` records the same gap as an accepted one, but it
is attached to the connections surface; nothing near the filter types says it, and a reader working
on filters will not find it.

**What breaks if you ignore it** — the package writes the field, the platform stores it, the
package's own unit tests pass, and `describe-business-process` answers without it. Nothing logs a
dropped member, so the condition simply reads back incomplete — which looks like the encoder failing
to persist it. This already happened live to macro read-back with green unit tests on both sides; the
same property was added pre-emptively for `datePart`. A DTO change also needs clio rebuilt and the
MCP server restarted, or a stale process keeps serving the old shape.
