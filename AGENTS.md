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

## Update rules

- If the command already has an MCP tool, keep the tool arguments, descriptions, destructive flags, and execution path aligned with the current command behavior.
- If the command is environment-sensitive, use the MCP `BaseTool` environment-aware execution pattern instead of executing the startup-time injected command directly.
- If the command has an MCP prompt, keep the prompt guidance aligned with the current tool contract.
- If no MCP artifact exists for a touched command, explicitly check whether one should be added and mention the result in the change summary.
- If MCP artifacts are still accurate after review, explicitly state "MCP reviewed, no update required" in the change summary/PR description.

## Skill to use

For command documentation tasks, explicitly use the `document-command` skill.
- Trigger this skill when documenting a command, updating command help/docs, or when command-related source changes may affect docs.
- Preferred invocation pattern: `$document-command <request>`.

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
