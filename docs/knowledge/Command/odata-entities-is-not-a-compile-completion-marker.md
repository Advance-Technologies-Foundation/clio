---
description: Terrasoft.Configuration.ODataEntities.csproj is not written by a configuration compile, so it cannot gate compile-configuration completion - only RunODataBuild produces that row
applies-to:
  - clio/Common/CompilationSettleTracker.cs
  - clio/Command/WatchCompilationCommand.cs
  - clio/Command/CompileConfigurationCommand.cs
ticket: 1422
date: 2026-09-09
---

**What is true** — `Terrasoft.Configuration.ODataEntities.csproj` rows in `CompilationHistory` are
NOT produced by `compile-configuration`. Measured on a live stand (core 10.0.0.934): two complete
`Rebuild` runs produced no such row in the 400 history rows that followed them, while rows from
those very builds are present. The rows that do appear come from a separate trigger —
`WorkspaceExplorerService.RunODataBuild`, which `EntitySchemaPublisher` queues and which is
`WorkspaceBuilder.BuildOData`'s only caller (see
`odata-rebuild-scoped-to-contract-change.md`). During the measurement session they arrived on an
independent 3–5 minute cadence from another client publishing schemas against the same stand.

`CompilationSettleTracker.FinalMarkerProjectName` therefore holds for the trigger paths
`watch-compilation` was written for, and not for a build clio itself requests. That is why
`CompileConfigurationCommand` does not use the settle tracker at all: it decides completion from the
runtime reload that ends a build, and falls back to a quiet window of its own only when no reload is
ever observed.

**Why it is this way** — the OData entities assembly is rebuilt only when the published OData
contract changes, which a recompilation of existing sources does not do. It is a separate 90–120
second background build with its own request, not a stage of the configuration build.

**What breaks if you ignore it** — a completion rule gated on that marker never fires for
`compile-configuration`: the caller waits out its entire timeout on every successful compile. The
inverse mistake is just as easy and was made first during the investigation of issue #1422 — the
marker rows were attributed to the compilations that happened to precede them, which produced a
confident but wrong "marker latency 6.5 to 9 minutes" measurement. Before treating any history row
as belonging to a build clio started, check that a build with no other activity on the stand
produces it.
