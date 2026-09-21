---
description: A sequence flow's VisualType (CI6) is written to metadata as a hardcoded AutoPolyline, so setting the property changes nothing that persists
applies-to:
  - clio/Command/ProcessModel/
ticket: ENG-91853
date: 2026-09-05
---

**What is true** — `ProcessSchemaSequenceFlow.WriteMetaData` passes the *literal* enum value to the
writer instead of the property:

```csharp
writer.WriteValue(VisualTypePropertyName, ProcessSchemaSequenceFlowVisualType.AutoPolyline,
    ProcessSchemaSequenceFlowVisualType.Curve);
```

(`Terrasoft.Core/Process/ProcessSchemaSequenceFlow.cs`, in `WriteMetaData`.) Every other line in that
method passes its own property; this one does not. The third argument is the default-suppression value,
and since the written value is the constant `AutoPolyline` it never equals `Curve`, so the key is never
suppressed either.

So `CI6` is **always `1` (AutoPolyline)** in a saved flow's metadata, whatever the in-memory
`VisualType` holds — including on flows the designer itself writes.

**Why it is this way** — almost certainly a slip rather than a decision: the enum was typed where the
property belonged. It is recorded here because the *effect* is indistinguishable from a convention, and
the convention reading is the one people arrive at.

**What breaks if you ignore it** — two opposite mistakes, both silent.

1. Concluding from a corpus scan that "all 9 762 shipped flows are AutoPolyline, therefore the designer
   always chooses AutoPolyline". It does not choose; the writer cannot emit anything else. Any argument
   built on that count as *evidence of designer behaviour* is unfounded — this is exactly how the count
   was first used in this repository.
2. Assuming that setting `VisualType` on a flow you build is what makes the metadata say AutoPolyline,
   and therefore that dropping the assignment would regress the serialization. It would not: the
   metadata is identical either way. The assignment is still worth making — the value is then right in
   memory before the first save, and it stays correct if the platform ever passes the property — but a
   test asserting the *persisted* `CI6` cannot fail on its removal, so do not write one and believe it
   guards the assignment.

Related: the same flow's *kind* is carried by four independent fields, of which this is one. The other
three (CLR class, `FlowType`, `ManagerItemUId`) do persist and do have to be written together.
