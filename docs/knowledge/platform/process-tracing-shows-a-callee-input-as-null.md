---
description: process tracing is not evidence of a multi-instance callee's inputs - it shows them null in every iteration even when the values arrive, and it showed an In parameter named Name holding the caller element's name although the mapped value arrived; read them with a gateway inside the callee
applies-to:
  - clio/Command/ProcessModel/IProcessDescriber.cs
  - clio.mcp.e2e/ProcessTracingToolE2ETests.cs
  - clio.mcp.e2e/SubProcessMultiInstanceToolE2ETests.cs
ticket: ENG-95984
date: 2026-10-14
---

**What is true** — with tracing on, `SysPrcElementTraceLog.ProcessData` of a multi-instance callee's elements
shows the callee's In parameters with `"Value": null` in every iteration, including iterations that demonstrably
received a value. An In parameter literally named `Name` was shown holding the caller's sub-process ELEMENT name
(`Sub1`) instead. A gateway inside the same kind of callee proved both wrong: 3 of 3 iterations received the mapped
value, for a parameter named `Email` and for one named `Name` alike. Measured on the .NET Framework stand on
2026-10-14.

**Why it is this way** — not traced in the platform source. The trace evidently snapshots the callee's
parameters before the per-iteration values are applied; the `Name` value is whatever the snapshot holds at that
point, not what the callee's elements read.

**What breaks if you ignore it** — the trace makes a working multi-instance mapping look broken, and an evidence
review built on it discards a valid measurement. The reliable oracle: in the callee, an exclusive gateway with a conditional flow
(`string.IsNullOrEmpty([#Email#])`, or `[#File#] == null`) to a Formula task captioned "M empty" and a default flow
to one captioned "M set"; then count `SysProcessElementLog` rows by `Caption`, filtered on
`SysProcess.SysSchema.Name` (ESQ). It proves a value arrived, not which one; a further conditional flow comparing
with a known value tells which.
