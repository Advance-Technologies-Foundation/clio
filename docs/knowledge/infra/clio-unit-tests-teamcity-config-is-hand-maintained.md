---
description: Team_Atf_ClioUnitTests is configured by hand in TeamCity (framework, SDK, BranchNameClio root, Commit Status Publisher) and nothing in the repository reproduces it, so a TargetFramework change in clio.tests.csproj must be mirrored there or the PR status CLIO Unit Tests (ATF) turns red on every PR
applies-to:
  - .github/workflows/teamcity-unit-tests.yml
  - .github/scripts/queue-teamcity-build.ps1
  - clio.tests/clio.tests.csproj
ticket: ENG-92669
date: 2026-09-16
---

**What is true** — the TeamCity build configuration `Team_Atf_ClioUnitTests` (project `Team_Atf`,
teamcity-rnd.bpmonline.com) runs `dotnet test clio.tests/clio.tests.csproj --framework net10.0` with
"Required SDK" `10` on a Windows agent, checks clio out from `refs/heads/%BranchNameClio%` (default
`master`) through a VCS root used by this config only, and posts the `CLIO Unit Tests (ATF)` commit
status through the GitHub App connection "TeamCity ATF". `.github/workflows/teamcity-unit-tests.yml`
queues it for every same-repo PR that touches the paths it lists (docs-only PRs are skipped) by
setting `BranchNameClio`. None of this lives in the repository:
there is no `.teamcity/` directory and no Kotlin DSL, the settings were entered through the REST API
and the UI, and the account that can change them is a `Team_Atf` PROJECT_ADMIN.

**Why it is this way** — the configuration predates the PR trigger and was parameterised in place
rather than recreated, and TeamCity's own pull-request discovery cannot reach public github.com from
the internal server, so the branch has to arrive as a parameter (same model as
`Team_Atf_ClioMcpE2eTests`, see [mcp-e2e-teamcity-builds-all-report-branch-trunk](mcp-e2e-teamcity-builds-all-report-branch-trunk.md)).

**What breaks if you ignore it** — a change to `<TargetFramework>` in `clio.tests.csproj`, or to the
SDK the tests need, is not visible to TeamCity. PR #1075 dropped `net8.0` on 2026-08-14 and the
config kept asking for it; every build from 2026-08-17 to 2026-09-16 (219 of them) died on start with
`NETSDK1005` on SDK 10 agents or a TargetFrameworkInference error on SDK 9 agents, and since nothing
consumed the config nobody saw it. Now that the status is on every PR, the same drift paints every
PR with a red `CLIO Unit Tests (ATF)` that has nothing to do with the PR. When you move the test
project's framework, change the step's "Framework" and "Required SDK" in TeamCity in the same change,
and say so in the PR.
