---
description: IPackageInstaller.Install and IApplicationInstaller.Install report a failed installation by logging "Package installation failed: <reason>" and returning false, never by throwing - a caller that drops the bool reports success for an installation that failed (push-workspace logged Done and exited 0)
applies-to:
  - clio/Package/BasePackageInstaller.cs
  - clio/Package/IPackageInstaller.cs
  - clio/Package/IApplicationInstaller.cs
  - clio/Workspace/WorkspaceInstaller.cs
date: 2026-11-11
---

**What is true** — both installers end a failed installation (the server's response says it failed, the
install log lacks the success marker, or the backup could not be created) by writing an `[ERR]` line
through the logger and returning `false`. They throw only for a few recognized pre-flight failures
(an invalid gzip archive, a package folder without `descriptor.json`). So the exit code and any closing
"Done" are the caller's to decide from the returned bool. `push-pkg` and `install-application` check it
and close with `PushPackageCommand.BuildFailureMessage`; `WorkspaceInstaller.InstallApplication` throws
`InvalidOperationException` naming the packages, which `push-workspace` turns into exit code 1.

**Why it is this way** — the installer already wrote the reason, together with the install log, at the
moment it saw it; the caller only adds what failed and decides how to end.

**What breaks if you ignore it** — a new caller that writes `installer.Install(...)` as a statement
compiles, logs the installer's error, and then reports success. Until 2026-11 `WorkspaceInstaller` did
exactly that: push-workspace printed "Package installation failed: ..." and then "Done" with exit code 0,
and on the application-installer path (the one MCP push-workspace uses) it even logged "Installation
completed successfully." in between. An agent or a CI step that reads the exit code goes on as if the
packages were installed.
