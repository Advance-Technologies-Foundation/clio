# Which detached operations can be reconciled, and which can only be uncertain

Discussion #1643. This is analysis plus measurement against a live Creatio stand, not a code change.
It answers the question E3's README left open: after a host has lost its memory of an operation, which
classes can establish the outcome from the server, and which can only ever report uncertainty.

It matters because it decides how strong the quiescence gate has to be. A class whose outcome can be
established afterwards does not need a swap to wait for it; a class whose outcome cannot be established
does, and after a process loss even waiting no longer helps.

## The operation classes

Everything that can outlive its response goes through the over-deadline heartbeat. In clio 8 that is:
`ApplicationTool` (create-app-section), `CompileCreatioTool`, `RestartTool`, `SchemaSyncTool`,
`InstallProcessBuilderTool`, `InstallDashboardsMigratorTool`, `RunProcessTool`, `StartTool`.

## Three tiers, not two

The useful split is not reconcilable versus not. It is whether the server can answer **what its state is**
and, separately, whether that state can be **attributed to your operation**.

### Tier 1 — attributable: the request names the artefact, so its presence is the answer

| class | reconciled by | evidence |
|---|---|---|
| `create-app-section` | the section and entity schema, by the name you asked for | measured: after a backend swap destroyed the work, `list-app-sections` and `find-entity-schema` correctly showed absence, and the control run showed presence |
| package installs (`install-process-builder`, `install-dashboards-migrator`, `push-pkg`) | package name + version | measured: `list-packages` returns `cliogate 2.0.0.48` — a request for a named version is answered exactly |
| `sync-pages` / schema writes | schema content, by name | not separately measured here; the checksum-conflict path already reads schemas back for this purpose |

A fresh process with no memory can answer "did this happen?" because the question contains its own key.
**A swap does not need to wait for these.** It needs to reconcile afterwards and say something truthful.

### Tier 2 — state-reconcilable, not attributable: the server knows its state, not whose operation caused it

`compile-creatio`. Creatio persists the last compilation result and clio can read it
(`last-compilation-log`). Measured on the stand, the entire payload is:

```json
{"errors":[],"buildResult":0,"success":true}
```

**No timestamp. No operation identity. No build id.** clio's own `ICompilationResultReader` says so
outright: *"The payload carries no timestamp. It answers 'what was the last compilation result', not
'what was the result of the build I started', so a caller must establish by other means that the build it
is asking about has actually ended."* `CompileConfigurationCommand` establishes it by observing the
runtime reload that ends a build — an observation a fresh process after a crash never made.

**Correction — my first version of this claimed too much.** I wrote that a durable end-marker converts
this class to recoverable "for free". That is wrong, and circularly so: an end-marker exists only if the
process survived long enough to write it, and the case that matters is the process dying *during* the
compile, where the marker is absent by construction.

What is true, more narrowly:

- The Creatio core serialises compilation per environment, so at most one compile is in flight per
  target. That means "the last compilation result" *would* be attributable to a known build if you could
  establish that the build had ended — which is the part nothing here supplies.
- A **begin**-marker still improves the answer: instead of today's false `NotFound`, recovery can say
  "a compile for this environment was started at T and this host cannot establish its outcome". That is
  truthful uncertainty with provenance. It is not reconciliation.
- An **end**-marker helps only in the narrower case where the process died *after* the compile finished.
  That is a real case and worth having, but it is the ledger answering from its own evidence, not the
  server being consulted.

What would actually convert this class: a timestamp or build id on the result — a product change, not a
clio one — or an indirect liveness probe exploiting the reject-on-concurrent behaviour, which is a
mutating probe and not something I would recommend. Absent either, `compile-creatio` interrupted
mid-flight is uncertain-only, the same as tier 3, and the tier-2 label describes what the server knows
rather than what a recovering host can establish.

Whether Creatio exposes a richer build-state endpoint than the one clio calls is **unknown** — clio has
exactly one (`api/ConfigurationStatus/GetLastCompilationResult`), and I did not go looking for others.

### Tier 3 — not reconcilable: the only observable is identical before and after

`restart`. The sole server-side signal is liveness. Measured: `get-info` on the stand returns
`coreVersion`, `freedomUiSchemaVersion`, timezone fields — **no uptime, no process start time, nothing a
restart would move**. A server that restarted successfully and a server that was never restarted are
indistinguishable through this surface.

clio already treats restart-by-credentials as deliberately unreportable. This measurement says the
limitation is broader: for restart there is no external answer to recover, so after a process loss
`Unknown` is not a temporary embarrassment, it is the permanent truth.

`StartTool` belongs here for the same reason on the local side. `RunProcessTool` is **untested**: Creatio
keeps process instances with identity, so it plausibly sits in tier 1, but I have not measured it and am
not classifying it on that basis.

## Consequence for the quiescence gate

| tier | must a swap wait? | after a process loss |
|---|---|---|
| 1 | no — reconcile afterwards by the artefact's key | recoverable: read the target |
| 2 | yes, in practice — nothing available establishes that a given build ended | recoverable only if the process survived the work; otherwise uncertain, like tier 3 |
| 3 | yes — waiting is the only honest option | permanently `Unknown` |

So a single global quiescence gate is stronger than tier 1 needs, and tier 2 and tier 3 both need it.
The gate's cost is not uniform across classes, which is the useful part; but tier 2 does **not** convert
for free, and my first version of this document said it did.

## What this does not claim

Reconcilability is about establishing an **outcome**, never about continuity. A tier 1 answer tells you
the artefact is absent; it does not resume the work, and it does not make a retry safe — a half-completed
`create-app-section` is absent by this test and not necessarily safe to re-run. @kirillkrylov's
qualification stands unchanged: `Unknown` is truthful uncertainty, not permission to replay.

Measured on `issue-969-stand` (Creatio 10.1.725.0, .NET Framework, MSSQL) with clio 8.1.0.131. One stand,
one product, one platform — a different product line may expose different surfaces. `sync-pages` and
`run-process` are reasoned about rather than measured, and are marked as such above.
