---
description: on a .NET (Core) Creatio host a successful compile does not load the new configuration assembly; the compiled process code runs only after an application restart, and a run before it looks like a run
applies-to:
  - clio/Command/CompileBusinessProcessCommand.cs
  - clio/Command/RunProcessCommand.cs
  - clio/Command/McpServer/Tools/CompileCreatioTool.cs
  - clio/Command/McpServer/Prompts/FsmAndCompilePrompt.cs
ticket: ENG-105744
date: 2026-11-09
---

**What is true** — a .NET (Core) host loads `Terrasoft.Configuration.dll` once, at startup
(`AppConnection.LoadWorkspaceAssembly`). After a compile, `WorkspaceAssemblyChanged` reaches
`Terrasoft.WebHost/Application.OnWorkspaceAssemblyChanged`, which only notifies the web-farm orchestrator;
the .NET Framework handler (`Global.asax`) unloads the AppDomain instead. Until the application restarts, the
platform's start check (`ProcessSchemaManager.GetIsProcessExistsInAssembly`) asks only whether the process's
wrapper TYPE is in the loaded assembly, so a process is in one of three states: never loaded — refused
("Publish the ... process before starting it"); loaded at startup — runs its PREVIOUS code; loaded and since
given a new script task — fails when the flow reaches it. Measured on 10.2.1024 (.NET 8 and .NET 10, 2026-11-06
and 2026-11-09): a signal start before the restart left its `SysProcessLog` instance Running with the refusal
only in `Error.log` (`ConsumerInvocationActor`, platform bug ENG-105872); after the restart the same process
completed.

**Why it is this way** — the platform's restart is `IApplicationRestarter`, which is internal, so neither
CrtProcessBuilder nor clio can trigger the reload the compile lacks; only `restart-by-environment-name` can, and
it reloads the application for every user. Whether a restart also ends the users' sessions on a .NET host
(sessions live in a distributed cache) was not measured.

**What breaks if you ignore it** — an agent verifies a run right after a clean compile, sees the old result or a
process that "started" and stays Running, and either reports the change as working or compiles again, which
changes nothing. The two-minute "runtime may still be reloading" retry that is right on .NET Framework never
ends on a .NET host.
