---
description: create-theme brand mode must leave BuildThemeOptions.EnvironmentName unset - the environment reaches the build as resolvedSettings, and setting both trips the version/environment exclusion guard
applies-to:
  - clio/Command/McpServer/Tools/CreateThemeTool.cs
  - clio/Command/Theming/BuildThemeCommand.cs
ticket: ENG-93989
date: 2026-08-19
---

**What is true** — when `CreateThemeTool.TryBuildBrandCss` maps brand arguments onto
`BuildThemeOptions`, it sets `Version` from the gate resolution and deliberately does **not** set
`EnvironmentName`. The environment reaches the build only through the `resolvedSettings` argument of
`BuildThemeCommand.TryBuildTheme`.

**Why it is this way** — `BuildThemeCommand.RejectAmbiguousVersionSource` treats a non-empty
`Version` together with a non-empty `EnvironmentName` as two competing version sources and throws
`--version and --environment-name are mutually exclusive`. That guard exists for the CLI surface,
where the two really are alternatives; in brand mode both values are legitimately known at once, so
the environment has to arrive by the other channel.

**What breaks if you ignore it** — copying `args.EnvironmentName` into `BuildThemeOptions` alongside
the resolved `Version` makes every brand-mode `create-theme` call fail with the mutual-exclusion
error, on every environment. The failure is total rather than intermittent, but the message points at
CLI flags the MCP caller never passed, so it reads as a clio defect instead of a mapping bug.
