# update-knowledge

## Command Type

    Integrations & tools

## Name

update-knowledge - Update verified knowledge from one source or all enabled sources

## Synopsis

```bash
clio update-knowledge [--source <alias>] [--json]
```

## Description

Checks every enabled source for a newer valid generation. Pass `--source` to select exactly one
configured alias. Each candidate is fully retrieved and verified before its library activation
marker changes, so one failed source never replaces its last-known-good generation or affects
other sources.

Git branches follow their persisted branch and record the newly resolved complete commit ID. Tags
resolve to a commit, and an explicit commit remains immutable. When a Git source has no configured
reference, only a successful update persists the discovered remote default branch. An
already-running MCP server observes successful per-library activation changes without restarting.

## Options

```bash
--source <alias>   Update only this configured source; omit for all enabled sources
--json             Emit the per-source result as indented JSON
```

## Examples

```bash
clio update-knowledge
clio update-knowledge --source creatio
clio update-knowledge --json
```

## Exit Codes

    0   Every selected source was updated or already current
    1   Selection, retrieval, verification, locking, or publishing failure

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#update-knowledge)
