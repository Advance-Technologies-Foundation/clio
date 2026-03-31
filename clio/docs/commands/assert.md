# assert

Validates infrastructure and filesystem resources.

## Usage

```bash
clio assert <scope> [options]
```

## Description

Validates infrastructure and filesystem resources.

## Examples

```bash
clio assert <scope> [options]
```

## Arguments

```bash
scope
    Assertion scope: k8 (Kubernetes), local (Local infrastructure), or fs
    (Filesystem). Required.
```

## Options

```bash
----fail-on-error
    Return fail code on errors
----fail-on-warning
    Return fail code on warnings
--all
    Run full validation checks applicable to selected scope
--db <VALUE>
    Database engines to assert (comma-separated): postgres, mssql
--db-min <NUMBER>
    Minimum number of database engines required. Default: 1.
--db-connect
    Validate database connectivity
--db-check <VALUE>
    Database capability check (e.g., 'version')
--db-server-name <VALUE>
    Name of local database server configuration from appsettings.json
--redis
    Assert Redis presence
--redis-connect
    Validate Redis connectivity
--redis-ping
    Execute Redis PING command
--redis-server-name <VALUE>
    Name of local Redis server configuration from appsettings.json
--context <VALUE>
    Expected Kubernetes context name
--context-regex <VALUE>
    Regex pattern for Kubernetes context name
--cluster <VALUE>
    Expected Kubernetes cluster name
--namespace <VALUE>
    Expected Kubernetes namespace
--path <VALUE>
    Filesystem path to validate
--user <VALUE>
    Windows user identity to validate
--perm <VALUE>
    Required permission level: read, write, modify, full
```

- [Clio Command Reference](../../Commands.md#assert)
