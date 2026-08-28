---
description: rebundle-process-builder.ps1 rebuilds exactly one Configuration/Framework clio output, so an install run from another one (Debug vs Release, net10.0 vs net8.0) still ships the previous CrtProcessBuilder archive
applies-to:
  - rebundle-process-builder.ps1
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
ticket: ENG-91845
date: 2026-08-19
---

**What is true** — clio multi-targets `net10.0;net8.0` and is built in both Debug and Release, so up
to four `clio/bin/<Configuration>/<Framework>` outputs can coexist. `rebundle-process-builder.ps1`
drives ONE of them (`-Configuration` / `-Framework`, auto-detected when only one exists) and rebuilds
only that one after producing the archive. It then merely NOTEs the other outputs
(`clio\bin\... holds a DIFFERENT archive. An install run from there ships that one.`).

**Why it is this way** — an install command resolves the bundled archive from the build output of the
clio that runs it, and rebuilding every target framework on every rebundle would cost several minutes
for outputs the operator is usually not using. The script prints a warning instead of rebuilding.

**What breaks if you ignore it** — the local verification silently proves the wrong thing: the
rebundle succeeds, the pins in `clio.tests/Common/BundledProcessBuilderPackageTests.cs` match, and
then `install-process-builder` run from a different output installs the OLD package and reports
success. Nothing downstream distinguishes it, because the version an environment records comes from
the archive that was actually installed. The warning is printed at rebundle time only, so whoever
runs the install later never sees it - check which output your `dotnet run` / `clio` resolves to before
believing the result. On Windows a running `clio mcp-server` also locks its own output's executable,
which is why the other framework's output tends to be the stale one.
