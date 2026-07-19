# info-knowledge

## Command Type

    Integrations & tools

## Name

info-knowledge - Show local Clio knowledge installation and update status

## Synopsis

```bash
clio info-knowledge [--offline] [--json]
```

## Description

Shows the visible `appsettings.json` path, configured knowledge root, active and previous package
versions, extracted active-content directory, source, package ID, installation time, validation
state, and update availability.

By default the command performs a bounded NuGet catalog check. Use `--offline` to inspect only the
disk cache. `--json` emits machine-readable output.

## Options

```bash
--offline    Do not contact the configured NuGet source
--json       Emit indented JSON
```

## Examples

```bash
clio info-knowledge
clio info-knowledge --offline --json
```

## Exit Codes

    0   The configured root was resolved and information was reported
    1   The knowledge root could not be resolved

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#info-knowledge)
