---
description: McpContractFixtureBase.ResolveEnvironmentOnceAsync caches only a successful stand probe - memoizing the Assert.Ignore fault would turn one transient unreachable window into a skip for every remaining test of the fixture, reported with a stale reason and indistinguishable from an unconfigured workstation
applies-to:
  - clio.mcp.e2e/Support/Mcp/McpContractFixtureBase.cs
  - clio.mcp.e2e/ApplicationToolE2ETests.cs
  - clio.mcp.e2e/ApplicationSectionMaintenanceToolE2ETests.cs
  - clio.mcp.e2e/DataForgeToolE2ETests.cs
ticket: clio#1427
date: 2026-09-10
---

**What is true** — `ResolveEnvironmentOnceAsync` assigns its cache field only after the probe task has
completed successfully. A probe that ends in `Assert.Ignore` (nothing reachable) is re-run by the next
test of the same fixture. The saving the method exists for is unaffected: on a reachable stand the
`ping-app` child process still runs exactly once per fixture instead of once per test.

**Why it is this way** — the obvious `_field ??= probe()` caches the fault as well, and the fault here
is a skip decision about a shared Creatio instance that recycles: an app-pool restart, the global OData
rebuild, or the delayed recycle a package install triggers each open a window of seconds in which
`ping-app` fails. With the fault cached, whichever test happens to run first during that window decides
the outcome for all 16 tests of `ApplicationToolE2ETests` or all 9 of `DataForgeToolE2ETests`.

**What breaks if you ignore it** — the run stays green and the coverage silently disappears. A fixture
reported as "passed, 15 skipped" with the message "configured sandbox environment was not reachable"
looks exactly like a developer machine with no sandbox configured, so nobody investigates, and the stand
was in fact answering again seconds later. The cost of not caching the failure is one extra `ping-app`
process per test in a fixture that is being skipped anyway.
