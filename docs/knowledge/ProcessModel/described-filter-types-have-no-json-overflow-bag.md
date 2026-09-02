---
description: DescribedFilter / DescribedFilterGroup / DescribedFilterCondition in IProcessDescriber.cs carry no [JsonExtensionData], so a filter field the ProcessBuilder package emits and clio does not declare is dropped on re-serialize with no error
applies-to:
  - clio/Command/ProcessModel/IProcessDescriber.cs
ticket: ENG-91842
date: 2026-08-19
---

**What is true** — `describe-business-process` deserializes the server payload into the
`Described*` types and re-serializes them for the caller. The types that model an ELEMENT and its
per-kind configuration blocks each hold a `[JsonExtensionData]` overflow bag, so an unknown field
survives the round trip — today `DescribeProcessResult`, `DescribedElement`, `DescribedEmail`,
`DescribedPerformer` and `DescribedApproval`, and a block added later is expected to carry one too.
The three filter types — `DescribedFilter`, `DescribedFilterGroup` and `DescribedFilterCondition` —
do **not**, and neither do `DescribedConnection`, `DescribedSignal`, `DescribedFlow` or
`DescribedParameter`. Every filter field therefore needs a property on both sides: the descriptor
in the ProcessBuilder package *and* a matching `[JsonPropertyName]` property here. `Macro`,
`MacroArgument` and `DatePart` exist for exactly that reason.

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
