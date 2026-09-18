---
description: the only GitHub Actions lane that runs clio.mcp.e2e.csproj is build.yml's mcp-e2e-noenvironment job with --filter TestCategory=McpE2E.NoEnvironment, so a Category("Unit") guard or any Sandbox fixture in clio.mcp.e2e still has no pre-merge gate on GitHub
applies-to:
  - .github/workflows/build.yml
  - clio.mcp.e2e/
ticket: GH-1570
date: 2026-09-16
---

**What is true** — `.github/workflows/build.yml` runs `clio.mcp.e2e.csproj` in exactly one job,
`mcp-e2e-noenvironment` (`windows-latest`, `net10.0`), with the filter
`TestCategory=McpE2E.NoEnvironment&TestCategory!=McpE2E.Manual&TestCategory!=McpE2E.ProcessDesigner`.
Everything else in that project — every `McpE2E.Sandbox` fixture and the harness self-tests tagged
`Category("Unit")` — is still not executed by any GitHub check. It runs on TeamCity only (`Team_Atf_ClioMcpE2eTests`), and on pull requests only the
subset `Select-McpE2eTestFilter.ps1` selects (see
[teamcity-mcp-e2e-step-reads-the-mcpe2etestfilter-parameter.md](../infra/teamcity-mcp-e2e-step-reads-the-mcpe2etestfilter-parameter.md)).
The job is advisory: it is not wired into the required `Unit Tests` gate.

**Why it is this way** — the NoEnvironment tier needs no Creatio and took ≈ 7 minutes of test time
inside the ≈ 48-minute TeamCity build; on a hosted runner it runs in parallel with the unit shards
and TeamCity pull-request runs skip it. The Sandbox tier needs a stood-up stand that only TeamCity
provides. The `Unit`-tagged self-tests carry no `McpE2E.*` tier and so match neither filter.

**What breaks if you ignore it** — you write an invariant guard in `clio.mcp.e2e` (a fixture-policy
assertion, a contract-drift oracle), tag it `Unit`, see it pass locally and believe it now blocks a
merge. It does not: it runs only when TeamCity runs the full suite, which on a pull request happens
only for infrastructure diffs. Put a guard that must block a merge in `clio.tests`, where the unit
lane runs it (`McpFixturePolicyTests`, `McpE2eSelectionCoverageTests` are the pattern); keep in
`clio.mcp.e2e` only what genuinely needs that project's harness, and tag it `McpE2E.NoEnvironment`
if it needs no stand so that the GitHub job at least runs it.
