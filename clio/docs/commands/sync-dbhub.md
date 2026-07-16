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
PostgreSQL and SQL-authenticated SQL Server connections are supported. SQL
Server integrated/identity-provider authentication and certificate-validation
modes that dbHub 0.23.0 cannot preserve are skipped with safe warnings.
PostgreSQL `Prefer` is tightened to dbHub `require`, while `Allow` maps to
`disable`, because dbHub 0.23.0 has no opportunistic TLS tokens.

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
- Refuses a configured HTTP endpoint outside `127.0.0.1`.
- Verifies dbHub tool discovery after its hot reload window.
- Reports changed, unchanged, and skipped counts without printing credentials.
- Leaves the harmless `clio_control` source when the last database source is removed,
  so dbHub can hot-reload a valid non-empty source list.

Clio ownership is recorded in comment markers surrounding each managed
`[[sources]]` block. Removing or changing those markers transfers ownership
back to the user; clio will not mutate that source unless ownership is
re-established without an identifier conflict.

## Exit Codes

    0   Reconciliation completed, including best-effort per-source warnings
    1   Reconciliation could not run (not configured, unsafe endpoint, missing selected environment, or internal failure)

## See Also

- [install-dbhub](install-dbhub.md) - Install or adopt dbHub and persist its configuration

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#sync-dbhub)
