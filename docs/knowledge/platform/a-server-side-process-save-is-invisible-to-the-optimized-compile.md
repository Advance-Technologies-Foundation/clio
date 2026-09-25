---
description: on Creatio 10.x a process saved server-side (create/modify-business-process) is skipped by Build AND by RebuildPackage - only designer saves mark a package for the optimized compile - so the script task keeps running its PREVIOUS body; compile-creatio process-name compiles it
applies-to:
  - clio/Command/CompileBusinessProcessCommand.cs
  - clio/Command/McpServer/Tools/CompileCreatioTool.cs
  - clio/Command/McpServer/Tools/ProcessDesigner/CreateBusinessProcessTool.cs
  - clio/Command/McpServer/Tools/ProcessDesigner/ModifyBusinessProcessTool.cs
  - clio/Command/McpServer/Tools/ProcessDesigner/ModifyProcessAsNewVersionTool.cs
  - spec/eng-92711-script-task/
ticket: ENG-92711
date: 2026-09-25
---

**What is true** — on Creatio 10.0 and later (`releases/10.0.0.*` and trunk; absent from every
`releases/8.3.*`), the compile that WorkspaceExplorerService runs for `Build` (the designer's Publish,
`clio compile-configuration`) and for `RebuildPackage` (`clio compile-package`, `compile-creatio
package-name=X`) is OPTIMIZED: it compiles only packages whose `PackageCompilationState.NeedCompile` is set
(`PackageCompilationOptimizationService.CreatePlan` → `SelectPackagesMarkedForCompilation`). A schema save
marks its package only through `IPackageCompilationOptimizationService.TrackSchemaSaved`, and only the
product's designers call it after `SaveSchema` (`BaseProcessSchemaDesigner.SaveSchema`,
`SchemaDesigner`). `SchemaManager.SaveSchema` itself does not, so a process saved by CrtProcessBuilder
leaves its package unmarked. The service is resolved from the internal `CoreApiContainer`; a configuration
package cannot reach it.

Measured on the local 10.1.37 .NET Framework stand `Creatio` on 2026-09-25, with an edited process saved
into `Custom` (`Build.log`: `compiled: N [...]`):

| Route | Compiled | Time | Result |
|---|---|---|---|
| `Build` (`compile-configuration`) | 0 packages | 57 ms | old body |
| `RebuildPackage(Custom)` (`compile-package`) | 0 packages, then `BuildAll` static content | 24 s + 17 min | old body |
| `Rebuild` (`compile-configuration --all`) | 321 packages, then `BuildAll` | 5.6 + 14.5 min | new body |
| `IWorkspaceBuilder.Build(["Custom"])` (CrtProcessBuilder `CompileProcess`) | `Custom` | 3 min 21 s | new body |

The last row is the package installer's own call (`SystemPackageOperations` → `_workspaceBuilder.Build(names)`):
the non-optimized path, which compiles the named package without consulting the plan. It is public on 8.3.4
and 10.x alike (`WorkspaceBuilderUtility.CreateWorkspaceBuilder`). On 8.3.x every build downloads all sources
from `SysSchemaSource` first (`DBWorkspaceBuilder.InitializeContent`), so the gap does not exist there - that
is a source reading, not measured on an 8.3 stand.

A package is marked by other events too: deleting a schema from it (`PackageSchemaDeleted`) and a designer's
entity save both did on this stand. That is why the first `Build` after a create DID pick a new process up
(an E2E cleanup had deleted processes from `Custom` that morning) while the `Build` after an edit did not.

**Why it is this way** — the save regenerates the source (`SysSchemaSource`) and never touches the
assembly; the runtime binds a script task to a method of the compiled `<Process>MethodsWrapper` by name, and
the only check (`ProcessSchema.CheckCompiledMethodsInAssembly`) is whether that TYPE exists. The optimized
compile is 10.x's answer to 20-minute full builds, and its bookkeeping is fed by the designers.

**What breaks if you ignore it** — an agent that edits a script body and runs `compile-creatio` with
`package-name` (or the CLI `compile-configuration` without `--all`) sees the OLD behaviour and "fixes" code
that was never the code running; the package-scoped one also spends 17 minutes on static content first. A
full `compile-creatio` does pick it up, in about 20 minutes. The fast route, measured on 10.1 and read from
source on 8.3, is
`compile-creatio process-name=<process>` (CrtProcessBuilder 1.6.6.33+), which calls the package's
`CompileProcess`. Do not "optimize" that endpoint back onto WorkspaceExplorerService: it would inherit the
plan and compile nothing. A never-compiled process refuses to start (`Publish the "<name>" process before
starting it`) rather than running stale code.
