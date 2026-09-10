---
description: clio.mcp.e2e multi-targets net8.0 and net10.0, so a .NET 9+ only type (System.Threading.Lock) compiles locally on net10.0 and breaks the TeamCity build in its first five minutes - a local single-framework build is not evidence the suite still compiles
applies-to:
  - clio.mcp.e2e/clio.mcp.e2e.csproj
  - clio.mcp.e2e/Support/Mcp/McpServerSession.cs
ticket: clio#1427
date: 2026-09-10
---

**What is true** — `clio.mcp.e2e.csproj` sets `TargetFrameworks` to `net8.0`, and to `net10.0;net8.0`
when the SDK is 10 or newer. `dotnet build -f net10.0` therefore proves nothing about the framework
TeamCity also compiles. `System.Threading.Lock` is the concrete trap: it exists from .NET 9, and on
net8.0 the name binds to an inaccessible internal type, so the file fails with CS0122 rather than a
missing-type error that would read as obviously version-related.

**Why it is this way** — the suite still has to run against the .NET 8 clio build, so both frameworks
stay in the project. Nothing in the local inner loop forces you to build both.

**What breaks if you ignore it** — the `CLIO MCP e2e tests (ATF)` build fails after about four and a
half minutes, in the compile step, before a single test runs. The GitHub commit status carries no test
names and no compiler output, and every GitHub Actions check stays green because those lanes build
`clio.tests`, not this project, so the failure looks like a stand or infrastructure problem and invites
a pointless re-run of a 47-minute build. Build BOTH frameworks before pushing a change to this project:
`dotnet build clio.mcp.e2e/clio.mcp.e2e.csproj -f net8.0` and `-f net10.0`.

**Do not invert this into "an early failure is always a compile error".** That over-generalization was
written here once and is wrong. An early failure means only that the build died before the test step,
and there is a second cause with the same signature: a build queued while another one is still running.
`queue-teamcity-build.ps1` de-dupes only the SAME commit, so a push during a live build starts a second
concurrent full-Creatio build, and the newer one dies early. Observed on 2026-09-10: 16002823 died at
4m38s running alone, and its cause was this file's compile error; 16003535 died at 7m30s while 16003496
was still running, with `clio.mcp.e2e` and `clio.tests` both compiling clean in Release for net8.0 and
net10.0. Distinguish them by whether another build of this configuration was running, not by the
duration — the `.../commits/<sha>/statuses` endpoint lists the pending entries and therefore the real
start times, which the `/status` endpoint does not.
