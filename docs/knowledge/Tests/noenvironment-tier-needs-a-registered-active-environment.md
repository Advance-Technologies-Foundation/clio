---
description: the McpE2E.NoEnvironment tier only passes when the shared CLIO home has at least one registered environment with a valid ActiveEnvironmentKey, because clio reports "clio settings bootstrap is broken" instead of "environment not found" while CanExecuteEnvTools is false; McpSharedHomeSetUpFixture seeds a loopback placeholder on hosts with no real settings
applies-to:
  - clio.mcp.e2e/McpSharedHomeSetUpFixture.cs
  - clio/Command/McpServer/Tools/ToolCommandResolver.cs
  - clio/Environment/SettingsBootstrapService.cs
  - .github/workflows/build.yml
ticket: GH-1570
date: 2026-09-16
---

**What is true** — "no environment" in `McpE2E.NoEnvironment` means no *reachable* Creatio, not no
*registered* one. `SettingsBootstrapReport.CanExecuteEnvTools` is true only when the settings file
resolves an active environment; while it is false, `ToolCommandResolver` answers every request that
names an unknown environment with "clio settings bootstrap is broken. Repair <appsettings.json>"
rather than with the not-found failure the tier asserts on. The shared-home fixture copies the
host's real `appsettings.json`, so on a developer machine and on the TeamCity agent the requirement
is met by accident. On a GitHub-hosted runner there is no file, the copy starts from `{}`, and the
first `build.yml` run of the tier failed 67 of 584 tests on that one cause.
`McpSharedHomeSetUpFixture.SeedPlaceholderEnvironmentWhenNoneRegistered` therefore registers
`mcp-e2e-placeholder` (`http://127.0.0.1:9`, the discard port, `Safe = true`) and makes it active
when the copied settings register nothing.

**Why it is this way** — the bootstrap deliberately refuses to guess when nothing is registered, and
the placeholder is a harness concern: the product's behaviour on a fresh install is correct as it
stands, and the tier was written and validated against machines that already had environments.

**What breaks if you ignore it** — removing the seed, or copying only part of the settings, turns
the whole NoEnvironment lane red on every clean host with an error text that points at a settings
file nobody edited, and a fixture-level "fix" (widening the expected regex to accept the bootstrap
message) would make the tier stop noticing when the not-found contract itself regresses.
