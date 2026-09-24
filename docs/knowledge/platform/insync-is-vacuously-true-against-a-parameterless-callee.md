---
description: subProcess.inSync is an All over the CALLEE's parameters, so it is vacuously TRUE when the callee declares none - which makes "false by construction on a multi-instance element" wrong, and three shipped surfaces said it
applies-to:
  - clio/Command/McpServer/Tools/ProcessDesigner/DescribeProcessTool.cs
  - clio/Command/ProcessModel/IProcessDescriber.cs
ticket: ENG-99856
date: 2026-09-22
---

**What is true** — `inSync` asks whether every parameter the CALLEE declares is present on the element,
and the package computes it as `details.Parameters.All(p => onElement.Contains(p.Name))`
(`SubProcessElementIdentity.MirrorsCallee`). `All` over an EMPTY sequence is `true`. So a callee that
declares no parameters yields `inSync: true` on any element, **including a multi-instance one** — whose
root carries the five service parameters and none of the callee's.

The correct statement is therefore: on a multi-instance element `inSync` is false whenever the callee
declares anything, and **vacuously true when it declares nothing**. It never carries information either
way; `multiInstanceOptions.calleeInSync` is the field that answers the question, asked one level down
against the collections' item properties.

The path is reachable rather than theoretical: `EnsureConversionLanded` returns early when there is
nothing routable to carry, so an element mirroring a parameterless callee converts cleanly and then
reports `multiInstance: true` beside `inSync: true`.

**Why it is this way** — `MirrorsCallee` is a containment test, not an equality test, and it is
one-directional by design (it catches a parameter the callee ADDED and misses one it REMOVED). Nothing
special-cases the empty callee, and nothing should: the test is simply not meaningful there.

**What breaks if you ignore it** — three shipped agent-facing surfaces asserted that the test "can never
pass" on a multi-instance element, and one of them had been *strengthened* to "FALSE BY CONSTRUCTION"
while a neighbouring clause was being corrected. The package's own shipped contract
(`Contracts/DescribeContracts.cs`) already recorded the vacuous case, so two surfaces contradicted a
third. An agent that believes "false by construction" reads a `true` it was told is impossible and has
no rule to apply to it — and an agent that believes `true` means the element is in sync acts on a value
that is empty of information.
