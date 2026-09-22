---
description: splitting clio into layers was measured against the e2e change detector and rejected - the type coupling is redundant, not hub-shaped
applies-to:
  - .github/scripts/Select-McpE2eTestFilter.ps1
  - clio.mcp.e2e/TestSelection/mcp-e2e-selection.json
  - spec/mcp-e2e-plan-split/
ticket: GH-1570
date: 2026-09-17
---

**What is true** - decomposing clio into layers would not let the pull-request detector run a
smaller part of `clio.mcp.e2e`. Measured on `master` at `7b39de32d` over a type reference graph of
the 1299 `.cs` files under `clio/`: 450 files reach at most 20 of the 226 MCP tool files. Cutting the
single most-used type raises that to 457; cutting all 22 types named by 60 or more files
(`ILogger`, `Command`, `EnvironmentSettings`, `IApplicationClient`, `IFileSystem`, `Package` and the
rest, together) raises it to 464. Fourteen files of 1299.

**Why it is this way** - the paths are redundant rather than hub-shaped: cutting one leaves several
others, so no extractable set of types unlocks the detector. The granularity win that a file split
would deliver was taken in the detector instead, by making the graph node a type rather than a file
(809 of 1299 files declare more than one top-level type); that alone moved 450 to 532 with no source
change. And for the files that stay imprecise the wide selection is correct, not waste:
`ConsoleLogger` reaches 225 of 226 tools because every MCP session logs, and a layered clio would
make that dependency explicit and directed without making it disappear.

**What breaks if you ignore it** - a large refactoring justified by the wrong number. Someone
returning to this idea will point at the 48-of-60 full runs and conclude the architecture is the
cause. It is not; the measurement and its method are in
[spec/mcp-e2e-plan-split/mcp-e2e-plan-split-analysis.md](../../../spec/mcp-e2e-plan-split/mcp-e2e-plan-split-analysis.md).
Layering may still be worth doing for build time, ownership or isolating the CLI - this record says
nothing about those, only that test selection is not the argument for it.
