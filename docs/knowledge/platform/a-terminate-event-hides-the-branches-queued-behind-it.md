---
description: FlowVisitor ends the whole process instance the moment it executes any element whose BpmnElementName is TerminateEventName - and ProcessSchemaEndEvent sets that name too - so a fan-out whose branches reach end events logs only the first one; a missing SysProcessElementLog row is not evidence that its branch was not taken
applies-to:
  - clio/Command/ProcessModel/ProcessGraphValidator.cs
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
ticket: ENG-91853
date: 2026-09-06
---

**What is true** — the process element log is not a record of which branches were *chosen*. It is a
record of which elements got as far as *executing* before the instance ended, and one element kind ends
it for everybody:

```csharp
// Terrasoft.Core/Process/FlowVisitor.cs
_flowService.ExecuteElement(flowElement, this);
if (flowElement.BpmnElementName == BpmnElementVocabulary.TerminateEventName) {
    return false;                      // stops the visitor loop for the whole instance
}
```

`ProcessSchemaEndEvent` sets `BpmnElementName = TerminateEventName` as well as
`ProcessSchemaTerminateEvent` does, so an ordinary **end event** does this — not just the element
called "Terminate".

Nothing upstream selects. `FlowSchema.GetNextFlowElements` is a plain `foreach` over
`FindSequenceFlowsBySourceUId` that adds every target; there is no condition, no arity check and no
kind check in it at all.

**So for an element with several plain outgoing flows whose targets are end events**, every branch is
queued, the first end event visited returns `false`, and every branch still in the queue is discarded
unrun and unlogged. `SysProcessLog` says `Completed`; `SysProcessElementLog` shows the source and one
branch.

**Why it is this way** — a terminate event means "end this process instance", and the visitor
implements that literally by refusing to drain its own queue. That is correct behaviour; the trap is
only that the *observability* of a fan-out is destroyed by it.

**What breaks if you ignore it** — you conclude that a decision was made when none was. This ticket did
exactly that: `UsrRequest_DecideNextStep` (two plain flows off a user task, both to end events) logged
one branch and completed, which was written up as evidence that clio's R12 rule — *"multiple outgoing
sequence flows = implicit parallel split"* — is false for a user task. It is not false. The claim
reached shipped package source before anyone questioned it.

**The positive control that settles it, and the shape it must have.** Give the fan-out
**non-terminating** targets, so nothing can end the instance before the second branch runs:

```
Read a contact    (readData)     Completed      13:22:58.810
Branch A task     (performTask)  Running        13:22:58.821
Branch B task     (performTask)  Running        13:22:59.861
```

Both branches ran. Two design notes that cost a build each: the toolkit's `endEvent` compiles to
`ProcessSchemaTerminateEvent`, so it can never be a safe probe target; and a **start event may not
fan out** — the package refuses *"start event 'X' must have a single outgoing flow"* — so the fan-out
source has to be an activity, and an auto-executing one like `readData` if you want the run to reach
the branches without a person completing a task.

**The general rule this is an instance of.** A missing row in an execution log is an *absence*, and an
absence is only evidence when the probe has been shown capable of producing the row. See
[`docs/knowledge/Tests/reachability-not-corpus-absence-decides-whether-a-guard-stays.md`](../Tests/reachability-not-corpus-absence-decides-whether-a-guard-stays.md)
for the same failure in a different medium.
