---
description: env-manage-ui builds the compile command by hand against cloned environment settings, so any collaborator left on the DI container silently targets the process-active environment instead of the selected one
applies-to:
  - clio/Command/EnvManageUiCommand.cs
  - clio/BindingsModule.cs
  - clio/Common/CompilationHistoryPoller.cs
  - clio/Common/CompilationActivityWatcher.cs
ticket: 1422
date: 2026-09-11
---

**What is true** — `EnvManageUiCommand.ExecuteCompileConfiguration` is a second composition root beside
the DI container. It clones the SELECTED environment's `EnvironmentSettings` and builds the compile
command's collaborators against that clone with `ActivatorUtilities.CreateInstance`. Every collaborator
it does NOT pass positionally is resolved from `_serviceProvider`, and the container is bound to the
**process-active** environment: `BindingsModule.RegisterActiveEnvironmentServices` registers
`IDataProvider` as a closure over the single active `EnvironmentSettings`, and
`CompilationHistoryPoller` takes exactly that provider. So a collaborator left on the container reads a
different stand than the one the build is running on.

The invariant "every collaborator of `CompileConfigurationCommand` targets the same environment" exists
nowhere in the command's contract. Nothing fails to compile and nothing throws when it is broken.

Not every dependency needs rebinding: `IApplicationClientFactory` takes the `EnvironmentSettings` as a
call argument (`CreateOwnedClient(settings)`), so the container-bound factory already targets the clone.
What must be rebound is anything that captured settings at construction — the URL builder, the
verdict reader, the availability probe, the reload watcher, and the compilation-history poller.

**Why it is this way** — the menu lists every registered environment and lets the user compile any of
them in a process that was started with one particular environment active. There is no per-environment
child container on this path, so the settings clone is the only thing carrying the selection.

**What breaks if you ignore it** — before completion was derived from the environment, a mis-bound
history poller only cost wrong progress lines, because the HTTP response produced the outcome. It is now
load-bearing: `NewRecordCount`, `LastActivityAtUtc` and `HasErrors` are inputs of
`CompilationCompletionDecider` rules 2, 3 and 4 and therefore of the exit code. With the poller on the
container, a build on the SELECTED environment writes rows the watcher never sees, `NewRecordCount`
stays 0 for the whole run, and past the 90-second startup grace the runtime reload that ends the build
completes the request and rule 4 returns `TransportFailure` — exit 1, "the environment never started
building", for a build that succeeded. Where an intermediary holds the socket open, rule 4 cannot fire
either and the command waits out the full 60-minute deadline instead.

The poller is built once and shared by the command (which takes the baseline from it) and the activity
watcher (which counts rows against that baseline). Two pollers for two environments is the same defect
in a different place.
