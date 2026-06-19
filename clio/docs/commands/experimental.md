# experimental

## Command Type

    Application management

## Name

experimental - List and toggle clio experimental feature flags

## Synopsis

```bash
experimental
experimental --name <feature-key> --enable
experimental --name <feature-key> --disable
experimental [OPTIONS]
```

## Description

Lists clio feature flags and turns them on or off. Feature flags gate
experimental commands and MCP tools so they stay hidden until you opt in.

A feature key is NOT a command verb: a single key may gate several commands
at once, and the key stays stable even when a verb is renamed. Commands that
are gated behind a feature flag do not appear in `clio help`, are not
parseable, and their per-command help behaves like an unknown verb until the
flag is enabled.

Feature keys are matched case-insensitively, so `AiAssist`, `aiassist`, and
`ai-assist` resolve to the same flag (the key is stored as you first type it).

Changes are persisted to clio's appsettings.json and take effect immediately.

## Options

```bash
--name <feature-key>    The feature key to enable or disable. When omitted,
all known feature flags are listed.

--enable                Enable the feature flag named by --name.

--disable               Disable the feature flag named by --name.

--list                  List all known feature flags and their state.
This is also the default when no other arguments are supplied. When --list is
passed explicitly it takes precedence: the command lists and ignores
--name/--enable/--disable.
```

## Examples

```bash
# List all known feature flags and their state
experimental

# Same as above using the alias
clio exp

# Enable an experimental feature
experimental --name ai-assist --enable

# Disable an experimental feature
experimental --name ai-assist --disable
```

## Behavior

- With no arguments (or with --list), prints a table of every known feature key
and whether it is ENABLED or DISABLED, sorted by key. --list wins over
--name/--enable/--disable, so it always lists and never toggles.
- A flag stored in settings that no command or MCP tool references is shown
as an orphan so a leftover or renamed key stays visible and manageable.
- With --name plus exactly one of --enable/--disable, persists the change and
reports the new state. Toggling a key that nothing references is allowed
but prints a warning.

## Exit Codes

    0   Listed flags, or toggled a flag successfully
    1   Validation error (for example both or neither of --enable/--disable)

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#experimental)
