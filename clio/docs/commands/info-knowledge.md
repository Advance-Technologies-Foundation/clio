# info-knowledge

## Command Type

    Integrations & tools

## Name

info-knowledge - Show configured knowledge sources, installed generations, and update status

## Synopsis

```bash
clio info-knowledge [--source <alias>] [--check-updates] [--json]
```

## Description

Shows `knowledge.root-path`, the visible settings file, source configuration, installed generation,
resolved transport revision, validation state, update availability, and safe diagnostics. Omit
`--source` to inspect every configured source, including disabled sources.

The command is local-only by default: it reads persisted configuration and installed caches without
contacting Git or NuGet. Pass `--check-updates` to perform bounded checks against eligible source
transports. An update check reports availability but does not install or activate content, and Git
default-branch discovery during an information request never changes source configuration. Output
never includes transport credentials, tokens, authorization headers, or other secrets.

## Options

```bash
--source <alias>   Show only this configured source; omit for every configured source
--check-updates    Contact eligible source transports to check for available updates
--json             Emit indented JSON
```

## Examples

```bash
clio info-knowledge
clio info-knowledge --source partner --check-updates
clio info-knowledge --json
```

## Exit Codes

    0   The requested source state was reported
    1   The source selector or knowledge root could not be resolved

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#info-knowledge)
