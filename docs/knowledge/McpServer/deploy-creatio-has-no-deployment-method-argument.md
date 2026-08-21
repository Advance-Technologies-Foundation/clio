---
description: DeployCreatioArgs exposes no deployment-method or no-iis argument, and DeploymentStrategyFactory.SelectStrategy auto-selects IIS on Windows without probing it, so every MCP deploy on a Windows agent takes the IIS path
applies-to:
  - clio/Command/McpServer/Tools/InstallerCommandTool.cs
  - clio/Common/DeploymentStrategies/DeploymentStrategyFactory.cs
  - clio/Command/CreatioInstallCommand/CreatioInstallerService.cs
date: 2026-08-19
---

**What is true** — `DeploymentStrategyFactory.SelectStrategy` only ever returns
`_dotNetStrategy` on Windows when the caller passes an explicit `deploymentMethod` of `dotnet` or
sets `noIIS`. With the defaults (`"auto"`, `noIIS: false`) a Windows host resolves to
`_iisStrategy`, and the factory never checks whether IIS is actually installed or usable.
`DeployCreatioArgs` (`siteName`, `zipFile`, `sitePort`, `dbServerName`, `redisServerName`,
`useHttps`) carries neither of the two escapes, so an MCP caller cannot request the dotnet path.

**Why it is this way** — the auto rule is a platform mapping (Windows to IIS, macOS and Linux to
dotnet), not a capability probe, and the MCP argument record was kept to the fields a normal local
deployment needs.

**What breaks if you ignore it** — a test or fixture that expects to deploy through MCP without
touching IIS cannot: the IIS strategy runs unconditionally on a Windows agent, whether or not IIS is
usable there. `CreatioInstallerService` then creates the configured IIS root
(`settingsRepository.GetIISClioRootPath()`) before it reaches any deployment step, so a clean agent
fails very early and the run reads as a deployment that produced no progress events rather than as a
strategy that was never applicable. There is no argument you can add to the tool call to avoid it -
the escape is an isolated `CLIO_HOME` with an IIS root override, or a new argument on
`DeployCreatioArgs`.
