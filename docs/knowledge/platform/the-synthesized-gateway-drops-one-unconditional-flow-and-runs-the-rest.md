---
description: FlowConditionalGateway treats EVERY non-conditional outgoing flow as the fallback - a plain one and a default one alike - and removes exactly ONE of them before running everything left, so a conditional branch beside two unconditional flows is a decision plus a second branch that always starts
applies-to:
  - clio/Command/ProcessModel/ProcessGraphValidator.cs
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
ticket: ENG-91853
date: 2026-09-06
---

**What is true** — the fallback the platform removes when a condition matches is not the flow marked
`default`. It is whichever non-conditional flow the collection happens to hold first, and only ONE of
them:

```csharp
// Terrasoft.Core/Process/FlowConditionalGateway.cs
private bool GetIsDefSequenceFlow(SequenceFlow f) =>
    f.BpmnElementName != BpmnElementVocabulary.ConditionalSequenceFlowName;   // plain AND default match

private void RemoveDefSequenceFlow(IList<SequenceFlow> list) {
    SequenceFlow def = list.Find(GetIsDefSequenceFlow);      // Find, singular
    if (def != null) { list.Remove(def); }                   // one Remove
}
```

`Accept` puts EVERY unconditional outgoing flow into `ResultSequenceFlows` before evaluating anything,
and `OnVisited` returns every flow still in that list. So for one element with flows
`[sequence, sequence, conditional]`:

- condition true → one unconditional flow is removed, and the **other one runs alongside** the
  conditional branch;
- condition false → nothing is removed at all (the removal is guarded by
  `ResultSequenceFlows.Any(conditional)`), and **both** unconditional flows run.

`describe` reads back `sequence / sequence / conditional`. Nothing in the metadata records that a second
branch is live.

**Why it is this way** — the gateway is synthesized for an element that branches, and BPMN gives an
exclusive gateway exactly one default. The platform implements "the default" as "the first thing that
is not a condition", which is correct while there is only one such flow, and the designer guarantees
that: `connection-utils.ts` turns a second unconditional connection into a conditional one rather than
drawing it plain.

**What breaks if you ignore it** — an authoring API that does not enforce the designer's guarantee
writes a process whose behaviour nobody declared. Measured over the shipped 7.8.0 corpus, 1711 schemas:
**736** sources carry a conditional flow beside an unconditional one — 310 of them ordinary elements,
not gateways — and **ZERO** carry two unconditional ones. The shape is unrepresented because the canvas
forbids it, not because it is rare.

clio reports it as **R18** (error) and `FlowKindRules.EnsureNoStrayBranchBesideACondition` refuses to
build it from CrtProcessBuilder 1.4.0.64. Two unconditional flows with NO conditional sibling stay
legal — that is the implicit parallel split, and both of ITS branches really do run (see
[`a-terminate-event-hides-the-branches-queued-behind-it.md`](a-terminate-event-hides-the-branches-queued-behind-it.md)
for how to observe that without the log lying to you).

**The trap in the guard that missed it.** `EnsureAtMostOneDefault` counts flows whose KIND TOKEN is
`default`. That is a different set from the one the runtime removes from, and the whole defect lives in
the gap between them. A rule about the runtime's fallback must be written in the runtime's vocabulary.
