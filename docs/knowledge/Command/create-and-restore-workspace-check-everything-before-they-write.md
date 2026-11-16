---
description: create-workspace (with an environment) and restore-workspace write nothing until every check has passed - local folder checks, then one ping through ApplicationPingExtensions.EnsureAnswers, then everything read from the environment; restore then downloads with DownloadPackagesFromAnsweredEnvironment so it logs in once
applies-to:
  - clio/Command/CreateWorkspaceCommand.cs
  - clio/Workspace/WorkspaceCreator.cs
  - clio/Workspace/WorkspaceRestorer.cs
  - clio/Package/PackageDownloader.cs
  - clio/Package/IPackageDownloader.cs
  - clio/WebApplication/ApplicationPing.cs
date: 2026-11-12
---

**What is true** — the two commands that build a workspace from an environment write nothing until every
check has passed. They check in this order:

1. **Local checks.** Not inside an existing workspace, no conflicting template folders:
   `IWorkspace.ValidateCanCreate`, which `WorkspaceCreator.Create` runs again.
2. **The environment.** `IApplicationPing.EnsureAnswers(env, "<what was not done>", name)` refuses an
   environment with no URL ("environment '<name>' is not registered") and one that does not answer ("the
   environment did not answer (<reason>). Check it with ping-app ..."). restore-workspace runs it through
   `IPackageDownloader.EnsureEnvironmentAnswers`, before the CreatioSDK lookup on api.nuget.org.
3. **What is read from the environment.** The customer package list (`WorkspaceCreator.Create` reads it
   before it copies the template) and the application for `--AppCode` (`CreateWorkspaceCommand` reads it
   before `Create`).

restore-workspace then downloads with `DownloadPackagesFromAnsweredEnvironment`, which does not ping again,
so a restore logs in once. `create-workspace -e` pings twice: once itself, before writing, and once in the
restore it runs.

**Why it is this way** — a half-written workspace is worse than none. Once `.clio/workspaceSettings.json`
exists, restore-workspace no longer falls back to create-workspace, so a skeleton left by a failed login or by
an unreadable package list had to be deleted by hand. The local checks come first so that "inside an existing
workspace" is the error shown, not a failed login against an environment that would not have been used. The
#1119 rule, that nothing is written before the checks, comes from the same place.

**What breaks if you ignore it** — a new read from the environment added after `CopyTemplateFolder` or after
`Create` brings the skeleton back on failure. A new caller that pings and then calls `DownloadPackages` logs in
twice; use `DownloadPackagesFromAnsweredEnvironment` after `EnsureEnvironmentAnswers`. A new "the environment
did not answer" message written by hand drifts from the shared wording; use `EnsureAnswers`.
`CreateWorkspaceCommandEnvironmentCheckTests`, `WorkspaceCreatorTests`, `WorkspaceRestorerTests` and
`RestoreWorkspaceToolE2ETests` pin the order.
