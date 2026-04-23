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

Use multiple agents in parallel to review code for
- code quality and maintainability
- performance and correctness
- security and best practices


## Nuget Management
This projects uses Centrally managed nuget packages versions, see
[Directory.Packages.props](./Directory.Packages.props) for details.
