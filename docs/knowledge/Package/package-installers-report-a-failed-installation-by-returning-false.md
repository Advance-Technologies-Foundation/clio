---
description: IPackageInstaller.Install and IApplicationInstaller.Install report a failed installation OUTCOME by logging "Package installation failed: <reason>" and returning false, so a caller that drops the bool reports success for a failed installation; merge-workspaces, deploy-app and install-nuget-pkg still drop it
applies-to:
  - clio/Package/BasePackageInstaller.cs
  - clio/Package/ApplicationInstaller.cs
  - clio/Package/IPackageInstaller.cs
  - clio/Package/IApplicationInstaller.cs
  - clio/Workspace/WorkspaceInstaller.cs
  - clio/Workspace/WorkspaceMerger.cs
  - clio/Command/ApplicationManager.cs
  - clio/Package/NuGet/InstallNugetPackage.cs
date: 2026-11-11
---

**What is true** — the installers return `false` for an installation that ran and failed: the service's
response says it failed, the install log lacks the success marker, or the backup before it could not be
created. Each of these writes `[ERR] - Package installation failed: <reason>` first. A package that is not
found by its path also returns `false`, after a plain `WriteLine`. Failures to reach the environment -
upload, login, the design-node session, a body that is not JSON - throw instead. So does an invalid gzip
archive on the application-installer path (`ThrowInvalidGZipArchiveInstallException`); the package
installer returns `false` for it. `push-pkg` and `install-application` check the bool and close with
`PushPackageCommand.BuildFailureMessage`. `WorkspaceInstaller.InstallApplication` throws an
`InvalidOperationException` naming the packages, which `push-workspace` turns into exit code 1.

**Why it is this way** — the installer writes the reason, together with the install log, at the moment it
sees it; the caller only adds what failed and decides how to end.

**What breaks if you ignore it** — a call written as a statement, `installer.Install(...);`, compiles,
logs the installer's error, and lets the command report success. Three callers still do that:
`WorkspaceMerger.InstallApplication` (merge-workspaces), `ApplicationManager` (deploy-app) and
`InstallNugetPackage` (install-nuget-pkg). An agent or a CI step that reads their exit code goes on as if
the packages were installed. Check the bool in any new caller, and in those three when you touch them.
