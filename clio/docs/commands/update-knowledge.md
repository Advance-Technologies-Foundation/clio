# update-knowledge

## Command Type

    Integrations & tools

## Name

update-knowledge - Update installed Clio knowledge to the latest verified version

## Synopsis

```bash
clio update-knowledge
```

## Description

Checks the configured NuGet catalog for a strictly newer stable package. The candidate is fully
downloaded and verified in staging before Clio atomically changes `current.json`. The previous
version remains available as a last-known-good cold-start fallback.

An already-running MCP server compares the activation marker on every knowledge lookup and serves
the newly installed version without an MCP restart.

Run `install-knowledge` first when no local installation exists. A failed update check is reported
as unavailable; it is never misreported as up to date.

## Examples

```bash
clio update-knowledge
clio info-knowledge
```

## Exit Codes

    0   Knowledge was updated, or the installed version is current
    1   Knowledge is absent, or update discovery, verification, locking, or publishing failed

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#update-knowledge)
