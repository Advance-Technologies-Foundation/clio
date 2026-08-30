---
description: DescribedFilter / DescribedFilterGroup / DescribedFilterCondition / DescribedFlow in IProcessDescriber.cs carry no [JsonExtensionData], so a filter or flow field the ProcessBuilder package emits and clio does not declare is dropped on re-serialize with no error
applies-to:
  - clio/Command/ProcessModel/IProcessDescriber.cs
ticket: ENG-91842
date: 2026-08-19
---

**What is true** — `describe-business-process` deserializes the server payload into the
`Described*` types and re-serializes them for the caller. `DescribeProcessResult`,
`DescribedElement` and `DescribedEmail` each hold a `[JsonExtensionData]` overflow bag, so an
unknown field survives the round trip. The three filter types — `DescribedFilter`,
`DescribedFilterGroup` and `DescribedFilterCondition` — do **not**. Every filter field therefore
needs a property on both sides: the descriptor in the ProcessBuilder package *and* a matching
`[JsonPropertyName]` property here. `Macro`, `MacroArgument` and `DatePart` exist for exactly that
reason.

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

**Widened by ENG-95891.** `DescribedFlow` joined the same shape when it gained `condition`: the package emits a flow's condition text, clio declares one property for it, and anything else the package starts emitting on a flow — a label, a precedence hint — vanishes the same silent way. The next field added to a flow needs a property here, not just a read on the server side.
