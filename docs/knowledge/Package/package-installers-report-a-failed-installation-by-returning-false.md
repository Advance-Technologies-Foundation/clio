---
description: IPackageInstaller.Install and IApplicationInstaller.Install report a failed installation OUTCOME by logging "Package installation failed: <reason>" and returning false, so a caller that drops the bool reports success for a failed installation; every caller now checks it and closes with InstallFailureMessages
applies-to:
  - clio/Package/InstallFailureMessages.cs
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
installer returns `false` for it. Every caller checks the bool. `push-pkg` and `install-application` close
with `PushPackageCommand.BuildFailureMessage`, `apply-environment-manifest` records the entry as failed, and
`WorkspaceInstaller` (push-workspace), `WorkspaceMerger` (merge-workspaces), `ApplicationManager.Install`
(deploy-app) and `InstallNugetPackage` (install-nuget-pkg) throw an `InvalidOperationException` naming what
was not installed - through `InstallFailureMessages.NotInstalled`, or the application and the destination for
deploy-app - which their commands turn into exit code 1.

**Why it is this way** — the installer writes the reason, together with the install log, at the moment it
sees it; the caller only adds what failed and decides how to end.

**What breaks if you ignore it** — a call written as a statement, `installer.Install(...);`, compiles,
logs the installer's error, and lets the command report success. Until 2026-11 four callers did that:
push-workspace, merge-workspaces, deploy-app and install-nuget-pkg logged the installer's error and then
Done with exit code 0, so an agent or a CI step that read the exit code went on as if the packages were
installed. Check the bool in any new caller.
