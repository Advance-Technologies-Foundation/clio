---
description: Whether describe's inSync can show sub-process drift depends on which schema instance the manager hands back on BOTH sides; an interpretable process's freshly built instance converges, a compiled one's carries the compile-time parameter set, so prime the caller's instance before changing the callee
applies-to:
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
  - clio/Command/ProcessModel/IProcessDescriber.cs
ticket: ENG-92707
date: 2026-09-18
---

**What is true** — `describe` returns whatever instance the manager hands back and never re-converges it,
so what `inSync` can show depends on which instance that is. `inSync` compares two sides, and the table is
about the CALLER's; the callee is read the same lazy way, so a compiled callee that was not recompiled
reports its own compile-time parameters and both sides agree on stale.

| Caller's instance when describe runs | `inSync` after a callee change |
|---|---|
| cached from BEFORE the change | **`false`** — real evidence |
| cached from AFTER it | as if freshly built — see the next two rows |
| built now, process INTERPRETABLE | `true` — the build converges |
| built now, process COMPILED and its type published in the workspace assembly | **`false`** — the instance carries the compile-time set, no priming needed, provided the callee's read also reflects the change |
| manager cannot produce one — no item, no assembly, `MissingMethodException`, or an unpublished compiled type | design instance, which converges → `true` |

**Why it is this way** — `BaseProcessSchemaManager.CreateSchemaInstance` takes the converging
`FindInstanceFromMetaData` route only when `UseInstanceFromMetaData`
(`ForceUseInstanceFromMetaData || CanUseFlowEngine`, `ProcessSchemaManagerItem.cs:61-73`); otherwise
`base.CreateSchemaInstance` → `assembly.CreateInstance` (`SchemaManager.cs:2547-2551`), which
synchronizes nothing. Within `BaseProcessSchemaManager` the only `SynchronizeParameters()` calls are
`:961`, `:966` (metadata) and `:1090` (design) — `EmbeddedProcessSchema.cs:190` is a fourth elsewhere and
changes nothing here. `SchemaManagerItem.Instance` is a lazy build ANY reader triggers; saving evicts
(`SchemaManager.cs:2311,2315,2317`). The last row returns null rather than throwing because
`InitializeSafeInstance` writes through the `Instance` setter, which marks the item initialized even for
null (`SchemaManager.cs:4023-4039`).

A compiled caller does NOT converge on build even though `ProcessSchemaSubProcess.SchemaUId`'s setter
calls `SynchronizeParameters`: `ProcessSchema`'s constructor runs `InitializeBaseElements()` before the
manager assigns `UId`, so `GetCanSynchronizeParameters()` is false.

**What breaks if you ignore it** — the advice inverts in both directions. "Run the caller to expose
drift" builds a fresh converged instance on an interpretable process and destroys the evidence; "any read
after the change hides it" makes `describe` look useless when a primed instance is exactly what works.

**The recipe, correct for every row and harmful in none:** save the caller, describe it once, change the
callee, describe again.

**Provenance.** This shipped wrong three releases running — "a COMPILED process reads the runtime
instance", then "produced by RUNNING the process", then "the fallback is a missing manager item". Each
was consistent with everything a stand could show; all three were only falsifiable in platform source.
That is why it lives here with line numbers rather than in `DescribeContracts`, which reads as settled.
