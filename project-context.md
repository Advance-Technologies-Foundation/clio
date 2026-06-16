# Clio Project Context

> This file is loaded by every BMAD agent at activation. It contains Clio-specific rules
> that cannot be derived from reading the code alone. Keep it current.

---

## Project Identity

| Field | Value |
|-------|-------|
| Project | Clio — CLI tool for Creatio platform integration |
| Language | C# 12 / .NET 10 |
| Type | Global dotnet tool (`dotnet tool install clio -g`) |
| Repo | `Advance-Technologies-Foundation/clio` |
| Main solution | `MainSolution.slnx` |

---

## Architecture Rules (non-obvious)

### Command pattern
- **Do NOT use MediatR.** MediatR has been fully removed from the codebase — do not
  add new `Request` + `Handler` pairs and do not route new commands through MediatR.
- New CLI commands derive from `Command<TOptions>` (see `clio/Command/Command.cs`),
  take their dependencies via **constructor injection**, and call collaborating
  **services** directly. Existing command families such as the skill commands
  (`clio/Command/SkillCommands.cs`) are the reference pattern.
- Commands are registered via DI in `clio/BindingsModule.cs` and wired as verbs in
  `clio/Program.cs` — never forget to register.
- `IApplicationClient` / `CreatioClient` is the ONLY way to talk to Creatio HTTP API — never use raw `HttpClient`

### CLI flag naming — HARD RULE
- **All CLI option names must be kebab-case**: `--package-name`, not `--packageName` or `--PackageName`
- This is enforced by Roslyn analyzer CLIO001; violations fail the build
- ~65 existing violations are tracked in `spec/cli-naming/camelcase-violations.md` — do not add new ones
- When renaming a flag, add an alias for the old name (breaking change policy)

### Roslyn analyzers (CLIO001-CLIO004)
- CLIO001: CLI option naming (kebab-case)
- CLIO002-CLIO004: see `Clio.Analyzers/` for details
- All analyzer warnings are treated as errors in CI

### MCP server
- `clio.mcp.server/` exposes CLI commands over Model Context Protocol
- When adding a CLI command, consider whether it should also be an MCP tool
- MCP tool names follow the same kebab-case convention
- MCP capability map: `docs/McpCapabilityMap.md`

---

## Testing Rules (critical)

### Test categories — THREE TIERS ONLY

| Category | Attribute | Meaning | Runs on |
|----------|-----------|---------|---------|
| `Unit` | `[Category("Unit")]` | No I/O, no external deps, NSubstitute mocks only | Every push |
| `Integration` | `[Category("Integration")]` | File system, DB, IIS, K8s stubs | PR merge |
| `E2E` | `[Category("E2E")]` | Real clio process, MCP protocol, real Creatio | Release / manual |

**NEVER use**: `[Category("UnitTests")]`, `[Category("CommandTests")]`, or any other string.
These are legacy violations — do not replicate them.

### Test naming — MANDATORY format
```
MethodName_ShouldExpectedBehavior_WhenCondition
```
Examples: `Execute_ShouldReturnPackageList_WhenEnvironmentIsValid`
Never: `Test1`, `TestMethod`, `ShouldWork`, `MyTest`.

### Test framework
- **NUnit 4.5.1** — test runner
- **FluentAssertions 7.2.0** — assertions (`result.Should().Be(expected)`)
- **NSubstitute 5.3.0** — mocking (`Substitute.For<IInterface>()`)
- Coverage: Coverlet (OpenCover format), no gates enforced yet

### Test file locations
- Unit + Integration: `clio.tests/{FeatureName}Tests.cs`
- MCP E2E: `clio.mcp.e2e/`
- MCP E2E tests are NOT in CI yet — always flag this in test plans

---

## Code Style

- StyleCop rules: see `stylecop.json`
- No hardcoded strings for user-facing messages — use constants or resource references
- Error messages must be user-friendly: "Error: package 'Foo' not found" not stack traces
- All public APIs need XML doc comments if they are part of the public interface

---

## CI/CD

- CI: GitHub Actions, self-hosted Windows runner
- Workflows: `.github/workflows/`
- Build script: `build.ps1` (PowerShell) / `build.cmd`
- PR check: `check-pr.sh` / `check-pr.ps1`
- Release: `create-release.ps1` / `create-release.sh`
- NuGet package version: managed in `Directory.Packages.props`

---

## Key Files for Agents

| Purpose | Path |
|---------|------|
| QA strategy | `spec/qa-automation-strategy/qa-automation-strategy.md` |
| CLI naming violations | `spec/cli-naming/camelcase-violations.md` |
| MCP capability map | `docs/McpCapabilityMap.md` |
| Contributing guide | `CONTRIBUTING.md` |
| Release notes guide | `RELEASE.md` |
| BMAD sprint status | `spec/sprint-status.yaml` |

---

## What NOT to do

- Do not use `HttpClient` directly — use `IApplicationClient`
- Do not add `[Category("UnitTests")]` — use `[Category("Unit")]`
- Do not add camelCase CLI flags — always kebab-case
- Do not create tests without a category attribute — uncategorized tests run on every push unexpectedly
- Do not skip Roslyn analyzer fixes — they are build errors in CI
- Do not write bare `catch (Exception)` — handle specifically or rethrow
