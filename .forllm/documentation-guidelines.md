# Public Documentation Guidelines for Clio Commands

This repository uses `docfx` to generate documentation.
All documentation files are located in the [documentation](../documentation) folder folder.

## 📍 Documentation Location

All documentation for CLI commands must be placed in:

```
/documentation/commands/
```

Each command must have a single Markdown file. The filename must match the `Verb` from the `Options` class, e.g., `restart-web-app.md`.

## ✅ Steps for Documenting a Command

### Step 1: Identify the Command and Options

Locate the `ExecuteCommandWithOption` method in `Program.cs`.
This method maps an options class (e.g., `RestartOptions`) to a command class (e.g., `RestartCommand`).

Example:

```csharp
RestartOptions opts => CreateRemoteCommand<RestartCommand>(opts).Execute(opts),
```

This tells you that:
- `RestartOptions` defines the CLI arguments
- `RestartCommand` is the actual command logic

### Step 2: Extract Metadata from the Options Class

Look for the `Verb` attribute on the `Options` class:

```csharp
[Verb("restart-web-app", Aliases = ["restart"], HelpText = "Restart a web application")]
public class RestartOptions : RemoteCommandOptions { }
```

From this you can extract:
- Command name: `restart-web-app`
- Aliases: `restart`
- Description: "Restart a web application"
- Inherited options from base classes (e.g., `RemoteCommandOptions`)

Also extract `[Option]` attributes for flags, arguments, and defaults. For example:

```csharp
[Option("timeout", Required = false, HelpText = "Request timeout", Default = 100_000)]
```

### Step 3: Understand Command Behavior

Open the associated command class (e.g., `RestartCommand`) and briefly describe what the command does.
Follow the command's logic to understand its behavior, especially if it has complex logic or side effects.

### Step 4: Create the Markdown File

- Filename must match the `Verb`, e.g., `restart-web-app.md`
- File must be placed in [commands](../documentation/commands) folder
- Format the content as described below

### Step 5: Update table of contents
- Update the `toc.yml` file in the same folder to include the new command documentation.


## 🧩 Markdown File Format

Each file must follow this structure:

```markdown
# <verb>

## Summary
<Brief summary of the command>

## Aliases
- <alias1>
- <alias2>

## Options

| Name        | Short | Type   | Required | Description                          | Default   |
|-------------|-------|--------|----------|--------------------------------------|-----------|
| --option    | -o    | string | Yes      | Description of the option            |           |
| --timeout   | N/A   | int    | No       | Request timeout                      | 100000    |

*Options from base classes must be included and marked as inherited if applicable.*

## Examples

Provide usage examples for the command. Use realistic scenarios that demonstrate how to use the command effectively.
Provide several usage examples, including different options and combinations.
```bash
clio <verb> --option value --timeout 3000
```

## Description

Explain what the command does in detail.
Mention **important** behaviors, steps, or caveats.
Do not include implementation details, focus on user-facing behavior.

## Validation Checklist

- [x] Filename matches the `Verb`
- [x] Located in [commands](../documentation/commands) folder
- [x] Includes all options and inherited options
- [x] Includes aliases
- [x] Includes usage examples
- [x] No duplicate files

## 🔁 Base Option Classes

If a command inherits from a base options class (e.g., `RemoteCommandOptions`),
ensure inherited options are documented.
These may include but not limited to:
- `--Login`, `-l`
- `--Password`, `-p`
- `--Environment`, `-e`

## 🤖 LLM Agent Instruction

When generating command documentation, use the format above. Ensure accuracy in:
- File naming
- Folder placement
- Option coverage (including inherited)
- Realistic usage examples
- Language clarity and consistency

If any expected mapping, alias, or behavior is unclear from the source code, annotate that section with `TODO:` for human review.


