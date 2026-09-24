---
description: Team_Atf_ClioUnitTests is configured by hand in TeamCity (five dotnet steps with their framework and SDK, BranchNameClio root, Commit Status Publisher) and nothing in the repository reproduces it, so a TargetFramework change in any test project it runs must be mirrored there or the PR status CLIO Unit Tests (ATF) turns red on every PR
applies-to:
  - .github/workflows/teamcity-unit-tests.yml
  - .github/workflows/build.yml
  - .github/scripts/queue-teamcity-build.ps1
  - clio.tests/clio.tests.csproj
  - Clio.Analyzers.Tests/Clio.Analyzers.Tests.csproj
  - Creatio.ConflictResolver.Tests/Creatio.ConflictResolver.Tests.csproj
  - cliogate.tests/cliogate.tests.csproj
  - clio/clio.csproj
ticket: ENG-92669
date: 2026-09-16
---

**What is true** — the TeamCity build configuration `Team_Atf_ClioUnitTests` (project `Team_Atf`,
teamcity-rnd.bpmonline.com) mirrors every test lane of `build.yml` as five sequential `dotnet` steps
on a Windows agent, each set to run even when an earlier step failed:

| Step | Command | Framework | Required SDK |
|---|---|---|---|
| `clio.tests (unit + integration)` | `test clio.tests/clio.tests.csproj` | `net10.0` | 10 |
| `Analyzer tests` | `test Clio.Analyzers.Tests`, `-p:RunAnalyzers=false` | `net10.0` | 10 |
| `ConflictResolver tests` | `test Creatio.ConflictResolver.Tests`, Release, `-p:RunAnalyzers=false` | `net8.0` | 8, 10 |
| `ClioGate tests (net472)` | `test cliogate.tests`, Release | project default (`net472`) | 10, 4.7.2 |
| `clio net8.0 product compatibility build` | `build clio/clio.csproj`, `-p:RunAnalyzers=false` | `net8.0` | 8, 10 |

Since 2026-09-17 this is the **only** place `cliogate.tests` runs: `build.yml` had a `ClioGate
Tests` job on a self-hosted Windows runner (hosted runners cannot reach the private Nexus feed the
gate restores from), and it was removed as an exact duplicate of the step above. It was already
restricted to same-repo pull requests, the same restriction the TeamCity trigger carries, so no
pull request lost coverage - but if no in-network runner can queue the TeamCity build, nothing runs
`cliogate.tests` at all.

Two surfaces are **not** covered after that removal, and both are silent (everything reads green):

- a push to the `test` branch that changes `cliogate/**` - `build.yml` triggers on pushes to
  `master` and `test`, `teamcity-unit-tests.yml` triggers only on pull requests to `master` plus
  `workflow_dispatch`, and this config's VCS trigger builds `refs/heads/%BranchNameClio%` with
  default `master`, so nothing runs `cliogate.tests` for that push;
- a pull request touching only `cliogate/**` or `cliogate.tests/**` - `build.yml` no longer has a
  paths filter watching those directories, so every test job is skipped, skipped jobs satisfy the
  required contexts, and the only signal left is the advisory `CLIO Unit Tests (ATF)` status.

To cover `cliogate.tests` for the `test` branch, extend this config's branch specification (or the
`teamcity-unit-tests.yml` trigger) rather than assuming `build.yml` still does it.

It checks clio out from `refs/heads/%BranchNameClio%` (default `master`) through a VCS root used by
this config only, and posts the `CLIO Unit Tests (ATF)` commit status through the GitHub App
connection "TeamCity ATF". `.github/workflows/teamcity-unit-tests.yml` queues it for every same-repo
PR that touches the paths it lists (docs-only PRs are skipped) by setting `BranchNameClio`. None of
this lives in the repository: there is no `.teamcity/` directory and no Kotlin DSL, the settings were
entered through the REST API and the UI, and the account that can change them is a `Team_Atf`
PROJECT_ADMIN.

**Why it is this way** — the configuration predates the PR trigger and was parameterised in place
rather than recreated, and TeamCity's own pull-request discovery cannot reach public github.com from
the internal server, so the branch has to arrive as a parameter (same model as
`Team_Atf_ClioMcpE2eTests`, see [mcp-e2e-teamcity-builds-all-report-branch-trunk](mcp-e2e-teamcity-builds-all-report-branch-trunk.md)).

**What breaks if you ignore it** — a change to `<TargetFramework>` in any of the five projects above, a new
test project in `build.yml`, or a change to the SDK the tests need, is not visible to TeamCity. PR #1075 dropped `net8.0` on 2026-08-14 and the
config kept asking for it; every build from 2026-08-17 to 2026-09-16 (219 of them) died on start with
`NETSDK1005` on SDK 10 agents or a TargetFrameworkInference error on SDK 9 agents, and since nothing
consumed the config nobody saw it. Now that the status is on every PR, the same drift paints every
PR with a red `CLIO Unit Tests (ATF)` that has nothing to do with the PR. When you move a test
project's framework or add a lane to `build.yml`, change the matching step's "Framework" and
"Required SDK" (or add the step) in TeamCity in the same change, and say so in the PR.
