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

**Do not invert this into "an early failure is always a compile error", and do not blame a concurrent
build either.** Both inversions were written here and both are wrong. An early failure means only that
the build died before the test step; the GitHub commit status carries neither a step name nor compiler
output, so the cause has to come from the TeamCity log. Evidence from 2026-09-10: 16002823 died at 4m38s
running alone and its cause was this file's compile error, which is the one mechanism proven here;
16000226 and 16003535 died at 2m30s and 7m30s while another build of this configuration was running,
with both projects compiling clean in Release for net8.0 and net10.0, and their cause was never
established. Concurrency by itself is NOT it: 16003887 ran the whole 48m54s alongside another build of
the same configuration and passed, while that other build failed. So: rule out the compile error first,
because it is cheap and invisible to every GitHub lane, then read the build log — do not infer a cause
from the duration. The `.../commits/<sha>/statuses` endpoint lists the pending entries and therefore the
real start times, which `/status` hides; that is how you tell what else was running.
