---
description: A sequence flow's kind lives in FOUR independent fields - the CLR class, FlowType, ManagerItemUId and VisualType - each read by a different consumer, so writing one without the others yields a flow that describes one way and runs another
applies-to:
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
  - clio/Command/ProcessModel/ProcessGraphValidator.cs
  - clio/Command/McpServer/Tools/ProcessDesigner/
ticket: ENG-91853
date: 2026-09-05
---

**What is true** — there is no single "kind" field on a flow. There are four, and four different
consumers each read a different one:

| Field | Read by | Plain | Conditional | Default |
|---|---|---|---|---|
| CLR class | the RUN TIME | `ProcessSchemaSequenceFlow` | `ProcessSchemaConditionalFlow` | `ProcessSchemaSequenceFlow` |
| `FlowType` (`CI4`) | design-time helpers | `Sequence` (absent in meta) | `Conditional` | `Default` |
| `ManagerItemUId` (`BL7`) | the designer client | `0d8351f6…` | `dac675d4…` | `573ed909…` |
| `VisualType` (`CI6`) | connector routing | `AutoPolyline` | `AutoPolyline` | `AutoPolyline` |

A default flow and a plain one share the CLASS and differ only in the middle two. Measured over the
shipped 7.8.0 corpus with **zero** exceptions: 7 599 plain, 1 406 conditional, 756 default.

**Why it is this way** — the class carries behaviour (only `ProcessSchemaConditionalFlow` copies
`ConditionExpression` into flow-schema generation), the enum drives design-time helpers, the palette
item drives the client's image and its allowed-connection menu, and the routing enum is a rendering
hint. Nothing reconciles them; each is written independently.

**What breaks if you ignore it** — each wrong field fails differently, and none of them loudly.
Class right, enum wrong: `ProcessSchemaFlowNode.GetOutgoingsConditionalFlowsInternal` downcasts
unguarded and throws `InvalidCastException` at a human opening a properties page, not at the caller
who wrote it. Enum right, class wrong: the flow serializes and validates as conditional and its
condition is silently dropped at generation time — and `describe` does **not** corroborate it, because
the kind is read from the class first, so the flow reads back as `sequence` while its metadata says
otherwise. Palette item wrong: the designer resolves the flow to the wrong menu entry while `describe`
still reports the kind correctly from the enum, which is a disagreement nobody thinks to look for.

`VisualType` is the exception that is worth knowing so you do not build an argument on it: it cannot
be got wrong, because the platform's writer ignores the property — see
[sequence-flow-visualtype-is-written-as-a-literal](../platform/sequence-flow-visualtype-is-written-as-a-literal.md).
Related: [flow-palette-item-is-set-on-every-shipped-flow](flow-palette-item-is-set-on-every-shipped-flow.md)
for the palette item alone, and [conditional-flow-rekind-must-be-in-place](conditional-flow-rekind-must-be-in-place.md)
for what changing the class costs.
