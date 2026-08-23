---
description: an MCP tool constructor dependency declared as `IFoo? foo = null` is absent in every existing test construction, so it is only safe when the null path is deliberately fail-soft - never for a requirement or version gate
applies-to:
  - clio/Command/McpServer/Tools/PageSyncTool.cs
  - clio/Command/McpServer/Tools/PageUpdateTool.cs
  - clio/Command/McpServer/Tools/BaseTool.cs
date: 2026-08-19
---

**What is true** — adding a collaborator to an MCP tool as an optional parameter with a `null` default
lets the ~15 existing target-typed test constructions keep compiling while DI still supplies the real
service in production. `PageSyncTool` and `PageUpdateTool` use that pattern for
`IPlatformVersionResolverFactory?` and `ISettingsRepository?`. It is acceptable **only** because
`ResolvePlatformVersionAsync` returns `null` when `resolverFactory is null` and chart validation then
falls back to the latest component set — the degraded behaviour is the intended one.

**Why it is this way** — the tools are constructed positionally in tests, so a required parameter is a
compile break across the whole fixture set. The optional default buys that back, at the cost of making
the dependency untested: no fixture distinguishes "injected" from "missing".

**What breaks if you ignore it** — the same pattern applied to an enforcement dependency silently
disables it, and every test still passes. A `[RequiresPackage]` or version checker taken as an optional
ctor parameter is never forwarded by a derived tool, so the gate simply never fires. That is why
`BaseTool.EnforcePackageRequirements` and `EnforceCreatioVersionRequirements` resolve their checkers
through `ResolveFromCallContainer<T>` at call time instead: it also binds them to the per-call
environment rather than to the MCP bootstrap container. Use the optional-null pattern for fail-soft
enrichment only.
