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

- Sharding the run across several parallel TeamCity builds. `McpE2eTestFilter` already makes it
  possible without a new plan, and agents are not scarce (107 authorized, median queue wait 0 min,
  five e2e builds observed running at once). It was rejected for a different reason: it multiplies
  the number of Creatio stands per pull request by the shard count, and the stands, not the agents,
  are the unmeasured constraint. Out of scope here, which is about running fewer tests rather than
  running the same tests faster.
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
`clio.mcp.e2e/TestSelection/mcp-e2e-selection.json`, runs in the `select` job of
`teamcity-mcp-e2e.yml` (hosted `ubuntu-latest`, full checkout) and classifies every changed file:

| # | Changed file | Result |
|---|---|---|
| 1 | `ignoredPaths` (`clio/docs/**`, `clio/help/**`, `clio/Wiki/**`, markdown outside `clio/tpl/**`, `cliogate/Resources/**`) | ignored - none of it is compiled or loaded |
| 2 | not under `relevantPaths` | ignored |
| 3 | a `registrationFiles` entry (`Program.cs`, `BindingsModule.cs`) | classified from its **changed lines** (see below) |
| 4 | matches `fullRunPaths` | **full run** |
| 5 | top-level `clio.mcp.e2e/<File>.cs` | the fixtures declared in that file |
| 6 | `explicitMappings` entry | the listed fixtures |
| 7 | `clio/Command/McpServer/Tools/**/X.cs` **that declares a tool** | every fixture whose source names a `*Tool`/`*Tools` class declared in `X.cs`, or contains a tool-name literal bound by `[McpServerTool(Name = ...)]` in `X.cs`, or is named `X*E2ETests`; zero matches -> **full run** (and the gap is pinned in `toolsWithoutFixtures`) |
| 8 | any other `clio/**/*.cs` | the transitive consumer closure over the reference graph (below) |
| 9 | any other `clio/**` file (a data asset) | the product files that name the asset by file name, then rule 8 on each |
| 10 | anything else | **full run** |

One full-run file anywhere in the diff makes the whole run full. A subset larger than
`maxSubsetFixtures` (60) becomes a full run (command-line length, and at that size the saving is gone).

#### The reference graph

The node is a **type**, not a file: 809 of 1299 files declare more than one top-level type, and while
the node was a file a narrow helper inherited the consumer set of whatever wide type shared its file.
A type owns the text from its declaration to the next top-level declaration, so nested types belong
to their outer type and no character is unattributed.

Edges, all of them widening rather than narrowing:

- **reference** - A names a type declared in B. The identifier must not be preceded by a dot, so
  `task.Result` and `options.Schema` are member access, not references to the types `Result` and
  `Schema`.
- **qualified reference** - `Clio.Common.Foo` and `global::Clio.Common.Foo`. The dot in front of
  `Foo` hides it from the rule above, and 1247 references in this tree are written that way. Only
  chains rooted in a namespace this repository declares are followed, plus the aliases of such a
  namespace (`using Contracts = Clio.Common;`); an alias of `System.*` adds nothing.
- **implementation to interface** - a consumer injects `IFoo` and never spells `Foo` out. Restricted
  to base types declared as `interface`: a base *class* here (`Command`, `BaseTool`) is a
  template-method host whose hundreds of subclasses are not interchangeable, and following it merges
  the whole tree into one component.
- **extension class to extended type** - `value.Normalize()` names neither the extension class nor
  its file, so the type it extends is the only route from the call site to it. When the receiver is
  not a type this repository declares (`this string`, `this IEnumerable<T>` - half the extension
  methods here), no consumer set bounds it and the whole suite runs.
- **registration** - `AddSingleton<IFoo, Foo>()` and the factory form
  `AddSingleton<IFoo>(sp => new Adapter(new Backend()))`, taken from the registration files only and
  read to the end of the statement rather than to the end of the line. The factory form is what
  links a type that does not implement the interface itself.

Parsing C# with regular expressions has known soft spots, so structure is never read from the raw
file. One length-preserving pass blanks every literal and comment - raw strings with any number of
leading dollars and a closing run matching the opening one, interpolated strings whose holes may
contain quotes, verbatim and interpolated-verbatim strings in either `$@` or `@$` order, ordinary
strings, char literals, line comments and block comments - replacing each character with a space and
keeping the line breaks. Declarations, base lists and the
parentheses of a registration call are parsed on that text, so a bracket, a quote, a semicolon or a
whole class written inside a comment or a literal cannot be read as syntax. *References* are still
read from the raw text: a type named only in a comment adds an edge, which widens the selection and
is the safe direction.

The guard asserts the invariant that makes this checkable: after blanking, no quote and no comment
marker is left anywhere under `clio/`. A literal form the lexer does not know leaves one behind, so
the whole class of parser gaps fails a test instead of silently narrowing a selection.

Two further shapes are handled explicitly. A base list may start on the line after the declaration,
as `clio/Common/System.cs` does. A file whose first declaration is not its least-indented one is
attributed whole to every type it declares instead of split, because a nested type indented less
than its outer type would otherwise swallow the outer body.

Entry points found in a closure select fixtures two ways: an **MCP tool file** through rule 7, and a
`[Verb("x")]` through the tool published under the same name (132 of 243 verbs are also MCP tool
names) or through a fixture that spells the verb out.

A closure with no tool and no covered verb means no fixture in this suite executes the changed code.
The file then contributes nothing, and a diff made only of such files resolves to mode **none**: no
TeamCity build is queued at all. Those files are pinned in
`clio.mcp.e2e/TestSelection/unreachable-product-files.txt` and compared by the guard, so a file
entering that set is a reviewed claim rather than a silent loss of coverage.

Two things the graph deliberately refuses to reason about, because textual matching cannot see them:
a file declaring a `[ResolvedDynamically]` type (reflection, by the repository's own marker), and a
registration file whose diff contains anything other than registration statements. Both are full runs.

A subset whose fixtures are all positively `McpE2E.NoEnvironment` also becomes mode **none**: the
NoEnvironment tier already ran on GitHub. This is checked *before* the size cap, so a large
NoEnvironment-only selection skips the build instead of becoming a full run.

The subset filter is composed as
`(FullyQualifiedName~Clio.Mcp.E2E.A|FullyQualifiedName~Clio.Mcp.E2E.B)&TestCategory!=McpE2E.NoEnvironment&<baseFilter>`.
`TestCategory` (not `Category`) is mandatory in the category part - see
`docs/knowledge/Tests/nunit-adapter-drops-large-non-category-shard-filters.md`.

Replay over the 60 most recently merged pull requests (2026-09-17), classified against today's tree:

| Selector | full | subset | none |
|---|---|---|---|
| As shipped in #1571 | 57 | 3 | 0 |
| This pull request | 48 | 6 | 6 |

The replay understates the registration rule: it passes file names only, so `BindingsModule.cs` and
`Program.cs` (12 and 6 of the 60) fall back to a full run, while the workflow always has the diff.
Whether layering clio would raise this further is measured in
[mcp-e2e-plan-split-analysis.md](mcp-e2e-plan-split-analysis.md); the answer is no.

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
- **Mode none posts no status at all.** A pull request that touches only unreachable code shows no
  `CLIO MCP e2e tests (ATF)` check. The decision log in the `select` job says which rule discarded
  each file, and the pinned list is what makes the claim reviewable.
- **Reachability is textual, not semantic.** It over-approximates edges, so a subset is wider than
  the compiler would require, and it cannot see reflection at all. A fixture that exercises tool B
  but only names tool A is selected by changes to A. Reference the tool class (`XTool.ToolName`)
  from the fixture.
- **A `#if false` region containing a type declaration would fabricate a type** and could cut the
  enclosing type's body short. One file under `clio/` uses `#if` and no declaration sits inside such
  a region; the guard's lexer-residue invariant does not cover preprocessor directives.
- **9 MCP tools have no fixture** (`toolsWithoutFixtures`). Each forces a full run, because the
  detector cannot tell which tests would show the regression.
- **Hidden per-session cost** is unaffected by the filter.

## Rollout

1. TeamCity: parameter `McpE2eTestFilter` created with the old filter as default; step args switched to
   `--filter "%McpE2eTestFilter%"` — done 2026-09-16 by a.kravchuk, default-preserving.
2. This pull request: workflows, detector, manifest, guard, knowledge records.
3. Watch the first pull-request runs: `MCP e2e NoEnvironment` duration and stability on hosted
   runners, and the TeamCity build comment showing `selection: subset` with the expected fixtures.
   First hosted run (PR #1571): 8 min 38 s, 67 failures, all one cause — the tier silently depended on
   the host's real clio settings having an active environment (`CanExecuteEnvTools`); the shared-home
   fixture now seeds a loopback placeholder when none is registered
   (`docs/knowledge/Tests/noenvironment-tier-needs-a-registered-active-environment.md`).
4. Later: promote `mcp-e2e-noenvironment` into the required gate; add `explicitMappings` for the
   domains whose fixtures are known.
