---
description: a test that exercises the clio.mcp.e2e harness itself (no MCP server, no stand) belongs in clio.tests/McpE2EHarness - clio.tests already references clio.mcp.e2e and the project grants it InternalsVisibleTo, so the internal Support types stay internal and the tests still run in the fast unit lane
applies-to:
  - clio.mcp.e2e/clio.mcp.e2e.csproj
  - clio.tests/McpE2EHarness/
  - clio.mcp.e2e/Support/
ticket: clio#1427
date: 2026-09-10
---

**What is true** — `clio.tests` has a project reference to `clio.mcp.e2e` (that is how
`McpFixturePolicyTests` reflects over the e2e assembly), and `clio.mcp.e2e.csproj` declares
`InternalsVisibleTo("clio.tests")`. A test for a harness type under `clio.mcp.e2e/Support/`
(`IisApplicationPoolResolver`, `BoundedCleanup`, `ClioCliCommandRunner`, `MessageCollectingProgress`,
`WorkerSpawnObserver`, …) therefore lives in `clio.tests/McpE2EHarness/` with `[Category("Unit")]`,
without making a single Support type public and without extracting a shared support assembly. Allure
attributes have to be stripped: `clio.tests` does not reference Allure.NUnit.

**Why it is this way** — no GitHub Actions lane runs `clio.mcp.e2e`; the project reaches a pull request
only through the TeamCity stand build. A harness test placed there is gated by a 47-minute build with a
full Creatio deploy, so a broken XML parser is reported 45 minutes late, and reports nothing at all when
the stand is down. The same test in `clio.tests` answers in about a second.

**What breaks if you ignore it** — the two failure modes above, plus the misleading `E2E` suffix: a
fixture named `…E2ETests` that starts no process teaches the next reader that the suffix means nothing.
The inverse mistake is worse: speed is NOT the criterion for moving a test out. `McpWorkerWorkingDirectoryE2ETests`
finishes in milliseconds and must stay, because it spawns a real child process and asserts the working
directory that process reports about itself — cross-process behaviour no in-process test can cover.
Before moving a fast fixture, check what it starts, not how long it takes.
