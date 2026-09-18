---
description: Whether describe's inSync can show sub-process drift depends on which schema instance the manager hands back, and for an INTERPRETABLE process a freshly built one converges while for a compiled one it carries the compile-time parameter set; prime the instance before changing the callee
applies-to:
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
  - clio/Command/ProcessModel/IProcessDescriber.cs
ticket: ENG-92707
date: 2026-09-18
---

**What is true** — `describe-business-process` returns whatever schema instance the manager hands back
and never re-converges it. Whether `inSync` can therefore show that a called process changed depends on
two things: whether an instance built BEFORE the change is still cached, and — if one has to be built —
whether the process is interpretable.

| State when describe runs | What it returns | `inSync` after a callee change |
|---|---|---|
| an instance cached from before the change | that instance, unconverged | **`false`** — real evidence |
| no instance, process is INTERPRETABLE | one built now, and the build CONVERGES | `true` — says nothing |
| no instance, process is COMPILED | one built from the assembly, carrying the COMPILE-TIME parameter set | **`false`** — real evidence, no priming needed |
| manager cannot produce an instance at all | the design instance, which converges | `true` — says nothing |

**Why it is this way** — `BaseProcessSchemaManager.CreateSchemaInstance` routes to
`FindInstanceFromMetaData` only when `UseInstanceFromMetaData`, which is
`ForceUseInstanceFromMetaData || CanUseFlowEngine` (`ProcessSchemaManagerItem.cs:61-73`). That path
converges: `GetItemFromMetaData` calls `SynchronizeParameters()` (`BaseProcessSchemaManager.cs:961,966`,
and `:1090` for the design path — those three are its only call sites). Otherwise it falls to
`base.CreateSchemaInstance` → `assembly.CreateInstance` (`SchemaManager.cs:2547-2551`), which
synchronizes nothing, so a compiled process's fresh instance is whatever was compiled.

`SchemaManagerItem.Instance` is a lazy double-checked build, so ANY reader creates one; saving the schema
evicts it (`DropInstance` / `ClearRuntimeInstances`, `SchemaManager.cs:2305-2317`). The last row is not
just file-design mode: `InitializeSafeSchema` (`SchemaManager.cs:4023-4039`) also yields null when the
assembly is absent, on `MissingMethodException`, and when `assembly.CreateInstance` returns null — which
is what a compiled process whose type was never published into the workspace assembly does.

**What breaks if you ignore it** — the advice inverts. Told that running or re-reading the caller is what
makes drift visible, an agent does it AFTER changing the callee; on an interpretable process that builds a
fresh converged instance and reports `true`, destroying the evidence it was sent to collect. Told the
opposite — that any read after the change hides it — a caller concludes `describe` is useless, when a
primed instance is exactly what makes it work.

**The recipe, which is correct for every row above and harmful in none:** save the caller, describe it
once, change the callee, describe again. The middle read is what makes the last one meaningful.

**Provenance, because this paragraph has a history.** It shipped wrong in three consecutive releases —
"a COMPILED process reads the runtime instance", then "a runtime instance is produced by RUNNING the
process", then "the fallback is a missing manager item". Every version was consistent with everything the
stand could show; each error was visible only by reading platform source. That is why the mechanism lives
here, with line numbers, instead of in `DescribeContracts`, which reads as settled. The stand control
behind the second version (one caller differing only in having been run) established that a run is one
way to prime the cache, not that it is the mechanism.
