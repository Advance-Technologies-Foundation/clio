---
description: BaseTool.ResolveCommand eagerly builds a per-environment container, so a tool that must work with an explicit version or no arguments at all cannot derive from BaseTool<T>
applies-to:
  - clio/Command/McpServer/Tools/ExportComponentRegistryTool.cs
  - clio/Command/McpServer/Tools/ComponentInfoTool.cs
  - clio/Command/ExportComponentRegistryCommand.cs
ticket: ENG-95543
date: 2026-08-21
---

**What is true** — `BaseTool<T>.ResolveCommand<TCommand>` resolves the command out of a
per-environment child container, and it does so before the tool method sees the arguments. A tool
whose contract allows a call with no environment at all — `export-component-registry` and
`get-component-info` both accept an explicit `version`, or nothing — therefore cannot derive from
`BaseTool<T>`. Both are built as flat `[McpServerToolType]` classes that resolve
`EnvironmentSettings` themselves, lazily, and only on the branch where the caller actually supplied
`environment-name`/`uri`.

**Why it is this way** — the environment container is the seam that carries connection settings and
credentials; building it is only meaningful once a target environment is known. There is no
"maybe an environment" mode in `ResolveCommand`.

**What breaks if you ignore it** — porting either tool onto `BaseTool<T>` for surface consistency
makes an explicit-`version` call (the CI path with no live stand — the whole reason
`export-component-registry` exists) fail on environment resolution, or bind silently to whatever
default environment happens to be registered on the host and probe it. The version-resolution
branch in `ExportComponentRegistryTool` must go through `IToolCommandResolver.Resolve<EnvironmentSettings>`,
not `ISettingsRepository`, so an authorized credential-passthrough request keeps its header tenant
(ENG-93347).
