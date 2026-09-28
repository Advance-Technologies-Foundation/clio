---
description: clio.process.fixture is an Exe named git; without Private="false" on its ProjectReference a git apphost without git.dll lands in the clio.tests output, bare-name "git" starts it, and "The application to execute does not exist ... git.dll" becomes the quoted reason of any "Test host process crashed"
applies-to:
  - clio.mcp.e2e/clio.mcp.e2e.csproj
  - clio.process.fixture/clio.process.fixture.csproj
  - clio.tests/clio.tests.csproj
  - clio.tests/TestOutputDirectoryTests.cs
  - clio.tests/McpE2eSelectionCoverageTests.cs
  - clio.tests/Command/ProcessModel/RecordFilterDirectionSweepTests.cs
date: 2026-09-25
---

**What is true** — `clio.mcp.e2e` references `clio.process.fixture` (an Exe with `AssemblyName=git`) for
build order only; every consumer runs it from `clio.process.fixture/bin/<configuration>/<tfm>/`. An Exe hands
its apphost and, via `AddDepsJsonAndRuntimeConfigToCopyItemsForReferencingProjects`, its `deps.json` and
`runtimeconfig.json` to each referencing project's copy-to-output items, transitively.
`ReferenceOutputAssembly="false"` drops only `git.dll`/`git.pdb`; the per-reference switch for the rest is
`Private="false"`. Without it `clio.tests/bin/<cfg>/<tfm>/` held a git apphost that cannot start. Measured
on Windows: a bare-name `git` from a process in that folder (as `testhost.exe` is) resolves to it before
`PATH`, exits `0x8000809A` and prints `The application to execute does not exist: '<dir>\git.dll'`.
`RecordFilterDirectionSweepTests.TrackedFiles` makes such a launch with stderr inherited, swallows the
failure and falls back to a directory walk, so it stays green.

**Why it is this way** — the fixture must be an Exe named `git` to stand in for Git on `PATH`, and no project
needs a copy of it in its own output.

**What breaks if you ignore it** — vstest keeps the test host's stderr and quotes it as the reason whenever
the host dies for any cause, so an unrelated abort reads "Test host process crashed : The application to
execute does not exist: ...git.dll" and sends the reader after git. The aborts that surfaced this were
`--blame-hang-timeout` kills (see `nunit-adapter-drops-large-non-category-shard-filters.md`); deleting the
files removes the misleading line, not the kill. A complete fixture there would be worse: every bare-name
`git` from a unit test would silently run the fake. `TestOutputDirectoryTests` fails on a `git` or `git.*`
file, or a `<name>.runtimeconfig.json` without its `<name>.dll`, beside `clio.tests.dll`.
