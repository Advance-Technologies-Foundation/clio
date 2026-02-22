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

# Instance creation and DI policy

Prefer resolving instances from the DI container and avoid manual construction via `new` for behavior-bearing classes.

- Do not instantiate services, handlers, managers, repositories, validators, or other behavior classes with `new`.
- Register behavior classes in the DI container and consume them through constructor injection.
- Any class that implements behavior must have an interface and be registered in DI through that interface.
- Exception: simple DTO/value carriers may be created with `new`. Prefer `record`/`record class` for these data-only types.


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

## YYYY-MM-DD - <short title>
Context: <why this work happened>
Decision: <important decision or approach>
Discovery: <important behavior/constraint learned>
Files: <path1>, <path2>
Impact: <how this helps future tasks>
```
