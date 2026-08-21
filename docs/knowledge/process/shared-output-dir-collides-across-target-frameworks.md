---
description: passing -o <dir> to dotnet build/test on clio or clio.mcp.e2e makes net10.0 and net8.0 write to the same folder and clobber each other - pin -f net10.0 whenever you redirect the output
applies-to:
  - clio/clio.csproj
  - clio.mcp.e2e/clio.mcp.e2e.csproj
date: 2026-08-19
---

**What is true** — `clio/clio.csproj` and `clio.mcp.e2e/clio.mcp.e2e.csproj` both declare
`<TargetFrameworks>net10.0;net8.0</TargetFrameworks>` when the SDK is 10.0 or newer. A single
`-o <dir>` flattens both target frameworks into one directory, so the two builds overwrite each other's
assemblies. Always pair the redirect with a single framework: `-f net10.0 -o <dir>`.
(`clio.tests/clio.tests.csproj` is single-target `net10.0` and is not affected.)

**Why it is this way** — `-o` overrides the per-framework subdirectory that MSBuild would otherwise
append to the output path. Multi-targeting relies on that subdirectory to keep the two builds apart, and
nothing warns when it is removed.

**What breaks if you ignore it** — the run is not a build error but a mass of phantom test failures:
whichever framework finished last leaves behind assemblies the other framework's test host then loads,
producing hundreds of failures that reproduce nowhere else and disappear as soon as the framework is
pinned. Do not start chasing those failures as a regression in the code under test.
