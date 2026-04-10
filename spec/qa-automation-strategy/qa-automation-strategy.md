# QA Automation Strategy: Clio

## Current State Analysis

### What we have

| Metric | Value |
|--------|-------|
| Unit tests (clio.tests) | ~2,074 |
| MCP E2E tests (clio.mcp.e2e) | ~129 |
| Test framework | NUnit 4.5.1 + FluentAssertions 7.2.0 + NSubstitute 5.3.0 |
| Coverage tool | Coverlet (OpenCover format) |
| CI | GitHub Actions on self-hosted runner (Windows/PowerShell) |
| Coverage thresholds | None enforced |
| Static analysis | SonarQube (commented out), Roslyn analyzers (CLIO001-004) |

### Key problems

1. **All 2,074 tests run on every push/PR** — no selective execution, slow feedback
2. **Single monolithic CI job** — no parallelism, no separation of fast/slow tests
3. **Category inconsistency** — mix of `Unit`, `UnitTests`, `CommandTests` without clear strategy
4. **No coverage gates** — coverage reports are generated but don't block merges
5. **No impact-based regression** — changing a single command reruns the entire suite
6. **MCP E2E tests not in CI** — only clio.tests runs in build.yml
7. **No local quick-check workflow** — developers must run the full suite or manually pick tests
8. **SonarQube disabled** — static analysis not enforced

---

## Target Architecture

```
Developer push
     │
     ▼
┌─────────────────────────────────────────────────┐
│  GitHub Actions Pipeline                        │
│                                                 │
│  ┌───────────┐  ┌────────────┐  ┌───────────┐  │
│  │ Unit      │  │ Analyzers  │  │ Coverage  │  │
│  │ (filtered │  │ (Roslyn +  │  │ Gate      │  │
│  │ by impact)│  │ SonarQube) │  │ (min %)   │  │
│  └─────┬─────┘  └─────┬──────┘  └─────┬─────┘  │
│        │              │              │          │
│        ▼              ▼              ▼          │
│  ┌───────────┐  ┌────────────┐                  │
│  │Integration│  │ MCP E2E    │  (on demand /    │
│  │ Tests     │  │ Tests      │   release only)  │
│  └───────────┘  └────────────┘                  │
└─────────────────────────────────────────────────┘
```

**Key principle**: fast feedback on every push, deep validation on release/merge.

---

## Phases

### Phase 1: Test Taxonomy & Category Cleanup

**Goal**: Establish a consistent test classification system that enables selective execution.

**Why first**: Every subsequent phase depends on being able to filter tests by category/trait.

#### 1.1 Standardize categories

Define three tiers:

| Category | Meaning | Expected runtime | Runs on |
|----------|---------|-----------------|---------|
| `Unit` | Isolated, no I/O, no external deps | <5 sec total | Every push |
| `Integration` | File system, DB, IIS, K8s stubs | <30 sec total | PR merge |
| `E2E` | Real clio process, MCP protocol | <2 min total | Release / manual |

#### 1.2 Cleanup actions

- Replace all `[Category("UnitTests")]` with `[Category("Unit")]` (20 occurrences)
- Audit tests that do real file I/O (e.g., `CreatioPkgTests`, `NewPkgCommand.Tests`) — reclassify as `Integration`
- Add `[Category("Unit")]` to test fixtures that are missing categories entirely
- Document the taxonomy in AGENTS.md test style policy

#### 1.3 Add module traits (NUnit `[Property]`)

Map tests to source modules for impact-based filtering:

```csharp
[Property("Module", "PackageCommand")]
[Category("Unit")]
public class PushPkgCommandTests : BaseCommandTests<PushPkgOptions>
```

Module names match source directory structure:
- `ALMCommand`, `ApplicationCommand`, `PackageCommand`, `CreatioInstallCommand`
- `McpServer`, `TIDE`, `Update`, `EntitySchemaDesigner`, `ProcessModel`
- `Common/Database`, `Common/IIS`, `Common/Kubernetes`, `Common/DeploymentStrategies`
- `Workspace`, `Utilities`, `Infrastructure`

**Deliverables**:
- [ ] Category cleanup PR
- [ ] Module property annotations on all test fixtures
- [ ] Updated AGENTS.md test policy

**Estimated effort**: 1-2 days

---

### Phase 2: CI Pipeline — Split & Parallelize

**Goal**: Fast unit feedback (<2 min), deeper checks in parallel.

#### 2.1 Split build.yml into parallel jobs

```yaml
jobs:
  unit-tests:
    name: Unit Tests
    runs-on: self-hosted
    steps:
      - uses: actions/checkout@v5
      - run: |
          dotnet test clio.tests/clio.tests.csproj \
            --filter "Category=Unit" \
            --collect:"XPlat Code Coverage" \
            -p:RunAnalyzers=false

  integration-tests:
    name: Integration Tests
    runs-on: self-hosted
    needs: unit-tests
    if: github.event_name == 'pull_request'
    steps:
      - uses: actions/checkout@v5
      - run: |
          dotnet test clio.tests/clio.tests.csproj \
            --filter "Category=Integration"

  analyzer-tests:
    name: Analyzer Tests
    runs-on: self-hosted
    steps:
      - uses: actions/checkout@v5
      - run: |
          dotnet test Clio.Analyzers.Tests/Clio.Analyzers.Tests.csproj

  mcp-e2e:
    name: MCP E2E Tests
    runs-on: self-hosted
    needs: unit-tests
    if: github.event_name == 'release' || contains(github.event.pull_request.labels.*.name, 'run-e2e')
    steps:
      - uses: actions/checkout@v5
      - run: |
          dotnet test clio.mcp.e2e/clio.mcp.e2e.csproj
```

#### 2.2 Add path-based triggers with `dorny/paths-filter`

Only run test suites when relevant code changes:

```yaml
  changes:
    runs-on: self-hosted
    outputs:
      commands: ${{ steps.filter.outputs.commands }}
      common: ${{ steps.filter.outputs.common }}
      mcp: ${{ steps.filter.outputs.mcp }}
      analyzers: ${{ steps.filter.outputs.analyzers }}
    steps:
      - uses: dorny/paths-filter@v3
        id: filter
        with:
          filters: |
            commands:
              - 'clio/Command/**'
              - 'clio/Common/**'
              - 'clio/Program.cs'
              - 'clio/BindingsModule.cs'
            mcp:
              - 'clio/Command/McpServer/**'
            analyzers:
              - 'Clio.Analyzers/**'
            common:
              - 'clio/Common/**'
              - 'clio/Requests/**'
              - 'clio/Environment/**'
```

#### 2.3 NUnit parallel execution

Add to `clio.tests.csproj`:
```xml
<PackageReference Include="NUnit.Analyzers" />
```

Add assembly-level parallelism in `AssemblyInfo.cs`:
```csharp
[assembly: Parallelizable(ParallelScope.Fixtures)]
[assembly: LevelOfParallelism(4)]
```

**Deliverables**:
- [ ] Refactored build.yml with parallel jobs
- [ ] Path filter configuration
- [ ] NUnit parallel execution enabled
- [ ] Unit test job completes in <2 minutes

**Estimated effort**: 2-3 days

---

### Phase 3: Coverage Gates & Quality Enforcement

**Goal**: Prevent coverage regression and enforce code quality standards.

#### 3.1 Coverage threshold in CI

Add to `dotnet test` call or use coverlet MSBuild properties:

```xml
<!-- In clio.tests.csproj or Directory.Build.props -->
<PropertyGroup Condition="'$(CI)' == 'true'">
  <Threshold>60</Threshold>
  <ThresholdType>line</ThresholdType>
  <ThresholdStat>total</ThresholdStat>
</PropertyGroup>
```

Start conservative (60%) and ratchet up as coverage improves.

#### 3.2 PR coverage diff comment

Use `danielpalme/ReportGenerator-GitHub-Action` or `irongut/CodeCoverageSummary` to post coverage diff as a PR comment:

```yaml
  - uses: irongut/CodeCoverageSummary@v1.3.0
    with:
      filename: TestResults/coverage.opencover.xml
      format: markdown
      output: both
      thresholds: '60 80'
  - uses: marocchino/sticky-pull-request-comment@v2
    with:
      path: code-coverage-results.md
```

#### 3.3 Re-enable SonarQube (or switch to SonarCloud)

The existing SonarQube configuration is commented out in build.yml. Options:
- **SonarCloud** (free for open-source): no self-hosted server needed
- **Re-enable self-hosted SonarQube**: if internal server is available

Add as a parallel job that doesn't block the pipeline but reports quality gate status.

#### 3.4 Enforce Roslyn analyzers in CI

Currently `-p:RunAnalyzers=false` in build.yml. Remove this flag and treat CLIO001-004 warnings as errors in CI:

```xml
<!-- In clio.csproj for CI builds -->
<PropertyGroup Condition="'$(CI)' == 'true'">
  <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  <WarningsAsErrors>CLIO001;CLIO002;CLIO003;CLIO004</WarningsAsErrors>
</PropertyGroup>
```

> **Note**: This requires first fixing the ~40 existing CLIO* warnings, or using a baseline approach.

**Deliverables**:
- [ ] Coverage threshold gate (60% line, ratchet up)
- [ ] PR coverage diff comments
- [ ] SonarCloud or SonarQube re-enabled
- [ ] Roslyn analyzer enforcement plan (fix existing → enable gate)

**Estimated effort**: 2-3 days

---

### Phase 4: Smart Regression — Impact-Based Test Selection

**Goal**: When you change `PackageCommand/`, only run tests tagged with `Module=PackageCommand` + `Module=Common` (shared dependencies).

#### 4.1 Build a test impact matrix

Create a mapping file `.github/test-impact-matrix.yml`:

```yaml
# Source path → test module traits to run
impact:
  - paths: ["clio/Command/PackageCommand/**"]
    modules: ["PackageCommand"]
    always_run: ["Common"]

  - paths: ["clio/Command/ApplicationCommand/**"]
    modules: ["ApplicationCommand"]
    always_run: ["Common"]

  - paths: ["clio/Command/McpServer/**"]
    modules: ["McpServer"]
    always_run: ["Common"]
    also_run_e2e: true

  - paths: ["clio/Common/**", "clio/BindingsModule.cs", "clio/Program.cs"]
    modules: ["ALL"]  # Core changes → full regression

  - paths: ["clio/Command/Update/**"]
    modules: ["Update"]
    always_run: ["Common"]

  - paths: ["Clio.Analyzers/**"]
    modules: []  # Only run analyzer tests
    run_analyzer_tests: true

# Fallback: if no path matches, run all unit tests
fallback: "ALL"
```

#### 4.2 CI workflow with dynamic filter

```yaml
  smart-test:
    runs-on: self-hosted
    steps:
      - uses: actions/checkout@v5
      - id: impact
        uses: ./.github/actions/test-impact
        # Custom action that reads matrix + changed files → outputs NUnit filter
      - run: |
          dotnet test clio.tests/clio.tests.csproj \
            --filter "${{ steps.impact.outputs.filter }}" \
            --collect:"XPlat Code Coverage"
```

#### 4.3 Dependency graph awareness

For deeper impact analysis, consider `dotnet-affected` tool:

```bash
dotnet tool install dotnet-affected
dotnet affected --from origin/master
```

This identifies which .csproj projects are affected by changes, enabling project-level filtering.

**Deliverables**:
- [ ] Test impact matrix definition
- [ ] Custom GitHub Action for filter generation
- [ ] Integration with CI pipeline
- [ ] Documentation of module boundaries

**Estimated effort**: 3-5 days

---

### Phase 5: Local Developer Experience

**Goal**: Developers get fast, relevant feedback before pushing.

#### 5.1 `dotnet test` filter aliases

Create a `.cliotest` script or Makefile:

```makefile
# Makefile targets for local testing
test-unit:
	dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit" --no-build

test-module:  # Usage: make test-module MODULE=PackageCommand
	dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=$(MODULE)" --no-build

test-integration:
	dotnet test clio.tests/clio.tests.csproj --filter "Category=Integration" --no-build

test-mcp:
	dotnet test clio.mcp.e2e/clio.mcp.e2e.csproj

test-analyzers:
	dotnet test Clio.Analyzers.Tests/Clio.Analyzers.Tests.csproj

test-all:
	dotnet test clio.tests/clio.tests.csproj --collect:"XPlat Code Coverage"

test-changed:  # Smart: only test modules affected by uncommitted changes
	@./scripts/test-changed.sh
```

#### 5.2 Git pre-push hook (optional, opt-in)

```bash
#!/bin/bash
# .githooks/pre-push — runs affected unit tests before push
changed_files=$(git diff --name-only origin/master...HEAD)
# Parse changed_files → determine modules → run filtered tests
dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&(Module=...)" --no-build
```

Install via:
```bash
git config core.hooksPath .githooks
```

#### 5.3 IDE integration

For JetBrains Rider / Visual Studio:
- NUnit categories appear as test traits → can filter in Test Explorer
- Module properties appear as test properties → can group/filter
- Create `.runsettings` profiles for common scenarios

**Deliverables**:
- [ ] Makefile / PowerShell script with test targets
- [ ] `test-changed.sh` / `test-changed.ps1` scripts
- [ ] Optional pre-push hook
- [ ] .runsettings profiles for IDE use

**Estimated effort**: 1-2 days

---

### Phase 6: Advanced — Mutation Testing & Flaky Detection

**Goal**: Ensure tests actually catch bugs, not just execute code.

#### 6.1 Mutation testing with Stryker.NET

```bash
dotnet tool install dotnet-stryker
dotnet stryker --project clio.csproj --test-project clio.tests/clio.tests.csproj
```

Run periodically (weekly CI schedule or manual trigger), not on every push:

```yaml
  mutation-testing:
    if: github.event.schedule == '0 2 * * 1'  # Monday 2 AM
    runs-on: self-hosted
    steps:
      - run: dotnet stryker --reporters "['html', 'dashboard']"
```

Focus on critical modules first: `PackageCommand`, `ApplicationCommand`, `Common/Database`.

#### 6.2 Flaky test detection

Track test stability over time:
- Use NUnit `[Retry(3)]` attribute sparingly for known-flaky tests
- Aggregate test results across CI runs to identify patterns
- Quarantine flaky tests into a `[Category("Quarantine")]` that runs separately

#### 6.3 Contract testing for Creatio API

For commands that interact with Creatio endpoints, consider:
- Recording HTTP interactions with `WireMock.Net` for stable replay
- Contract schemas for API request/response validation

**Deliverables**:
- [ ] Stryker.NET weekly CI job
- [ ] Flaky test quarantine process
- [ ] WireMock integration for HTTP-dependent tests (optional)

**Estimated effort**: 3-5 days

---

## Recommended Implementation Order

```
Phase 1 ──► Phase 2 ──► Phase 3
  (1-2d)      (2-3d)      (2-3d)
                              │
              Phase 5 ◄───────┤
               (1-2d)        │
                              ▼
                          Phase 4 ──► Phase 6
                           (3-5d)      (3-5d)
```

- **Phase 1** is the prerequisite for everything — consistent categories enable filtering
- **Phase 2** gives immediate CI speed improvements
- **Phase 3** adds quality gates — prevents regression
- **Phase 5** can run in parallel with Phase 3 — improves local DX
- **Phase 4** is the "smart regression" — needs Phase 1 module traits
- **Phase 6** is ongoing improvement

## Quick Wins (can do today)

1. Add `--filter "Category!=Integration"` to build.yml test step — immediately skip slow tests on push
2. Add NUnit parallel execution attribute — free speed improvement
3. Add coverage threshold to CI — prevent regression without any test changes

## Metrics to Track

| Metric | Current | Phase 2 Target | Phase 4 Target |
|--------|---------|-----------------|-----------------|
| CI unit test time | ~? min (all tests) | <2 min | <1 min (filtered) |
| Feedback on push | Full suite | Unit only | Affected modules only |
| Coverage gate | None | 60% line | 70% line |
| Tests with categories | ~400/2074 | 2074/2074 | 2074/2074 |
| MCP E2E in CI | No | On release | On release + MCP changes |
