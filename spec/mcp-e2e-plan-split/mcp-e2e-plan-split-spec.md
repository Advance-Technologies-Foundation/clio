# MCP e2e plan split — Specification

🔗 **Issue:** [#1570](https://github.com/Advance-Technologies-Foundation/clio/issues/1570)

## Problem

`Team_Atf_ClioMcpE2eTests` (TeamCity, `teamcity-rnd.bpmonline.com`) runs the whole `clio.mcp.e2e`
suite on every pull request that touches `clio/**`. The build is queued by
`.github/workflows/teamcity-mcp-e2e.yml` and its result is posted back as the
`CLIO MCP e2e tests (ATF)` commit status. Measured on trunk build 16036613 (2026-09-16):

| Step | Time |
|---|---|
| Deploy Creatio + `install-gate` | ≈ 7 min |
| `dotnet test` (987 tests, one step, one fixed `--filter`) | 40.6 min |
| of which `McpE2E.NoEnvironment` (≈ 500 tests, no Creatio) | 6.9 min |
| of which `McpE2E.Sandbox` (≈ 490 tests) | ≈ 33 min |
| **Total** | **48 min** |

The deploy is cheap; the time is the tests, and the Sandbox tier is effectively serial (shared
stand, `[NonParallelizable]`, global OData rebuild). A pull request that changes one MCP tool pays
for all 987 tests. On top of the billed durations, every MCP server session costs a fixed start
and dispose window (see `docs/knowledge/Tests/the-mcp-shutdown-window-is-spent-in-full-on-every-session.md`).

## Goal

Run, per pull request, only the e2e fixtures the change can affect — without touching what the
VCS-triggered master builds run, and without multiplying TeamCity build configurations.

## Non-goals

- Several TeamCity plans per domain. Each plan would deploy its own Creatio (≈ 7 min), take a scarce
  agent, post its own status and need `maximumNumberOfBuilds` of its own. Rejected.
- Shrinking the fixed per-session cost of the harness. Separate line of work.
- Making the e2e check required. It stays advisory (Phase 3 of ENG-92669).

## Design

### 1. The NoEnvironment tier moves to GitHub-hosted runners

`build.yml` gains the job `mcp-e2e-noenvironment` (`windows-latest`, `net10.0`):

```
dotnet test clio.mcp.e2e --filter "TestCategory=McpE2E.NoEnvironment&TestCategory!=McpE2E.Manual&TestCategory!=McpE2E.ProcessDesigner"
```

It runs under the same change conditions as the unit shards and is **advisory**: not part of the
required `Unit Tests` gate until its stability on hosted runners is known. Windows on purpose, like
the TeamCity agents, so a path or process difference cannot pass here and fail there.

Pull-request runs on TeamCity exclude this tier (`TestCategory!=McpE2E.NoEnvironment`). Master builds
keep running it, so trunk still has the complete oracle. The repository variable
`MCP_E2E_KEEP_NOENVIRONMENT_ON_TEAMCITY=true` reverts the exclusion without a code change.

### 2. One parameterized TeamCity plan

The step `Run MCP e2e tests (.NET)` now reads `--filter "%McpE2eTestFilter%"`. The parameter's
default is the filter the step had before:

```
TestCategory!=McpE2E.ProcessDesigner&TestCategory!=McpE2E.Manual
```

so VCS-triggered master builds and manual `workflow_dispatch` runs are unchanged. Pull-request runs
override it through the build property sent by `.github/scripts/queue-teamcity-build.ps1`
(`TEST_FILTER` → `McpE2eTestFilter`). The build comment carries `selection: full|subset`, because a
subset-green and a full-green post the same commit status.

### 3. A detector in the repository

`.github/scripts/Select-McpE2eTestFilter.ps1`, driven by
`clio.mcp.e2e/TestSelection/mcp-e2e-selection.json`, runs in the new `select` job of
`teamcity-mcp-e2e.yml` (hosted `ubuntu-latest`, full checkout) and classifies every changed file:

| # | Changed file | Result |
|---|---|---|
| 1 | not under `relevantPaths` (docs, unit tests, other projects) | ignored |
| 2 | matches `fullRunPaths` (`clio/Common/**`, `Program.cs`, `BindingsModule.cs`, McpServer root files, `Tools/BaseTool.cs`, `Tools/Mcp*.cs`, `clio.mcp.e2e/Support/**`, `cliogate/**`, `Directory.Packages.props`, the workflow and scripts themselves, …) | **full run** |
| 3 | top-level `clio.mcp.e2e/<File>.cs` | the fixtures declared in that file |
| 4 | `explicitMappings` entry | the listed fixtures |
| 5 | `clio/Command/McpServer/Tools/**/X.cs` | every fixture whose source names a `*Tool`/`*Tools` class declared in `X.cs`, or contains a tool-name literal bound by `[McpServerTool(Name = …)]` in `X.cs`, or is named `X*E2ETests`; zero matches → **full run** |
| 6 | other `clio/**/*.cs` | the files under `clio/` that name a type declared in it are its consumers (`Program.cs`, `BindingsModule.cs` excepted). All consumers are tool files → the union of rule 5 over them; a consumer elsewhere, or none at all (DI, reflection) → **full run** |
| 7 | anything else under `relevantPaths` | **full run** |

One full-run file anywhere in the diff makes the whole run full. A subset larger than
`maxSubsetFixtures` (60) becomes a full run (command-line length, and at that size the saving is gone).
A diff with no relevant file also resolves to a full run: the safe default is everything, never nothing.
A subset whose fixtures declare no `McpE2E.Sandbox` test becomes mode **none**: the NoEnvironment tier
already ran on GitHub, so no TeamCity build is queued (a deploy for zero tests) and no
`CLIO MCP e2e tests (ATF)` status appears on that pull request.

Rule 6 is sound only because it demands a *closed* consumer set: a type named by any non-tool file, or
by no file at all, has an unknown blast radius. It is deliberately textual and one hop deep.

Dry run over the 14 most recent first-parent merges into `master` (2026-09-16): 2 would have been
subsets (5 and 1 fixtures), 12 full runs — 5 through `fullRunPaths` (`BindingsModule.cs`,
`clio/Common/**`, McpServer prompts and data), 4 because a changed `clio/Command/*.cs` is also
consumed outside the tools, 3 with no relevant file or no direct consumer. The detector therefore
saves little on today's mix of pull requests by itself; the guaranteed saving comes from moving the
NoEnvironment tier out, and `explicitMappings` is where domain knowledge can widen the subsets.

The subset filter is composed as
`(FullyQualifiedName~Clio.Mcp.E2E.A|FullyQualifiedName~Clio.Mcp.E2E.B)&TestCategory!=McpE2E.NoEnvironment&<baseFilter>`.
`TestCategory` (not `Category`) is mandatory in the category part — see
`docs/knowledge/Tests/nunit-adapter-drops-large-non-category-shard-filters.md`.

### 4. A guard in `clio.tests`

`clio.tests/McpE2eSelectionCoverageTests.cs` runs in the pre-merge unit lane (nothing in
`clio.mcp.e2e` does) and asserts the facts the detector depends on:

- every fixture in `Clio.Mcp.E2E` is declared in a top-level `clio.mcp.e2e/*.cs` file (or under `Support/`);
- no fixture name is a prefix of another (`FullyQualifiedName~` is a substring match);
- every automatic fixture is selected by at least one tool source file, or is listed in
  `explicitMappings` / `fullRunOnlyFixtures`; every listed name still exists;
- the manifest's `baseFilter` equals the TeamCity parameter default;
- every `paths:` entry of the trigger workflow is a `relevantPaths` entry;
- the script itself (run in-process through `System.Management.Automation`) returns the expected
  `mode`/`filter` for a tool-only diff, an infrastructure diff, a package-version diff and a docs-only
  diff on the live tree, and for the consumer rules on a synthetic repository with known contents.

The script is the single owner of the textual rules: the guard reads its `-Inventory` output
(fixtures per file, tool files that select each fixture) and compares it with reflection, instead of
re-implementing the regexes in C#.

## Expected effect

| Pull request | Before | After |
|---|---|---|
| one MCP tool (`Tools/X.cs` + its fixture) | 48 min TeamCity | ≈ 10 min GitHub (NoEnvironment) ∥ 7 min deploy + the tool's Sandbox fixtures on TeamCity |
| shared infrastructure (`clio/Common/**`, harness) | 48 min | ≈ 10 min GitHub ∥ ≈ 40 min TeamCity (Sandbox tier only) |
| master (VCS trigger) | 48 min | 48 min, unchanged |

## Known limits

- **A subset-green is not a full-green.** The commit status name is the same; the difference is
  visible in the TeamCity build comment only. The full oracle is the next VCS-triggered master build.
- **Reachability is textual, not semantic.** A fixture that exercises tool B but only names tool A in
  its source is selected by changes to A. The guard proves every fixture is selectable by *some* tool
  file, not by the right one. Reference the tool class (`XTool.ToolName`) from the fixture.
- **Non-MCP product code** (`clio/Command/<domain>/**`, `clio/Package/**`, …) has no mapping and is a
  full run by rule 6. `explicitMappings` is the extension point when a domain's fixtures are known.
- **Hidden per-session cost** is unaffected by the filter.

## Rollout

1. TeamCity: parameter `McpE2eTestFilter` created with the old filter as default; step args switched to
   `--filter "%McpE2eTestFilter%"` — done 2026-09-16 by a.kravchuk, default-preserving.
2. This pull request: workflows, detector, manifest, guard, knowledge records.
3. Watch the first pull-request runs: `MCP e2e NoEnvironment` duration and stability on hosted
   runners, and the TeamCity build comment showing `selection: subset` with the expected fixtures.
4. Later: promote `mcp-e2e-noenvironment` into the required gate; add `explicitMappings` for the
   domains whose fixtures are known.
