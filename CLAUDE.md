# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Git commits require explicit confirmation

Never run `git commit` in this repository until the user explicitly confirms in chat, even if all
tests pass and the change looks complete. Finishing an implementation is not confirmation. Ask,
then wait for a clear yes before committing.

## Authoritative instruction files (read these first)

@AGENTS.md is the single source of truth for engineering policy (DI, testing, docs, MCP,
analyzers, feature toggles, ClioRing, BMAD). It is imported above — follow it exactly.

Two more files carry rules that cannot be derived from the code:

- `project-context.md` — architecture rules, test tiers, code style, "what NOT to do".
- `CONTRIBUTING.md` — build/test targets, PR workflow, CI stages.

The sections below are a fast orientation for those files, not a replacement. When they and this
file disagree, `AGENTS.md` / `project-context.md` win.

## What clio is

A cross-platform CLI (`dotnet tool`) that integrates the **Creatio** low-code platform with
development and CI/CD tooling — package management, environment registration, schema/model
generation, workspace scaffolding, and an **MCP server** exposing commands to AI agents.
`ClioRing` (`clio-ring/`) is a separate, optional, feature-gated Windows desktop companion.

## Build / run / test

Targets **net8.0** (and **net10.0** when SDK ≥ 10 is installed) — commands multi-target; pass
`--framework net8.0` when you need one. `make` wraps the raw `dotnet` calls (macOS/Linux; on
Windows either run `make` if available or the `dotnet` form directly).

```bash
make build                        # dotnet build clio/clio.csproj -c Debug (analyzers on)
make test                         # full unit suite (Category=Unit)
make test-module MODULE=Command   # ONLY the changed module — do this before committing (see below)
make test-analyzers               # Roslyn analyzer (CLIO*) tests
make lint                         # rebuild surfacing analyzer diagnostics
```

Raw equivalents / when `make` is unavailable:

```bash
# Build (use --no-incremental after DI/binary-shape changes — stale incremental builds silently
# keep old compiled types and cause baffling runtime failures)
dotnet build clio/clio.csproj -c Debug --no-incremental

# Run the CLI from source without installing the global tool
dotnet run --project clio/clio.csproj --framework net8.0 -- <verb> [args]   # e.g. -- get-info -e <env>

# Single test / one module (fast — targeted filters keep feedback < 10 s)
dotnet test clio.tests/clio.tests.csproj --filter "FullyQualifiedName~MyTestName" --no-build
dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=Command" --no-build
```

**Smart regression testing (mandatory before commit):** run only the tests for the modules you
changed via `Module=<trait>`; run the full suite only when shared infra changed
(`BindingsModule.cs`, `Program.cs`, `clio/Common/`, test infra, or > 3 modules touched). The
full module→source map is in `AGENTS.md` → *Smart regression testing policy*.

Test stack: **NUnit 4** + **FluentAssertions 7** + **NSubstitute 5**. Command tests inherit
`BaseCommandTests<TOptions>`. Tiers are `[Category("Unit"|"Integration"|"E2E")]` — never
`"UnitTests"`. Naming: `Method_ShouldExpectedBehavior_WhenCondition`, every assertion has a
`because`, every test a `[Description]`.

## Architecture (big picture)

A command is defined in ~4 coordinated places — miss one and it won't run:

1. **Options class** — a POCO with `[Verb("kebab-name", Aliases=…)]` + `[Option("kebab-flag")]`
   properties (all long names **must** be kebab-case — enforced by analyzer **CLIO001**).
2. **Command class** — derives from `Command<TOptions>` (`clio/Command/Command.cs`), takes
   collaborators via **constructor injection**, and calls **services** directly.
   **MediatR was removed** — do not add `Request`/`Handler` pairs.
3. **DI registration** — wired in `clio/BindingsModule.cs` (behavior classes are resolved from
   DI, not `new`ed — analyzer **CLIO001**; dead registrations are flagged by **CLIO005**).
4. **Verb wiring** — the options type is listed in `Program.CommandOption[]` and dispatched
   through `Program.ExecuteCommandWithOption` (`clio/Program.cs`).

Cross-cutting rules that shape almost every change:

- **Creatio HTTP access goes only through `IApplicationClient` / `CreatioClient`** — never raw
  `HttpClient`.
- **`cliogate/`** is a privileged Creatio package (installed into an environment) exposing
  WCF/REST endpoints under `/rest/CreatioApiGateway/<Method>` for operations DataService can't do.
  Adding an endpoint touches `cliogate/Files/cs/CreatioApiGateway.cs` **and**
  `ServiceUrlBuilder.KnownRoute(s)` — see the *ClioGate integration* section of `AGENTS.md`.
- **MCP surface** (`clio/Command/McpServer/` → `Tools/`, `Prompts/`, `Resources/`) is a *second*
  contract over the same commands and must be reviewed whenever a command changes (mandatory —
  `AGENTS.md` → *MCP maintenance policy*; e2e tests in `clio.mcp.e2e/`). MCP is also consumed by
  ClioRing, which adds a compatibility gate.
- **Feature toggles** hide experimental commands: `[FeatureToggle("key")]` on the *options* class
  **and** on the matching `[McpServerToolType]`/etc. class (two separate surfaces).

Filesystem access uses the project's filesystem abstraction (so tests need no real FS).
Roslyn analyzers (`Clio.Analyzers/`, IDs `CLIO001`–`CLIO005`) run on every Debug build; CLIO1xx
are build errors in CI — never leave new `CLIO*` warnings in code you touched.

### Project layout

| Project | Role |
|---|---|
| `clio/` | The CLI tool + MCP server (all commands, services, model/workspace generation) |
| `cliogate/` | Privileged Creatio package deployed into an environment (see above) |
| `Clio.Analyzers/` | Roslyn analyzers enforcing CLIO001–CLIO005 |
| `clio.tests/` | Unit + Integration tests (`{Feature}Tests.cs`) |
| `clio.mcp.e2e/` | MCP end-to-end tests (not in CI yet — flag this in test plans) |
| `cliogate.tests/`, `Clio.Analyzers.Tests/` | Test projects for the above |
| `clio-ring/` | Optional Windows desktop companion (independent `0.x`, NativeAOT — isolated boundary, see `project-context.md`) |

Solution: `clio.slnx` (root) — ClioRing is the `/clio-ring/` group within it.

## Documentation & feature workflow

- Changing any command's behavior/options requires updating **three** doc files:
  `clio/help/en/<command>.txt`, `clio/docs/commands/<command>.md`, `clio/Commands.md`
  (use the `document-command` skill). Resolve aliases to the canonical verb for filenames.
- Non-trivial features go through the **BMAD** pipeline (`/bmad`) before code:
  PRD → ADR → stories → test plan under `spec/`, tracked in `spec/sprint-status.yaml`.
- Keep the engineering diary current: append to `.codex/workspace-diary.md` after non-trivial work.
