---
description: DeployCreatioArgs exposes deployment-method but still no no-iis argument, and DeploymentStrategyFactory.SelectStrategy auto-selects IIS on Windows without probing it, so default MCP deploys on a Windows agent take the IIS path
applies-to:
  - clio/Command/McpServer/Tools/InstallerCommandTool.cs
  - clio/Common/DeploymentStrategies/DeploymentStrategyFactory.cs
  - clio/Command/CreatioInstallCommand/CreatioInstallerService.cs
date: 2026-08-21
---

**What is true** — `DeploymentStrategyFactory.SelectStrategy` only ever returns
`_dotNetStrategy` on Windows when the caller passes an explicit `deploymentMethod` of `dotnet` or
sets `noIIS`. With the defaults (`"auto"`, `noIIS: false`) a Windows host resolves to
`_iisStrategy`, and the factory never checks whether IIS is actually installed or usable.
`DeployCreatioArgs` carries an optional `deployment` field, so an MCP caller can explicitly request
the dotnet path. It still has no `noIIS` field, and omitted `deployment` remains automatic.

**Why it is this way** — the auto rule is a platform mapping (Windows to IIS, macOS and Linux to
dotnet), not a capability probe, and the MCP argument record was kept to the fields a normal local
deployment needs.

**What breaks if you ignore it** — a test or fixture that expects to deploy through MCP without
touching IIS cannot: the IIS strategy runs unconditionally on a Windows agent, whether or not IIS is
usable there. `CreatioInstallerService` now reserves and validates the requested IIS port before it
creates the configured IIS root, but that safety check does not change the selected deployment
strategy. A Windows MCP caller that needs dotnet must send `deployment: "dotnet"`; omitting it still
selects IIS without a capability probe.
