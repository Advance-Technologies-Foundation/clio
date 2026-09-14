---
description: ReadDataUserTask.ResultCount is a declared parameter that resolves and stores in any condition, but HandleResult assigns it only in Function result mode with FunctionType == Count - so on the first-record mode clio builds it stays 0 and every "> 0" branch is dead
applies-to:
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
ticket: ENG-91853
date: 2026-09-06
---

**What is true** — `ResultCount` is a real declared parameter of a Read data element, so
`[#Read.ResultCount#]` resolves, the condition saves, the process compiles and `describe` reads it back
looking exactly right. It is nevertheless never assigned unless the element runs in **Function** mode
with **`FunctionType == Count`**:

```csharp
// ReadDataUserTask.CrtProcessDesigner.cs - HandleResult
if (resultType != ProcessReadDataResultType.Function) {
    ResultEntity = resultEntity;
    ResultEntityCollection = entities;
    return;                                   // ResultCount untouched
}
...
if (FunctionType == (int)AggregationTypeStrict.Count) {
    ResultCount = resultEntity.GetTypedColumnValue<int>(aggregationColumnName);
}
```

CrtProcessBuilder builds only `mode: "first"`, which is `ProcessReadDataResultType.Entity`
(`ReadData.ModeFirst`; collection and function modes are read-back tokens for describing
designer-authored elements, not buildable ones). So on anything clio creates, `ResultCount` is 0 for
the life of the instance.

**Why it is this way** — the outputs are a union: one element class serves three result shapes and each
shape fills only its own field. Nothing narrows the parameter list per mode, because the designer
switches the mode on the same element.

**What breaks if you ignore it** — `[#Read.ResultCount#] > 0` as a "was a record found?" test is
**always false**. The record-found branch never runs, the fallback always does, and every artefact you
would check to notice — the saved condition, the describe output, the compiled schema, the run's
absence of errors — looks correct. The shipped guidance carried exactly that as its only
element-output example until the third review gate.

The honest test on a `first`-mode read is against `ResultEntity`, and that is a RECORD: reaching one of
its columns needs the three-segment `[Element:…].[Parameter:…].[EntityColumn:…]` meta-path, which the
by-name form on the build path cannot express. 242 of the 487 element-output conditions in the shipped
corpus are that shape, so this is the common case and not a corner.
