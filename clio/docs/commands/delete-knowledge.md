# delete-knowledge

## Command Type

    Integrations & tools

## Name

delete-knowledge - Delete Clio-managed local knowledge artifacts

## Synopsis

```bash
clio delete-knowledge [--force]
```

## Description

Deletes the managed activation marker, installed versions, staging data, and downloaded reference
examples. It preserves the configured root directory, the `knowledge-root-path` setting, and any
unrelated files in that directory.

Deletion proceeds only when the root contains Clio's ownership marker. The activation marker is
withdrawn before recursive cleanup, so a running MCP server stops serving deleted knowledge even
if cleanup is interrupted and completed by a later retry.

The command requires interactive confirmation. Non-interactive hosts must pass `--force` and should
obtain user authorization first.

## Options

```bash
--force    Confirm deletion without an interactive prompt
```

## Examples

```bash
clio delete-knowledge
clio delete-knowledge --force
```

## Exit Codes

    0   Managed knowledge was deleted, or was not installed
    1   Confirmation was refused, or locking/filesystem cleanup failed

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#delete-knowledge)
