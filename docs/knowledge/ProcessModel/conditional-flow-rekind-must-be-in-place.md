---
description: A flow's kind is the CLR type, so turning a plain sequence flow into a conditional one means replacing the object — and it must be replaced AT THE SAME INDEX, because sibling branch precedence is nothing but flow-collection order; removing a flow also does not unregister it from its endpoint nodes' Outgoings, so a same-UId replacement throws ItemAlreadyExistException
applies-to:
  - clio/Command/McpServer/Tools/ProcessDesigner/ModifyBusinessProcessTool.cs
  - clio/Command/McpServer/Prompts/ProcessDesigner/ModifyBusinessProcessPrompt.cs
ticket: ENG-95891
date: 2026-08-29
---

**What is true** — three facts that only bite together, when `setFlowCondition` turns an existing plain
flow into a conditional one.

1. **The kind is the CLR type, not the enum.** `ProcessSchemaConditionalFlow` overrides
   `CreateSequenceFlowElement` to copy `ConditionExpression`; the base `ProcessSchemaSequenceFlow` never
   does. Setting `FlowType = Conditional` on the base class gives a flow that *describes* as conditional
   and *serializes* as conditional (`CI4 = 2`, a populated `CI3`) but loses its condition during
   flow-schema generation — and makes `ProcessSchemaFlowNode.GetOutgoingsConditionalFlowsInternal` guard
   on the enum and then cast to the type, throwing `InvalidCastException` at a human opening a properties
   page. So a re-kind is a genuine object replacement.

2. **Position is behaviour.** A diverging gateway evaluates its outgoing conditional flows in the
   insertion order of the schema's flow-element collection and takes the FIRST that evaluates true. No
   index, priority or position property exists anywhere in the metadata. Remove-and-add moves the flow to
   last and silently changes which branch runs.

3. **Removing a flow does not detach it.** `schema.FlowElements.Remove` leaves the flow registered in its
   source node's `Outgoings` (the `SourceRefUId` setter put it there), and that collection is keyed — so
   inserting a replacement that carries the same UId throws `ItemAlreadyExistException` from inside
   `MetaItemCollection.InsertItem`. Assign `Guid.Empty` to `SourceRefUId`/`TargetRefUId` first; the setter
   treats that as detach.

**Why it is this way** — the flow classes were designed for a designer that creates a flow already knowing
its kind, so nothing in the platform re-kinds one and none of the three facts had to be reconciled before.
Ordering-as-precedence is a consequence of `FlowSchema.FindSequenceFlowsBySourceUId` being a plain `Where`
over an ordered collection.

**What breaks if you ignore it** — each failure is silent in a different way. Skip (1) and the condition
is dropped at generation time and the process runs the branch unconditionally, with the cast exception
surfacing later to whoever opens the page. Skip (2) and two overlapping conditions (`Amount > 100`,
`Amount > 1000`) resolve differently after an edit that changed nothing a human can see in the metadata.
Skip (3) and the operation throws a platform exception that names nothing about flows.

Both the write path and its regression tests live in the ProcessBuilder repository
(`Graph/ProcessGraphBuilder.SetFlowCondition`, `ProcessConditionalFlowTests`). Recorded here because the
clio-side tool and prompt text describe the in-place guarantee to agents, and a reader who does not know
why it is in place will eventually "simplify" it into remove-and-add.
