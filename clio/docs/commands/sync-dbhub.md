# sync-dbhub

## Command Type

    Integrations and tools

## Name

sync-dbhub - Reconcile clio-owned dbHub sources with local Creatio environments

## Synopsis

```powershell
clio sync-dbhub [--environment <name>]
```

## Description

Reads local Creatio database settings from `ConnectionStrings.config` and
reconciles clio-owned source blocks in the configured dbHub TOML file.
PostgreSQL and SQL Server authentication are supported. SQL Server Windows
integrated authentication is skipped with a safe warning because dbHub cannot
use it.

User-authored TOML and user-owned sources are never rewritten or removed. A
full sync removes stale clio-owned blocks; a selected-environment sync touches
only that environment.

## Options

```text
--environment <name>  Reconcile one registered local environment. When omitted,
                      reconcile all eligible local environments.
```

## Examples

```powershell
clio sync-dbhub
clio sync-dbhub --environment local-dev
```

## Behavior

- Generates deterministic lowercase source identifiers.
- Detects identifier collisions and user-source ownership conflicts.
- Uses an adjacent lock and atomic replace for TOML updates.
- Preserves the original file when validation or writing fails.
- Verifies dbHub tool discovery after its hot reload window.
- Reports changed, unchanged, and skipped counts without printing credentials.

Clio ownership is recorded in comment markers surrounding each managed
`[[sources]]` block. Removing or changing those markers transfers ownership
back to the user; clio will not mutate that source unless ownership is
re-established without an identifier conflict.

## Exit Codes

    0   Reconciliation completed, including best-effort per-source warnings
    1   dbHub integration is not configured

## See Also

- [install-dbhub](install-dbhub.md) - Install or adopt dbHub and persist its configuration

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#sync-dbhub)
