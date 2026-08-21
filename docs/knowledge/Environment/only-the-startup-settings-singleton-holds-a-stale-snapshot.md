---
description: settings staleness in a long-lived MCP host is confined to the startup root singleton - every per-environment child container built by ToolCommandResolver constructs its own SettingsRepository and reads appsettings.json fresh
applies-to:
  - clio/BindingsModule.cs
  - clio/Environment/ConfigurationOptions.cs
  - clio/Environment/ISettingsRepository.cs
  - clio/Command/McpServer/Tools/ToolCommandResolver.cs
ticket: ENG-94529
date: 2026-08-19
---

**What is true** — `SettingsRepository` serves reads from a snapshot taken in its constructor, and
`ISettingsRepository.Reload()` exists to refresh it. What the code does not say is how narrow the
staleness is: `BindingsModule.RegisterInto` constructs a **new** `SettingsRepository` on every
`Register(...)` call, so each per-environment child container `ToolCommandResolver` builds reads
`appsettings.json` at container-build time. Only the process-start root singleton — used by
`ToolCommandResolver` itself and by the startup-injected commands behind the non-generic
`BaseTool.InternalExecute(options)` — is frozen.

**Why it is this way** — the child container is the unit of per-environment isolation, and it is
cheaper to build a fresh repository than to share and invalidate one. The consequence was never
intentional; it is a side effect of the composition root being re-entered.

**What breaks if you ignore it** — you conclude from one stale answer that the whole MCP process is
serving pre-edit settings, and either add `Reload()` calls inside command code where they buy nothing
but a file lock and a full deserialize, or chase a phantom cache in the child containers. The reverse
mistake is as bad: because most tools go through a child container, an external edit is picked up
*most* of the time, which makes the remaining root-singleton path read as intermittent flakiness
rather than a fixed boundary. The honest claim is the one the "not found" message already makes — the
list is re-read at call time, tools bound at server start are not.
