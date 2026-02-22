# Document Command (Canonical)

Canonical-ID: document-command
Canonical-Version: 1

Use this instruction set when creating or updating documentation for one `clio` CLI command.

## Required Outputs

For the target command, verify and update all relevant docs together:

- `clio/help/en/<canonical-command>.txt`
- `clio/docs/commands/<canonical-command>.md`
- `clio/Commands.md`

If everything is already accurate, report: `docs reviewed, no update required`.

## Workflow

1. Resolve canonical command name from `[Verb("<command>", Aliases = ...)]`.
2. Find command mapping in `clio/Program.cs` (`ExecuteCommandWithOptions`).
3. Inspect options and command classes, including inherited options.
4. Extract user-facing contract:
   - `[Option]` and `[Value]` parameters
   - required/optional and defaults
   - validation constraints and behavior-affecting notes
   - whether `cliogate` is required
5. Update all required outputs with consistent content and naming.
6. Re-check examples, defaults, aliases, and links for consistency.

## Content Rules

- Use canonical command name for filenames and headings.
- Mention aliases where useful, but never as filenames.
- Prefer user-facing language over internal implementation jargon.
- Keep required args and defaults aligned with source code.
- For remote/auth commands, prefer examples that use `-e/--environment` first.

## Quick Checklist

- Canonical command resolved from `[Verb]`
- Aliases handled correctly
- Required/optional/defaults documented
- Inherited options included
- `cliogate` requirement documented (if applicable)
- `clio/help/en`, `clio/docs/commands`, and `clio/Commands.md` synchronized
