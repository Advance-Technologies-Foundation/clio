# remove-knowledge-source

## Command Type

    Integrations & tools

## Name

remove-knowledge-source - Remove one configured source and its managed cache

## Synopsis

```bash
clio remove-knowledge-source --alias <alias> [--force] [--json]
```

## Description

Removes and deactivates the selected source configuration before attempting to delete that source's
Clio-managed installed generations, staging data, and activation marker. Configuration removal is
retained if best-effort cache cleanup fails; the command reports the remaining cache as orphaned.
Other source configuration, caches, and unrelated files are preserved.

Removal requires interactive confirmation. Non-interactive hosts must pass `--force` after
obtaining user authorization. Use `disable-knowledge-source` instead when the source should stop
serving but its configuration and cache should remain available for later re-enablement.

## Options

```bash
--alias <alias>   Required configured source alias
--force           Confirm configuration and cache removal without an interactive prompt
--json            Emit indented JSON
```

## Examples

```bash
clio remove-knowledge-source --alias partner
clio remove-knowledge-source --alias partner --force --json
```

## Exit Codes

    0   Source configuration was removed and managed cache cleanup completed
    1   Confirmation, selection, persistence, or best-effort cache cleanup failed

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#remove-knowledge-source)
