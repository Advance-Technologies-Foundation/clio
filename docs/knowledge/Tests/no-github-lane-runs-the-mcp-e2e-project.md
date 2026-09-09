---
description: no GitHub Actions lane runs clio.mcp.e2e.csproj itself - the project reaches a pull request only through the TeamCity trigger workflow and a TeamCity-published commit status, so a Category("Unit") guard placed there is gated by a ~45-minute stand-backed build instead of the fast unit lane
applies-to:
  - .github/workflows/
  - .github/workflows/teamcity-mcp-e2e.yml
  - clio.mcp.e2e/
ticket: ENG-92558
date: 2026-08-30
---

**What is true** — `.github/workflows/build.yml` builds `clio.tests.dll` and runs its existing
`TestCategory!=Integration` and `TestCategory=Integration` predicates through GitHub-only shards; it also
runs `Clio.Analyzers.Tests.csproj`. No workflow in `.github/workflows/` invokes `dotnet test` on
`clio.mcp.e2e.csproj` (the `clio.mcp.e2e/**` entry in `build.yml`'s path filter only decides whether the
`clio.tests` shards run). The project still reaches a pull request, by a different route:
`.github/workflows/teamcity-mcp-e2e.yml` (ENG-92669) fires on `pull_request` for `clio/**`,
`clio.mcp.e2e/**`, `clio.process.fixture/**`, `cliogate/**` and `Directory.Packages.props`, queues
`Team_Atf_ClioMcpE2eTests`, and TeamCity's Commit Status Publisher posts the result onto the PR head SHA
as the **commit status** `CLIO MCP e2e tests (ATF)`. So a `[Category("Unit")]` test in
`clio.mcp.e2e` IS executed pre-merge — inside a full-Creatio build that takes roughly 45 minutes,
under a run-step filter that is exclusion-based (`TestCategory!=McpE2E.ProcessDesigner`) and therefore
does not exclude it.

**Why it is this way** — the project was built around a live Creatio stand and is driven from
TeamCity, so it was never wired into the GitHub test lanes; ENG-92669 added visibility on the PR rather
than moving execution. TeamCity is internal and clio is public, so the result arrives as a commit status
published by the "TeamCity ATF" GitHub App, not as a check run — which is why it does not appear in the
GitHub Actions job list and is easy to miss when reading `.github/workflows/` alone.

**What breaks if you ignore it** — two opposite mistakes, both observed. Reading only
`.github/workflows/` you conclude the project is ungated and that a guard placed there blocks nothing —
wrong: it does gate, through the commit status, and a red MCP e2e build holds the PR in "Some checks
haven't completed yet". Conversely, putting a cheap environment-free guard there because "it runs on the
PR anyway" costs ~45 minutes of feedback instead of ~2, makes the assertion depend on stand
availability, and lengthens the build that is already the pull request's critical path. Put a guard that
must block a merge fast in `clio.tests`, where the unit lane runs it; keep in `clio.mcp.e2e` only what
genuinely needs that project's harness. Whether the commit status is a *required* check in branch
protection is a separate question this record does not answer — the workflow header calls Phase 1
advisory; read the repository settings, do not assume.
