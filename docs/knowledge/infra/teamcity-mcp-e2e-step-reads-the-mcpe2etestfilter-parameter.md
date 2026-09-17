---
description: the TeamCity step "Run MCP e2e tests (.NET)" of Team_Atf_ClioMcpE2eTests runs --filter "%McpE2eTestFilter%"; the parameter default is the full-suite filter and pull-request runs override it with the subset chosen by Select-McpE2eTestFilter.ps1, so a subset-green posts the same commit status as a full-green
applies-to:
  - .github/workflows/teamcity-mcp-e2e.yml
  - .github/scripts/queue-teamcity-build.ps1
  - .github/scripts/Select-McpE2eTestFilter.ps1
  - clio.mcp.e2e/TestSelection/mcp-e2e-selection.json
  - clio.tests/McpE2eSelectionCoverageTests.cs
ticket: GH-1570
date: 2026-09-16
---

**What is true** — since 2026-09-16 the `dotnet test` step of `Team_Atf_ClioMcpE2eTests` no longer
carries a literal filter. Its arguments are
`--filter "%McpE2eTestFilter%"`, and the build parameter `McpE2eTestFilter` defaults to
`TestCategory!=McpE2E.ProcessDesigner&TestCategory!=McpE2E.Manual` — exactly the literal the step
had before. Nothing in this repository defines that default; the job config does.
`clio.mcp.e2e/TestSelection/mcp-e2e-selection.json` mirrors it as `baseFilter`, and
`McpE2eSelectionCoverageTests.Manifest_ShouldKeepTheTeamCityBaseFilter` pins the mirror, not the
original.

Pull-request builds queued by `.github/workflows/teamcity-mcp-e2e.yml` send the property
`McpE2eTestFilter` with whatever `Select-McpE2eTestFilter.ps1` chose: a list of
`FullyQualifiedName~Clio.Mcp.E2E.<Fixture>` terms plus `TestCategory!=McpE2E.NoEnvironment` plus the
base filter. VCS-triggered master builds and `workflow_dispatch` runs send nothing and get the default.
The build comment says `selection: full` or `selection: subset`; the commit status
`CLIO MCP e2e tests (ATF)` does not.

**Why it is this way** — the full suite is ≈ 41 minutes of test time on a ≈ 48-minute build, and
most of it cannot be affected by a one-tool pull request. One parameterized plan was chosen over
several plans because every extra plan is another ≈ 7-minute Creatio deploy on a scarce agent. The
default stays the full suite so trunk keeps the complete oracle.

**What breaks if you ignore it** — renaming the parameter, or changing its default, in TeamCity
without touching `baseFilter` here is silent on both sides: the guard still passes (it pins the
mirror), the queue script still sends the old name, and master builds start running whatever the new
default says — or, if the step reference is broken, `--filter ""`, i.e. everything including the
manual destructive fixtures. And a green `CLIO MCP e2e tests (ATF)` on a pull request is not proof
the whole suite passed: it may have been 5 fixtures, and a pull request whose fixtures are all
NoEnvironment-only gets no status at all. Open the TeamCity build and read the comment.
