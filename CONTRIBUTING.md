# Contributing to clio

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
- `make` (macOS/Linux) — or run `dotnet` commands directly on Windows
- PowerShell (`pwsh`) — required only for full release builds and `check-pr.ps1`
- [gh CLI](https://cli.github.com/) — required only for `make check-pr`

## Quick start

```bash
git clone https://github.com/Advance-Technologies-Foundation/clio.git
cd clio

# Fast debug build (analyzers on)
make build

# Run all unit tests
make test
```

Open in VS Code and install the recommended extensions (`.vscode/extensions.json`) to get inline CLIO analyzer warnings.

## Build targets

| Command | What it does |
|---|---|
| `make build` | Debug build of `clio.csproj` — fast, analyzers on |
| `make build-release` | Full release build including cliogate packaging (requires `pwsh`) |
| `make clean` | Remove all `bin/` and `obj/` artifacts |
| `make lint` | Rebuild with analyzer diagnostics surfaced |
| `make verify-docs` | Check agent-doc wrappers are in sync with canonical docs |

## Contributing to ClioRing

ClioRing is the optional Windows desktop companion in [`clio-ring/`](clio-ring/). It is an
internal `0.x` preview delivered independently from the `clio` dotnet tool, and it may be removed
if the experiment does not prove useful. Keep contributions isolated and reversible.

Before changing Ring, read:

- [ClioRing companion architecture](project-context.md#clioring-companion-architecture-internal-preview)
  for the dependency, NativeAOT, protocol, updater-security, privacy, and deletion contract.
- [ClioRing contribution policy](AGENTS.md#clioring-contribution-policy) for the mandatory safety
  and validation checklist.

The product identity is **ClioRing** / `clio-ring`; project paths, namespaces, tests, workflow
commands, assembly identities, and solution entries must stay aligned with that identity.

> **NativeAOT is mandatory.** The shipped application is the Windows x64 NativeAOT publish, not
> the JIT build. Every Ring change must preserve a clean AOT publish with zero IL2026/IL3050
> trim/AOT warnings. Passing unit tests or successfully running from an IDE is not sufficient.

Current validation commands:

```powershell
# Ring regression suite
dotnet test clio-ring/ClioRing.Tests/ClioRing.Tests.csproj -c Release

# The shipped shape: Windows x64 NativeAOT
dotnet publish clio-ring/ClioRing.Desktop/ClioRing.Desktop.csproj `
  -c Release -r win-x64 --self-contained true -p:PublishAot=true
```

For UI workflow changes, first prove the happy path through a debugger/button harness with explicit
inputs. Then add focused regression coverage. A harness must never deploy or uninstall a real
environment without an explicit user gesture and disposable target confirmation.

## Test targets

| Command | What it does |
|---|---|
| `make test` | Full unit suite |
| `make test-unit` | Explicit unit suite (`Category=Unit`) |
| `make test-integration` | Integration tests |
| `make test-analyzers` | Roslyn analyzer tests only |
| `make test-mcp-e2e` | MCP end-to-end tests |
| `make test-module MODULE=Command` | Targeted test for one module (fast, < 10 s) |
| `make test-filter FILTER="..."` | Custom `--filter` expression |

**Smart regression testing** — before committing, run only the tests for the modules you changed.
The module-to-source mapping is in [AGENTS.md](AGENTS.md#smart-regression-testing-policy).

```bash
# Example: changed a file in clio/Command/
make test-module MODULE=Command

# Changed clio/BindingsModule.cs (DI root) → run everything
make test-unit
```

## Code style

- Roslyn analyzers run automatically on every `dotnet build` in Debug mode.
- Severity is configured in [`clio/.editorconfig`](clio/.editorconfig).
- **CLIO1xx** (architecture/runtime safety) — treat as errors.
- **CLIO2xx** (developer experience/style) — treat as warnings.
- Never leave new `CLIO*` warnings in code you added or modified.
- See [AGENTS.md — CLIO analyzer handling](AGENTS.md#clio-analyzer-handling) for the full policy.

## Documentation

Every command change requires updating three places:

| File | Purpose |
|---|---|
| `clio/help/en/<command>.txt` | CLI `-H` help text |
| `clio/docs/commands/<command>.md` | Detailed GitHub docs |
| `clio/Commands.md` | Overview index |

Use the `document-command` skill when updating command docs.

## MCP surface

If you change command behavior, also review the MCP surface:
`clio/Command/McpServer/Tools/`, `Prompts/`, `Resources/`, and E2E tests in `clio.mcp.e2e/`.
See [AGENTS.md — MCP maintenance policy](AGENTS.md#mcp-maintenance-policy) for the full checklist.

## Feature development workflow (BMAD)

Non-trivial features follow a structured pipeline before any code is written. The pipeline
is enforced via Claude Code slash commands and specialized agents.

### When to use the pipeline

| Work type | Command | Produces |
|-----------|---------|---------|
| Small fix / enhancement (< 5 stories) | `/bmad-spec` | SPEC kernel — 5-field distillation |
| New command, significant change | `/bmad` | PRD → ADR → Stories → Test plan |
| Quick status check | `/bmad-status` | Read-only view of all in-flight features |

### Pipeline phases

```
/bmad "add export-workspace command"
        │
        ▼
  [pm-agent]          → spec/prd/prd-{feature}.md
  Product manager: problem statement, FR-N requirements,
  AC-N acceptance criteria, CLI flag specification
        │
        ▼
  [architect-agent]   → spec/adr/adr-{feature}.md
  Architect: codebase analysis, design decisions, file list,
  command/service contract, test strategy
        │
        ▼
  [story-writer]      → spec/stories/story-{feature}-N.md
                        spec/sprint-status.yaml (updated)
  Stories: one PR per story, Definition of Done included
        │
        ▼
  [qa-planner]        → spec/test-plans/tp-{feature}.md
  QA: Unit/Integration/E2E test cases, regression guard,
  ready-to-implement NUnit test stubs
```

**Optional adversarial review** at any phase:
use `bmad-reviewer` agent on a PRD, ADR, or story to get
a 3-lens critique (Blind Hunter / Edge Case Hunter / Acceptance Auditor)
before moving to the next phase.

### Facilitator vs autonomous mode

By default the pipeline pauses at checkpoint gates for your approval:

```bash
/bmad "add export-workspace command"          # pauses between phases
/bmad --auto "add export-workspace command"   # runs all phases without stopping
```

Autonomous mode is opt-in and does not persist across sessions.

### Artifact locations

| Type | Path | Naming |
|------|------|--------|
| PRD / SPEC | `spec/prd/` | `prd-{name}.md` / `spec-{name}.md` |
| ADR | `spec/adr/` | `adr-{name}.md` |
| Stories | `spec/stories/` | `story-{name}-{N}.md` |
| Sprint tracker | `spec/` | `sprint-status.yaml` |
| Test plans | `spec/test-plans/` | `tp-{name}.md` |
| Review results | `spec/reviews/` | `review-{artifact}-{date}.md` |

### CI pipeline

Every push triggers GitHub Actions (`.github/workflows/`):

| Stage | Runs on | What fails the build |
|-------|---------|---------------------|
| Build + Roslyn analyzers | Every push | CLIO001-CLIO004 warnings |
| Unit tests (`Category=Unit`) | Every push | Any failing test |
| Integration tests | PR merge | Any failing test |
| SonarCloud | PR | New code smells / duplications |
| MCP E2E tests | Manual / release | Failing MCP tool contracts |

Smart regression: run only the module you changed before pushing.
See [Test targets](#test-targets) above and [AGENTS.md](AGENTS.md#smart-regression-testing-policy).

## PR workflow

Every pull request must have a GitHub issue filed before the pull request is opened.

When creating the issue:

- Keep the title and description accurate and concise. Update them if the scope changes.
- Select exactly one GitHub issue type: `Task`, `Bug`, or `Feature`.
- Add at least one relevant repository label so the issue can be found and filtered easily.
- If your GitHub permissions do not allow you to set the issue type or labels, state the requested
  type and labels in the issue and ask a maintainer to apply them before review.

When opening the pull request:

- Open the pull request as a draft. Keep it in draft while implementation, validation,
  documentation, or the project's external review process is still in progress.
- Reference at least one issue in the pull request description. Prefer a closing keyword such as
  `Fixes #123` or `Closes #123` when the pull request fully resolves the issue.
- Assign the pull request to yourself. If your GitHub permissions do not allow this, ask a
  maintainer to assign it to you before review.
- Keep the pull request scope aligned with the referenced issue. Update the issue before expanding
  or materially changing that scope.

When the work is complete:

- Confirm the implementation, required tests, documentation, and external review are complete and
  that no known blocking feedback remains.
- Mark the pull request as ready for review.
- After the pull request is ready, a contributor or agent with permission may enable auto-merge
  when appropriate. Do not enable auto-merge while the pull request is a draft or before the work
  is ready.

```bash
# Check PR status and CI results
make check-pr

# Release-readiness score
make check-pr-release
```

All PRs run through SonarCloud. Resolve all new issues before requesting review.

## Further reading

- [AGENTS.md](AGENTS.md) — full engineering policies (DI, testing, docs, MCP, analyzers)
- [docs/DevFlowReadme.md](docs/DevFlowReadme.md) — end-to-end developer workflow with Creatio environments
- [spec/create-dev-env-4-mac.md](spec/create-dev-env-4-mac.md) — macOS environment setup
- [.codex/workspace-diary.md](.codex/workspace-diary.md) — engineering decision log
