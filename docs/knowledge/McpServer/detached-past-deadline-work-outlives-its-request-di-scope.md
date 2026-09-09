---
description: work a long-running MCP tool leaves running past its response deadline outlives the SDK's per-request DI scope, so anything it resolves lazily from a captured IServiceProvider throws ObjectDisposedException - the caller already holds an in-progress envelope and is never told the work stopped
applies-to:
  - clio/BindingsModule.cs
  - clio/Command/McpServer/Tools/McpProgressHeartbeat.cs
  - clio/Command/McpServer/Tools/ApplicationTool.cs
  - clio/Command/McpServer/Tools/CompileCreatioTool.cs
  - clio/Command/McpServer/Tools/InstallProcessBuilderTool.cs
  - clio/Command/McpServer/Tools/RestartTool.cs
  - clio/Command/McpServer/Tools/ProcessDesigner/RunProcessTool.cs
ticket: clio#1421
date: 2026-09-09
---

**What is true** — `McpProgressHeartbeat.RunWithProgressAndDeadlineAsync` detaches its work on
`Task.Run(..., CancellationToken.None)` so it survives the response deadline and a client disconnect.
The DI graph that work runs on does **not** survive with it. `McpServerOptions.ScopeRequests` defaults
to `true` in ModelContextProtocol 2.2.0 and clio never overrides it, so every `tools/call` gets its own
service scope and the SDK disposes that scope the moment the handler returns — which, on this path, is
the moment the tool answers `section-created=in-progress` / "keep polling", with minutes of work still
ahead of it.

Already-resolved instances keep working; a **lazy** resolution does not. The one factory in
`BindingsModule` that returned a delegate closing over its `IServiceProvider` —
`Func<EnvironmentSettings, ISysSettingsManager>` — therefore threw
`ObjectDisposedException: Cannot access a disposed object. Object name: 'IServiceProvider'` on its first
call inside the detached continuation. It now resolves every collaborator eagerly at registration time
and the returned delegate captures the instances;
`BuildEnvironmentScopedSysSettingsManager` takes those instances instead of a provider so the mistake
is unavailable rather than merely unmade.

Reproduce in about three seconds with no slow stand: start `clio mcp-server` with
`CLIO_MCP_RESPONSE_DEADLINE_SECONDS=1` and call `create-app-section` against a healthy environment.
`BindingsModuleMcpHostGateTests.SysSettingsManagerFactory_ShouldStillBuildAManager_WhenTheResolvingScopeIsAlreadyDisposed`
pins it without any environment at all.

**Why it is this way** — the two lifetimes are owned by different parties and neither can see the
other. The scope belongs to the SDK, so clio cannot extend it, and the detached task belongs to clio,
so the SDK cannot know it exists. The in-flight guard that does protect a long operation
(`McpToolExecutionLock.MarkInUse` / `MarkAvailable`, taken inside the work delegate) pins the
**session container** against eviction, not the request scope — a different provider entirely. Five
tools detach this way: `create-app-section`, `compile-creatio`, `install-process-builder`,
`run-process` and `restart-by-environment-name`.

**What breaks if you ignore it** — the failure is silent in the worst available way, because the
response has already been sent and it says the work is fine. `create-app-section` answered
`error-class=creatio-timeout`, `section-created=in-progress` and "do NOT retry, poll
`list-app-sections` until the section and its pages appear", then died 0.3 s later. Nothing was ever
created and nothing ever would be, so the agent polled a promise that could not come true; on a stand
where the insert takes 90-100 s this cost a reporter an evening (clio#1421). Where the deadline lands
decides which half you see: after the `InsertQuery` the section exists and only the metadata read-back
fails (15 attempts, every one `ObjectDisposedException`), so the same defect also presents as "the
section is there but clio reported an error". The only trace either way is one line on `stderr` from
`ObserveInBackground`, which no MCP client reads.

Before adding any lazily-resolving factory, or any new tool that detaches past its deadline, assume the
request scope is gone by the time the work needs anything: resolve eagerly, or resolve through
`IToolCommandResolver` (the session container, which the in-flight guard does pin).
