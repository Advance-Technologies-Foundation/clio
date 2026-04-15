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

## PR workflow

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
