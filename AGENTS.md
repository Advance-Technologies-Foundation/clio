# Authoritative agent instructions

`AGENTS.md` is the single authoritative instruction file for all coding agents working in this repository.
Read this file and `project-context.md` before doing any work. More specific nested `AGENTS.md` files, when present,
may add rules for their subtree but must not duplicate or contradict this file.

# ClioGate integration

ClioGate is a Creatio package (in `cliogate/`) that acts as a privileged backend service.
It exposes WCF/REST endpoints that bypass DataService ESQ permission checks by using
`Terrasoft.Core.DB.Select` / `Update` directly against the database engine.

## When to use ClioGate instead of DataService

Use ClioGate endpoints whenever:
- The operation touches schema objects with restricted NUI security (`SysPackage`, `SysSchema`, etc.).
- The existing DataService ESQ path fails with `SecurityException: Current user does not have permissions for the "X" object`.
- The operation needs to run with elevated permissions that can't be granted at the schema level.

## ClioGate URL pattern

All ClioGate methods are served at:
```
/rest/CreatioApiGateway/<MethodName>
```
**Never** use `/rest/CreatioApiGatewayService/…` — that path does not exist and will return an HTML error page.

Existing examples in `ServiceUrlBuilder.KnownRoutes`:
```csharp
{KnownRoute.DownloadPackageDllFile, "/rest/CreatioApiGateway/DownloadFile"},
{KnownRoute.SendEventToUi,          "/rest/CreatioApiGateway/SendEventToUI"},
{KnownRoute.UnlockPackages,         "/rest/CreatioApiGateway/UnlockPackages"},
{KnownRoute.LockPackages,           "/rest/CreatioApiGateway/LockPackages"},
```

`ServiceUrlBuilder.Build(KnownRoute)` automatically prepends `0/` for `.NET Framework` environments
(`IsNetCore = false`), so always register the raw `/rest/…` path in `KnownRoutes`.

## Adding a new ClioGate endpoint

1. Add a method to `cliogate/Files/cs/CreatioApiGateway.cs` with `[WebInvoke]` and `CheckCanManageSolution()` as the first call.
2. Add a new enum value to `ServiceUrlBuilder.KnownRoute` (continue the numeric sequence — do not reuse gaps or existing IDs).
3. Add the route to `ServiceUrlBuilder.KnownRoutes` using the `/rest/CreatioApiGateway/<MethodName>` pattern.
4. Call via `IApplicationClient.ExecutePostRequest(url, jsonBody)` — check the returned bool/string for success.

## Security: CheckCanManageSolution

Every public endpoint in `CreatioApiGateway.cs` must call `CheckCanManageSolution()` as its first line.
Before implementing a new security check, grep for `CheckCanManageSolution()` near the target method — it may already be present.

## Building ClioGate on macOS (without PowerShell)

`build.ps1` is the canonical build script but requires `pwsh`. On macOS without `pwsh`:

```bash
# 1. Build net472 assembly (for .NET Framework / IsNetCore=false environments)
dotnet build cliogate/cliogate.csproj -c Release -f net472 \
  -p:TargetFrameworks=net472 --no-incremental -p:AssemblyName=cliogate \
  --source ~/.nuget/packages
# Output: cliogate/Files/Bin/cliogate.dll

# 2. Copy ATF.Repository.dll (check version in cliogate/obj/project.assets.json)
cp ~/.nuget/packages/atf.repository/<version>/lib/netstandard2.0/ATF.Repository.dll \
  cliogate/Files/Bin/

# 3. Bump version
dotnet clio/bin/Release/net10.0/clio.dll set-pkg-version ./cliogate --PackageVersion X.Y.Z.W

# 4. Compress
dotnet clio/bin/Release/net10.0/clio.dll compress ./cliogate -d ./clio/cliogate/cliogate.gz

# 5. Clean up build artifacts from Files/Bin (build.ps1 does this too)
rm -f cliogate/Files/Bin/cliogate.dll cliogate/Files/Bin/cliogate.pdb \
      cliogate/Files/Bin/ATF.Repository.dll
```

Note: The netstandard2.0 target requires the private Nexus feed (may fail with 403 on macOS without VPN).
For `.NET Framework` environments this is not needed — only the net472 assembly is loaded.

## Deploying and verifying ClioGate on an environment

```bash
# Deploy
dotnet run --project clio/clio.csproj --framework net8.0 -- push-pkg ./clio/cliogate/cliogate.gz -e <env>

# Verify version installed
dotnet run --project clio/clio.csproj --framework net8.0 -- list-packages -e <env> | grep cliogate

# Quick connectivity check
dotnet run --project clio/clio.csproj --framework net8.0 -- get-info -e <env>
```

The install command is `push-pkg`, **not** `push-package` (that verb does not exist).

## Common clio command names (easily confused)

| Intent | Correct verb |
|---|---|
| Push a package .gz to environment | `push-pkg` |
| List registered environments | `list-environments` |
| Show system info / test connection | `get-info` |
| Lock a package | `lock-package` |
| Unlock a package | `unlock-package` |

# CLI parameter naming convention

All CLI option long names defined with `[Option("...", ...)]` **must use kebab-case** (e.g. `--restart-environment`, `--db-server-uri`).

**Never use camelCase or PascalCase** (e.g. `--restartEnvironment`, `--SysAdminUnitName`) for new or modified option names.

When renaming an existing camelCase option to kebab-case:
1. Change the main `[Option]` string to kebab-case.
2. Add a **hidden** alias property that delegates to the main property (for backward compatibility):
```csharp
[Option("restart-environment", Required = false, HelpText = "...")]
public bool RestartEnvironment { get; set; }

[Option("restartEnvironment", Required = false, Hidden = true, HelpText = "Alias for --restart-environment")]
public bool RestartEnvironmentAlias {
    get => RestartEnvironment;
    set { if (value) RestartEnvironment = value; }
}
```
3. Update all docs (`clio/docs/commands/*.md`, `clio/help/en/*.txt`) to use the new kebab-case form.

# Documentation structure for commands

- `clio\Commands.md` - Overview of all commands (displayed when user types `clio help`)
- `clio\help\en\*.txt` - Command-line help (displayed when user types `clio <command> -H`)
- `clio\docs\commands\*.md` - Detailed markdown documentation (displayed on GitHub)

# Feature documentation naming convention

To keep feature docs consistent:
- Each feature lives under `spec/<feature-name>/`.
- Files inside must be named `<feature-name>-<logical-block>.md` (examples: `call-service-delete-method-spec.md`, `call-service-delete-method-plan.md`, `call-service-delete-method-qa.md`).
- Use lowercase with hyphens for `<feature-name>` and `<logical-block>`; avoid spaces and camel case.
- Add new logical blocks as separate files rather than expanding one huge doc.

If adding a new feature, create the folder and follow this naming format for all Markdown files.

# Command documentation maintenance policy

When changing any command behavior or command-related classes, always review command documentation and update it if needed.

# MCP maintenance policy

When changing any command behavior or command-related classes, always review the MCP surface for that command in the same way documentation is reviewed.

## Trigger conditions for mandatory MCP review

Review MCP artifacts whenever any of the following is changed:
- Command options classes (for example classes with `[Verb]`, `[Option]`, `[Value]` attributes)
- Command handlers/execution logic (for example `*Command`, validators, mapping in `Program.cs`)
- Authentication/requirements/dependencies for command execution
- Workspace ownership/validation behavior
- Command output, progress reporting, or destructive behavior

## Required MCP targets

For every touched command, verify and update all relevant files:
- `clio\clio\Command\McpServer\Tools\*.cs`
- `clio\clio\Command\McpServer\Prompts\*.cs`
- `clio\clio\Command\McpServer\Resources\*.cs`
- `clio.tests\Command\McpServer\*.cs`
- `clio.mcp.e2e\*.cs`

## Update rules

- If the command already has an MCP tool, keep the tool arguments, descriptions, destructive flags, and execution path aligned with the current command behavior.
- If the command is environment-sensitive, use the MCP `BaseTool` environment-aware execution pattern instead of executing the startup-time injected command directly.
- If the command has an MCP prompt, keep the prompt guidance aligned with the current tool contract.
- Always add or update MCP end-to-end coverage in `clio.mcp.e2e` for every new or changed MCP tool. This is mandatory even when the user does not mention E2E coverage explicitly.
- Treat unit tests in `clio.tests` as necessary but insufficient for MCP tool changes; mapping-only coverage does not complete the task.
- If no MCP artifact exists for a touched command, explicitly check whether one should be added and mention the result in the change summary.
- If MCP artifacts are still accurate after review, explicitly state "MCP reviewed, no update required" in the change summary/PR description.
- If you changed a command's rule or behavior, review the matching guidance article in `GuidanceCatalog` AND its trigger line in the relevant tool `[Description]`. `McpServerInstructions.cs` carries only a mandatory pointer to the `routing` guide (`get-guidance name=routing`); the routing table itself (guide **names** only) lives in `Resources\RoutingGuidanceResource.cs`, and detailed rules live once in each guide (`Resources\*GuidanceResource.cs`) — never duplicate guide content in the instructions or the routing map. When you add or rename a guide, update the routing map row that points at it.

## ClioRing MCP compatibility gate

ClioRing is independently built and released, but it is a consumer of clio's MCP contract.
Decoupling forbids implementation/project coupling; it does **not** permit unvalidated protocol
breakage. Clio is the provider and must run consumer-driven compatibility checks when its MCP
surface can affect Ring.

This gate is mandatory when changing any of the following:

- An MCP tool invoked directly by Ring or dispatched through `clio-run`, including its name,
  arguments, defaults, validation, destructive classification, result content, or error envelope.
- Tool discovery/contract output, environment catalog output, child-process startup/handshake, or
  cancellation and process-lifetime behavior consumed by Ring.
- `notifications/progress`, progress-token correlation, `_meta.clioStageEvent`, deployment receipt,
  manifest/stage/terminal semantics, sequence ordering, or failure/success classification.
- A CLI command invoked by Ring through `clio-run`, even when no dedicated MCP tool class changed.

Do not maintain a duplicate static tool list here. Determine the live consumer surface by searching
`clio-ring/ClioRing.Ipc`, `clio-ring/ClioRing`, and `clio-ring/ClioRing.Desktop/actions.json` for
tool calls and nested command names.

Before completing a change covered by this gate:

1. Add/update provider-side MCP unit and E2E coverage as required by the normal MCP policy.
2. Run `dotnet test clio-ring/ClioRing.Tests/ClioRing.Tests.csproj -c Release` against the changed
   clio source/contract.
3. Run the relevant Ring contract/harness path; for typed stage events, preserve byte/schema parity
   of the committed contract fixture and verify unknown-field tolerance and ordered replay.
4. Run the Windows x64 NativeAOT publish command from the ClioRing policy. Contract changes can
   alter source-generated DTO/serialization paths, so JIT-only validation is insufficient.
5. If compatibility cannot be preserved additively, introduce an explicit schema/protocol version
   and a transition supporting the previously released Ring. Never rely on clio and Ring being
   upgraded atomically.

State `ClioRing compatibility reviewed` plus the exact Ring commands/results in the change summary
or PR description. If inspection proves the changed surface is not consumed by Ring, state
`ClioRing compatibility reviewed, no Ring-consumed contract changed` and cite the inspected paths.

## Skill to use

For command documentation tasks, explicitly use the `document-command` skill.
- Trigger this skill when documenting a command, updating command help/docs, or when command-related source changes may affect docs.
- Preferred invocation pattern: `$document-command <request>`.

For MCP tool implementation tasks, explicitly use the `create-mcp-tool` skill.
- Trigger this skill when adding or updating files under `clio\clio\Command\McpServer\Tools`, `Prompts`, or `Resources`, or when command changes require MCP contract alignment.
- Preferred invocation pattern: `$create-mcp-tool <request>`.

For MCP tool testing tasks, explicitly use the `test-mcp-tool` skill.
- Trigger this skill when adding or updating MCP unit tests in `clio.tests` or MCP end-to-end tests in `clio.mcp.e2e`.
- Preferred invocation pattern: `$test-mcp-tool <request>`.

## Trigger conditions for mandatory doc review

Review docs whenever any of the following is changed:
- Command options classes (for example classes with `[Verb]`, `[Option]`, `[Value]` attributes)
- Command handlers/execution logic (for example `*Command`, validators, mapping in `Program.cs`)
- Model-generation/output behavior used by a command (for example model builders, generated attributes, helper files, extension methods)
- Authentication/requirements/dependencies for command execution (for example `cliogate` requirement)

## Required documentation targets

For every touched command, verify and update all relevant files:
- `clio\help\en\<command>.txt` (CLI `-H` help)
- `clio\docs\commands\<command>.md` (detailed GitHub docs)
- `clio\Commands.md` (overview/index and command section)
- `clio\Wiki\WikiAnchors.txt` (canonical command and alias anchor mapping)

## Update rules

- Resolve aliases to canonical command name from `[Verb("command-name", Aliases = ...)]`; use canonical name in filenames.
- Keep argument lists, defaults, required flags, examples, and notes aligned with current source behavior.
- If docs are still accurate after review, explicitly state "docs reviewed, no update required" in the change summary/PR description.

# C# inline documentation policy

When adding or changing C# code, document public API using inline XML documentation comments (`///`).

- Add `///` summaries (and relevant tags like `param`, `returns`, `remarks`) for public types and members.
- If a class/member implements an interface contract, place the authoritative documentation on the interface member.
- In implementations, avoid duplicating full docs; keep docs at the interface level and use implementation comments only when behavior differs and needs clarification.

# Test style policy

When adding or changing tests, keep structure and assertions consistent.

- Use AAA structure explicitly: `Arrange`, `Act`, `Assert`.
- Every assertion must include a `because` explanation.
- Every test method must have a `[Description("...")]` attribute.
- All tests must be executable on macOS, Linux, and Windows; avoid OS-specific commands/paths unless the test explicitly validates OS-specific behavior.

## Command tests

When testing command classes:

- Prefer `BaseCommandTests<TOptions>` as the fixture base class for command tests.
- Do not add `[Category("UnitTests")]` when a fixture already inherits from `BaseCommandTests<TOptions>`.
- Register test doubles and command-specific dependencies in `AdditionalRegistrations(IServiceCollection containerBuilder)`.
- Resolve command system-under-test instances from the DI container in setup (`Container.GetRequiredService<TCommand>()`) instead of constructing with `new`.
- Clear substitute received calls in teardown (`ClearReceivedCalls`) to avoid cross-test interference.

# Smart regression testing policy

Before committing any change, run only the tests for affected modules — do not run the full suite unless core infrastructure changed. This keeps feedback time under 10 seconds for typical single-module changes instead of 60+ seconds for the full unit suite.

## Module-to-source mapping

| Module trait | Source paths |
|---|---|
| `Command` | `clio/Command/` (root-level command files) |
| `McpServer` | `clio/Command/McpServer/` |
| `ApplicationCommand` | `clio/Command/ApplicationCommand/` |
| `CreatioInstallCommand` | `clio/Command/CreatioInstallCommand/` |
| `ProcessModel` | `clio/Command/ProcessModel/` |
| `ModelBuilder` | `clio/ModelBuilder/` |
| `Common` | `clio/Common/` |
| `Package` | `clio/Package/` |
| `Workspace` | `clio/Workspace/`, `clio/Workspaces/` |
| `Core` | `clio/Core/` |
| `Query` | `clio/Query/` |
| `Requests` | `clio/Requests/` |
| `Validators` | `clio/Validators/` |
| `Theming` | `clio/Theming/` |

## Selection rules

1. **Identify changed source paths** using `git diff --name-only HEAD` (or staged files).
2. **Map each changed path to its module trait** using the table above.
3. **Run targeted tests** before committing:

```shell
# Single module
dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=Command" --no-build

# Multiple modules (pipe-separated)
dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&(Module=Command|Module=Common)" --no-build
```

4. **Full-suite triggers** — run `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit"` when any of the following changed:
   - `clio/BindingsModule.cs` or `clio/Program.cs` — DI composition root, affects all modules
   - `clio/Common/` — shared dependency used by every module
   - Changes span more than 3 distinct modules
   - Test infrastructure files: `clio.tests/TestAssemblySetup.cs`, `BaseClioModuleTests.cs`, `BaseCommandTests.cs`

5. **Analyzer-only changes** — `Clio.Analyzers/**` changed: run only the analyzer test project:

```shell
dotnet test Clio.Analyzers.Tests/Clio.Analyzers.Tests.csproj --no-build
```

## Mandatory agent behavior

- **Before every commit**: run the targeted test filter for each changed module and confirm all pass.
- **Do not commit if targeted tests fail.**
- When targeted tests pass but the change touches shared infrastructure (rule 4), additionally run the full unit suite.
- Include the filter command used in the commit message or PR description so reviewers know what was validated locally (e.g., `Validated: dotnet test --filter "Category=Unit&Module=Command"`).

# Instance creation and DI policy

Prefer resolving instances from the DI container and avoid manual construction via `new` for behavior-bearing classes.

- Do not instantiate services, handlers, managers, repositories, validators, or other behavior classes with `new`.
- Register behavior classes in the DI container and consume them through constructor injection.
- Any class that implements behavior must have an interface and be registered in DI through that interface.
- Exception: simple DTO/value carriers may be created with `new`. Prefer `record`/`record class` for these data-only types.

## CLIO analyzer handling

Treat custom `CLIO*` diagnostics as actionable and rely on `clio/.editorconfig` as the source of truth for severity.

- Use analyzer ID numbering convention to group importance:
- `CLIO1xx`: architecture/runtime safety (high importance).
- `CLIO2xx`: developer experience/style (medium importance).
- `CLIO9xx`: experimental/incubation (low importance).
- Because `.editorconfig` cannot define numeric ranges directly, add explicit per-ID severity entries for each new rule.
- Favor fixing diagnostics over suppressing; if suppression is required, add a short justification comment near the suppression.
- When modifying an existing file, fix relevant `CLIO*` warnings in that file within the edited scope instead of leaving them behind.
- Never introduce new `CLIO*` warnings in newly added or modified code.
- Treat clean `CLIO*` diagnostics in modified files as part of task acceptance.

### CLIO001 specifics

- Favor DI resolution and constructor injection over manual construction.
- Using `new`/`new()` for behavior classes should be a last resort, not normal practice.

### CLIO005 specifics

- Favor removing dead DI registrations over suppressing the diagnostic.
- Use `[ResolvedDynamically]` only for services genuinely resolved via reflection or from another assembly (e.g. `clio.mcp.server`).
- See the "Using [ResolvedDynamically]" callout in `project-context.md` for the full what/when/when-not guidance.

# Workspace diary

Keep a persistent engineering diary to speed up future tasks.

Canonical diary file:
- `./.codex/workspace-diary.md`

Mandatory agent behavior:
- For any non-trivial task, read the latest relevant diary entries before implementing changes.
- After completion of non-trivial work, append a new diary entry.
- Keep entries concise, factual, and path-referenced.
- Do not rewrite history; append only.
- If a task is exploratory and no code changes are made, still record key discoveries.

Entry format:
```markdown

## YYYY-MM-DD HH:mm – <short title>
Context: <why this work happened>
Decision: <important decision or approach>
Discovery: <important behavior/constraint learned>
Files: <path1>, <path2>
Impact: <how this helps future tasks>
```

# Code review

Agentic code review is **mandatory** and runs on multiple parallel agents covering:
- code quality and maintainability
- performance and correctness
- security and best practices

## When review is required (gates)

1. **Before opening a PR — ALWAYS (comprehensive).** Run a full parallel review over the
   PR's complete diff against the base branch. Resolve every Blocker/High finding before
   opening. No PR is opened without this gate.
2. **On every new commit pushed to an already-open PR (scoped, incremental).** Each new
   commit is reviewed — but scoped to *that commit's diff only*, not the whole PR, so the
   cost stays proportional to the change (see "Keeping it fast" below).
3. **Before marking a PR ready-to-merge — ALWAYS (comprehensive).** The authoritative final
   gate: one comprehensive adversarial review over the *entire* PR diff (status
   `review` → `done` in `spec/sprint-status.yaml`, or clearing draft). This is the real
   quality bar — the per-commit reviews are cheap early warning, this is the end-of-cycle
   gate every contribution must pass.

## Keeping it fast (do NOT review pointlessly)

The per-commit gate (2) must not slow development. Triage each post-open commit and pick the
cheapest sufficient review — escalate, never default to the full fan-out:

- **Skip entirely** (log "review skipped: <reason>") when the commit touches only zero-risk
  paths: docs (`*.md`, `help/**`, `docs/**`), comments/whitespace only, generated artifacts
  (`*.gz`, lockfiles, baselines), or test fixtures/data.
- **Single combined lens** for a small code diff (roughly < 50 changed lines in one module):
  one reviewer pass, not three.
- **Full 3-lens fan-out** only when the commit is substantive: multiple modules, > ~50 lines,
  security-sensitive code, or it touches shared infrastructure (`clio/Common/**`,
  `BindingsModule.cs`, `Program.cs`).
- **Severity gate:** only Blocker/High findings block the commit/PR; Medium/Low are advisory
  comments to address before the final gate (3).

State which tier ran (and why) in the PR thread / change summary so the scope is auditable.

## How to run it / automation path

- **Now (agent-driven):** the implementing agent runs the gates locally via the `Agent` tool
  (parallel reviewer subagents) — pre-PR and final gates use the full fan-out; per-commit uses
  the triage above.
- **End state (CI-enforced, so *all* contributions are reviewed):** a GitHub Actions workflow on
  `pull_request: [opened, ready_for_review, synchronize]` runs the reviewer headlessly and posts
  findings as a PR review. `opened`/`ready_for_review` → comprehensive (gates 1 and 3);
  `synchronize` → scoped incremental with the triage skip (gate 2). This moves the guarantee
  off the honor system without adding a lengthy review to every push.


## Nuget Management
This projects uses Centrally managed nuget packages versions, see
[Directory.Packages.props](./Directory.Packages.props) for details.

- Package families that must move together use one named MSBuild property (for example
  `AvaloniaVersion`, `MsPackageVersion`, and `SIOVersion`) referenced by every family member.
- Prefer the latest compatible stable package version. Use ordinary versions such as `12.1.0`,
  not exact-range syntax such as `[12.1.0]`, so Rider, Visual Studio, and dependency automation can
  discover upgrades. An exact range requires a documented compatibility reason next to the entry.

# Feature toggles (experimental commands)

Experimental / not-for-public CLI commands can be hidden behind a runtime flag.

**To make a command experimental:**

1. Put `[FeatureToggle("feature-key")]` on its **options class**, next to `[Verb]` — **not** on the command class. The options type is the only thing the parser and help reflect over.
2. If the command also has an MCP tool/resource/prompt, put the **same** `[FeatureToggle("feature-key")]` on that `[McpServerToolType]` / `[McpServerResourceType]` / `[McpServerPromptType]` class too. The MCP surface is gated separately; the CLI attribute does not cover it.
3. The feature is **off by default.** Enable it to test: `clio experimental --name feature-key --enable` (list/inspect with bare `clio experimental`; turn off with `--disable`). Flags persist in `appsettings.json` under `features`; there is no environment-variable override.

While off, the command is invisible and unreachable on every surface (CLI parse, help, generated docs, dispatch, MCP) and is omitted from generated public docs. A command with no `[FeatureToggle]` is unaffected.

**Do not** reintroduce `WithToolsFromAssembly` / `WithResourcesFromAssembly` / `WithPromptsFromAssembly`, and do not pass a `Type[]` to `WithTools` / `WithResources` / `WithPrompts` — that binds to the SDK's generic overload and registers nothing. MCP registration must go through `McpFeatureToggleFilter.RegisterEnabledPrimitives` (which uses `IEnumerable<Type>`).

See the "Feature toggles" section in `project-context.md` for the full rule and the four enforcement surfaces.

# ClioRing contribution policy

ClioRing is the optional internal-preview desktop companion in `clio-ring/`. Before changing it,
read **ClioRing companion architecture (internal preview)** in `project-context.md`; that section
is authoritative for its product boundary, dependency direction, protocol, release trust model,
privacy, and deletion strategy.

Mandatory rules:

- Treat **ClioRing** / `clio-ring` as the only product-family identity. Keep project paths,
  namespaces, workflow commands, assembly identities, and solution entries aligned.
- Keep clio core independent from Avalonia and Ring application assemblies. Ring integration with
  clio uses supported CLI/MCP contracts; never add a shortcut project reference into clio internals.
- **Maintain NativeAOT compatibility on every Ring change.** Keep ModelContextProtocol SDK/reflection
  behavior inside the IPC boundary. Production projects use plain interfaces/records and
  source-generated `System.Text.Json`; new IL2026/IL3050 warnings or a failed Windows x64 AOT publish
  are release blockers, not warnings to suppress. JIT success is insufficient.
- Treat progress delivery as concurrent and out of order. Preserve the `(runId, sequence)` ordered
  buffering contract, the sequence-zero manifest, and explicit terminal success/failure semantics.
- Keep clio settings as the only environment catalog. Refresh caches; do not create a Ring-owned
  environment database or copy credentials into Ring settings.
- Do not add telemetry. Do not log, persist, display, or put into test snapshots any secret-bearing
  configuration, connection string, token, password, or authorization header.
- Never let an agent, test, probe, watcher, retry, or startup path perform a real deploy/uninstall
  without an explicit user gesture and disposable target confirmation. Prefer the debugger/button
  harness for proving the UI happy path, then add focused regression tests.
- Preserve the secure updater invariants listed in `project-context.md`. Do not broaden download
  hosts, accept unsigned/unhashed assets, remove size/path bounds, allow downgrades, or weaken the
  install/update/uninstall lifecycle lock without an approved security design.
- Ring lifecycle remains intentionally absent from MCP unless a concrete agent use case and safe
  authorization model are approved. Do not expose a local UI installer merely for surface parity.
- Keep the feature removable: Ring-specific state, migrations, and dependencies must not leak into
  ordinary clio environment operation.

Required Ring validation commands:

```powershell
dotnet test clio-ring/ClioRing.Tests/ClioRing.Tests.csproj -c Release
dotnet publish clio-ring/ClioRing.Desktop/ClioRing.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishAot=true
```

For changes to the `clio ring` lifecycle command/bootstrap, also run the targeted Command-module
tests under every target framework affected by the change. Release workflow or bootstrap changes
require focused tests for checksum/host/path bounds, downgrade/repair behavior, concurrency, and
locked-executable uninstall; a successful UI launch alone is not sufficient.

# BMAD development pipeline

Clio uses the BMAD method (Break it down, Model, Act, Debug) for feature planning.
AI agents operating on this repo must understand and respect the pipeline.

## What is BMAD

BMAD is a structured pipeline where each phase produces an artifact that feeds the next.
No code is written until the ADR exists. No PR is opened until a story file exists.

```
Feature request
      │
      ▼
[pm-agent]         → spec/prd/prd-{name}.md       Phase 1 – requirements
      │
      ▼
[architect-agent]  → spec/adr/adr-{name}.md       Phase 2 – design
      │
      ▼
[story-writer]     → spec/stories/story-{name}-N.md   Phase 3 – stories
                     spec/sprint-status.yaml
      │
      ▼
[qa-planner]       → spec/test-plans/tp-{name}.md  Phase 4 – test plan
```

Optional at any phase: `bmad-reviewer` agent (3-lens adversarial critique).

## project-context.md

Every BMAD agent loads `project-context.md` (repo root) as its first action.
This file is the single source of truth for Clio-specific constraints that cannot
be derived from reading the code alone:

- CLI flag kebab-case rule (CLIO001)
- Command pattern (`Command<TOptions>` + DI services; MediatR has been removed, do not use)
- Test categories (`Unit` / `Integration` / `E2E`)
- IApplicationClient usage policy
- Test naming convention

**If you are an AI agent**: read `project-context.md` before making any implementation
decision. If you discover a rule that is not there, add it.

## When to use each command

| Situation | Command |
|-----------|---------|
| New CLI command or significant feature | `/bmad <description>` |
| Small fix / enhancement (< 5 stories) | `/bmad-spec <description>` |
| Check pipeline state before picking work | `/bmad-status` |
| Review an artifact before proceeding | use `bmad-reviewer` agent |

## Facilitator vs autonomous mode

Default (facilitator): the pipeline pauses at checkpoint gates between phases.
The AI presents its analysis and waits for `[C] Continue` before proceeding.

Autonomous (`--auto`): all phases run without pauses. Use for CI-like automation
or when you have already reviewed the feature thoroughly.

```
/bmad "feature description"          # pauses for approval at each phase boundary
/bmad --auto "feature description"   # runs all phases, prints summary at end
```

## Artifact conventions

| Artifact | Location | Naming |
|----------|----------|--------|
| PRD | `spec/prd/` | `prd-{kebab-feature-name}.md` |
| SPEC (small feature) | `spec/prd/` | `spec-{kebab-feature-name}.md` |
| ADR | `spec/adr/` | `adr-{kebab-feature-name}.md` |
| Story | `spec/stories/` | `story-{feature-name}-{N}.md` |
| Sprint tracker | `spec/` | `sprint-status.yaml` |
| Test plan | `spec/test-plans/` | `tp-{kebab-feature-name}.md` |
| Review | `spec/reviews/` | `review-{artifact-slug}-{date}.md` |

## Sprint tracker

`spec/sprint-status.yaml` is the authoritative list of all stories and their status.

Valid statuses: `ready-for-dev` → `in-progress` → `review` → `done`

AI agents implementing a story must:
1. Update the story's `status` to `in-progress` when starting
2. Update to `review` when opening a PR
3. Update to `done` when the PR merges

## Mandatory agent behavior for new features

1. Before writing any code for a non-trivial feature, check whether a PRD and ADR exist
   in `spec/prd/` and `spec/adr/`. If not, run the pipeline first.
2. Pick stories from `spec/sprint-status.yaml` with status `ready-for-dev`.
3. After implementation, ensure the test plan in `spec/test-plans/` is satisfied —
   all TC-U-* and TC-I-* test cases must be implemented.
4. Never close a story as `done` if its Definition of Done checklist has unchecked items.
