---
description: Sibling conditional flows are evaluated in schema.FlowElements insertion order and the first true one wins - no index, priority or position field encodes it, and Outgoings is NOT in that chain - so flows[] order must never be re-sorted and a kind change must preserve the flow's index
applies-to:
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
  - clio/Command/McpServer/Tools/ProcessDesigner/
  - spec/ai-business-process-generation/ai-bp-connection-rules.md
ticket: ENG-91853
date: 2026-09-05
---

**What is true** — when two conditional flows leave one element, which branch runs is decided by the
order they occupy in `schema.FlowElements`, and by nothing else. `FlowSchemaGenerator.Generate`
enumerates that collection, groups by source in encounter order, `FillSequenceFlows` adds them
unsorted, `FlowSchema.FindSequenceFlowsBySourceUId` is a plain `Where`, and
`FlowConditionalGateway.Accept` returns on the first condition that evaluates true under
`ConditionEvalStrategy.Exclusive`.

**No field records it.** A flow has no index, priority or position property, so precedence is invisible
in the metadata, invisible in `describe`, and undocumented on Academy. The source node's `Outgoings`
collection is **not** in this chain either — it appears zero times in `FlowSchemaGenerator` — so
reasoning about precedence from `Outgoings` gives an answer that happens to be right until it is not.

**Why it is this way** — the generator was written to preserve authoring order and nothing was ever
added to override it. Since the designer inserts a new flow at the end, a human drawing branches
top-to-bottom gets precedence matching the drawing, and the absence of a field never surfaced.

**What breaks if you ignore it** — every failure here is silent and reproduces as "the wrong branch
ran", with nothing to point at:

- **Sorting `flows[]` on the build path** — by name, by target, for tidiness — reorders evaluation.
  Two overlapping conditions (`Amount > 100`, `Amount > 1000`) then resolve the other way round.
- **A kind change done as remove-and-add** looks equivalent and appends, moving that branch to LAST.
  This is why the re-kind replaces the flow at its captured index; see
  [conditional-flow-rekind-must-be-in-place](conditional-flow-rekind-must-be-in-place.md).
- **Reading precedence back** is impossible from any API, so a reviewer cannot check it. The one
  place it becomes visible is the DIAGRAM: the layout engine assigns branch lanes in flow declaration
  order, top lane first, so top-to-bottom lane order equals evaluation order. That is the only reason
  a human can audit it at all, and it is why the layout must not centre or re-order lanes either.

**ACROSS the two dialects the platform partitions rather than ordering.** Array order is precedence
*within* the formula dialect and says nothing about a source carrying both kinds of sibling.
`FlowConditionalGateway.Accept` walks the source's outgoings once and puts every flow carrying
`ExpressionText` into a SECOND list instead of evaluating it; the flows left - the SELECTION branches -
are evaluated in that first pass, and on an exclusive gateway the first match returns before a single
formula has been read.

That rule was documented in `process-branch-conditions`, deleted during ENG-91853 when the dialect got
its own article, and restored only after a review caught its absence. It was then nearly deleted a
SECOND time, by me, on the grounds recorded above - array order - which is exactly the confusion this
paragraph exists to prevent. The shipped guidance carries the rule; the provenance lives here, because
an agent paying tokens for `get-guidance` should not be billed for our editing history.
