---
description: the McpE2E.NoEnvironment tier is only green when the runner's clio settings carry an ActiveEnvironmentKey that points at a registered environment - on a fresh machine every invalid-environment test fails with "clio settings bootstrap is broken" instead of "not found", because CanExecuteEnvTools is derived from the resolved active environment, not from file health
applies-to:
  - clio.mcp.e2e/
  - clio.mcp.e2e/McpSharedHomeSetUpFixture.cs
  - clio/Environment/SettingsBootstrapService.cs
  - clio/Command/McpServer/Tools/ToolCommandResolver.cs
ticket: ENG-92150
date: 2026-09-09
---

**What is true** — `SettingsBootstrapService.BuildResult` sets
`SettingsBootstrapReport.CanExecuteEnvTools = resolvedEnvironment is not null`, and an environment
resolves only when `ActiveEnvironmentKey` names a key present in `Environments`. `ToolCommandResolver`
checks that flag BEFORE it looks the requested environment up, so with zero registered environments a
call for `missing-env-<guid>` answers *"clio settings bootstrap is broken. Repair <path>…"* rather than
the environment-not-found text the `*_Should_Report_Invalid_Environment` tests assert on. The suite's
`McpSharedHomeSetUpFixture` copies the runner's settings into a suite-owned `CLIO_HOME` verbatim, so an
empty runner profile propagates into every child server. On the TeamCity agent the sandbox environment
is always registered and active, which is why the tier is green there and red on a clean container
until one environment is registered — any reachable-or-not URL will do:
`clio reg-web-app local-unreachable -u http://127.0.0.1:9 -l Supervisor -p Supervisor`
(`reg-web-app` prints a runtime auto-detect error for an unreachable host but still registers and
activates the environment).

**Why it is this way** — the flag answers "can env-bound tools run at all", and an install with no
active environment cannot run them regardless of which name the caller passes; the tests target the
next check down (name lookup), which is only reached once the first one passes.

**What breaks if you ignore it** — a developer or agent running
`dotnet test clio.mcp.e2e --filter "Category=McpE2E.NoEnvironment"` on a fresh machine sees ~70
failures with the same bootstrap text, concludes the invalid-environment contract regressed, and starts
debugging `ToolCommandResolver`. Nothing regressed: register one environment and rerun. Do not "fix"
the tests to accept the bootstrap text — it would hide a real regression of `CanExecuteEnvTools` on CI.
