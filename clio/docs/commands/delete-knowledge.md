# delete-knowledge

## Command Type

    Integrations & tools

## Name

delete-knowledge - Delete installed knowledge while retaining source configuration

## Synopsis

```bash
clio delete-knowledge [--source <alias>] [--force] [--json]
```

## Description

Deletes the managed installed cache for every enabled source. Pass `--source` to delete exactly one
configured source cache, including a disabled source. Source configuration, `knowledge.root-path`,
and unrelated files remain intact.

The command requires interactive confirmation. Non-interactive hosts must pass `--force` after
obtaining user authorization. Per-library activation is withdrawn before recursive cleanup, so a
running MCP server stops serving deleted knowledge even when another source remains active.

Use `remove-knowledge-source` when both the source configuration and its managed cache must be
removed.

## Options

```bash
--source <alias>   Delete only this source cache; omit for all enabled source caches
--force            Confirm deletion without an interactive prompt
--json             Emit the per-source result as indented JSON
```

## Examples

```bash
clio delete-knowledge --source partner
clio delete-knowledge --force
clio delete-knowledge --source partner --force --json
```

## Exit Codes

    0   Every selected cache was deleted or was not installed
    1   Confirmation, selection, locking, or filesystem cleanup failure

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#delete-knowledge)
