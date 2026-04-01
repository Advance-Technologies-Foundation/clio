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

## Scopes

    k8          Kubernetes resource assertions
    local       Local database and local Redis assertions
    fs          Filesystem path and permission assertions

## Common Options

    --all                           Run full validation checks applicable to selected scope
                                    Cannot be combined with explicit scope assertion options

## Kubernetes Options

    Context Validation:
    --context                       Expected Kubernetes context name (exact match)
    --context-regex                 Regex pattern for context name
    --cluster                       Expected Kubernetes cluster name
    --namespace                     Expected Kubernetes namespace

    Database Assertions:
    --db                            Database engines to assert (comma-separated)
                                    Supported: postgres, mssql
    --db-min                        Minimum number of database engines required
                                    Default: 1
    --db-connect                    Validate database connectivity via TCP
    --db-check                      Database capability check
                                    Supported: version

    Redis Assertions:
    --redis                         Assert Redis presence
    --redis-connect                 Validate Redis connectivity via TCP
    --redis-ping                    Execute Redis PING command

## Local Options

    Database Assertions:
    --db                            Database engines to assert (comma-separated)
                                    Supported: postgres, mssql
                                    Required for local database assertions
    --db-server-name                Local database server configuration key from appsettings.json
                                    Optional; when omitted, local DB servers are discovered from config
    --db-min                        Minimum number of database engines required
                                    Default: 1
    --db-connect                    Validate database connectivity
    --db-check                      Database capability check
                                    Supported: version
                                    PostgreSQL major version 16+ is always enforced
                                    for discovered postgres servers in k8/local scopes;
                                    --db-check version additionally returns version in output

    Redis Assertions:
    --redis                         Assert local Redis presence
                                    Resolution order:
                                    1) --redis-server-name (explicit)
                                    2) defaultRedis from appsettings.json
                                    3) single enabled redis server from appsettings.json
                                    4) localhost:6379 fallback when redis section is absent
    --redis-server-name             Local Redis server configuration key from appsettings.json
                                    Optional; requires --redis
    --redis-connect                 Validate Redis connectivity via TCP
    --redis-ping                    Execute Redis PING command
                                    Strict policy: when Redis credentials are configured in appsettings,
                                    assert also verifies anonymous access is blocked

    Unsupported in local scope:
    --context, --context-regex, --cluster, --namespace, --path, --user, --perm

## Filesystem Options

    --path                          Filesystem path to validate (required)
                                    Can be an absolute path or a setting key
                                    Setting keys: iis-clio-root-path

    --user                          Windows user/group identity to validate
                                    Format: "BUILTIN\IIS_IUSRS" or "IIS APPPOOL\AppName"
                                    Requires --perm (Windows only)

    --perm                          Required permission level (Windows only)
                                    Valid values:
                                      read          - Read permissions
                                      write         - Write permissions
                                      modify        - Modify permissions (read+write+delete)
                                      full-control  - Full control permissions
                                      full          - Alias for full-control
                                    Requires --user

## Kubernetes Examples

    Basic context validation:
        clio assert k8

    Context with specific name:
        clio assert k8 --context rancher-desktop

    Context with regex pattern:
        clio assert k8 --context-regex "^rancher-.*"

    Database presence check:
        clio assert k8 --db postgres

    Multiple databases:
        clio assert k8 --db postgres,mssql --db-min 2

    Database with connectivity:
        clio assert k8 --db postgres --db-connect

    Database with version check:
        clio assert k8 --db postgres --db-connect --db-check version

    Redis presence:
        clio assert k8 --redis

    Redis with connectivity and ping:
        clio assert k8 --redis --redis-connect --redis-ping

    Combined checks:
        clio assert k8 --db postgres --db-connect --redis --redis-ping

    Full validation:
        clio assert k8 \
            --context dev-cluster \
            --db postgres,mssql --db-min 2 --db-connect --db-check version \
            --redis --redis-connect --redis-ping

    Full scope validation preset:
        clio assert k8 --all

## Filesystem Examples

    Path validation (absolute path):
        clio assert fs --path "C:\inetpub\wwwroot\clio\s_n8\"

    Path validation (using setting key):
        clio assert fs --path iis-clio-root-path

    Path with user permissions (setting key):
        clio assert fs --path iis-clio-root-path --user "BUILTIN\IIS_IUSRS" --perm full-control

    Path with user permissions (absolute path):
        clio assert fs --path "C:\inetpub\wwwroot\clio\s_n8\" --user "BUILTIN\IIS_IUSRS" --perm full-control

    Path with modify permissions:
        clio assert fs --path iis-clio-root-path --user "BUILTIN\IIS_IUSRS" --perm modify

    Path with read permissions:
        clio assert fs --path "C:\data" --user "BUILTIN\Users" --perm read

## Local Examples

    Local Redis presence:
        clio assert local --redis

    Local Redis with explicit server:
        clio assert local --redis --redis-server-name redis-dev

    Local Redis with connectivity and ping:
        clio assert local --redis --redis-connect --redis-ping

    Local database presence:
        clio assert local --db postgres

    Local database with connectivity and version:
        clio assert local --db postgres,mssql --db-min 2 --db-connect --db-check version

    Combined local checks:
        clio assert local --db postgres --db-connect --redis --redis-ping

    Full scope validation preset:
        clio assert local --all

    Full filesystem validation preset:
        clio assert fs --all

## Design Principles

    - All checks are explicit via CLI flags (no hidden validation)
    - Deterministic behavior (same input produces same output)
    - Phase 0 context validation is mandatory for all K8 assertions
    - All checks are scoped to 'clio-infrastructure' namespace
    - Dynamic service/port resolution (no hardcoded values)
    - Read-only operations (no mutations)
    - Time-bounded operations with configurable timeouts

## Kubernetes Detection Rules

    Discovery Method:
    The assert command uses the same discovery logic as clio's k8Commands
    class to ensure consistency. Resources are found by:

    1. Checking StatefulSet/Deployment spec.selector.matchLabels
    2. Finding Services by label selector (app=clio-*)
    3. Validating at least one Pod is Running and Ready
    4. Dynamically resolving ports from Service.spec.ports

    This ensures that if assert passes, clio's normal operations (database
    restore, connection string generation, etc.) will successfully find
    and use the same resources.

    Databases:
    - Detection via authoritative labels and workload kind
    - Namespace: clio-infrastructure
    - Workload kind: StatefulSet
    - StatefulSet selector must have matchLabels:
        Postgres: app=clio-postgres
        MSSQL: app=clio-mssql
    - Services must have labels:
        Postgres: app=clio-postgres
        MSSQL: app=clio-mssql
    - At least one pod must be ready and not permanently failed

    Redis:
    - Namespace: clio-infrastructure
    - Workload kind: Deployment
    - Deployment selector must have matchLabels: app=clio-redis
    - Service must have label: app=clio-redis
    - At least one pod must be ready and not permanently failed

    Service Port Resolution:
    - Ports are discovered dynamically from Service.spec.ports
    - Uses 'port' (not 'targetPort')
    - If multiple ports exist, uses the first port
    - Outside cluster: Prefers LoadBalancer services
    - Inside cluster: Prefers ClusterIP services

    Service Naming:
    Services can have any name (e.g., postgres-service-lb,
    mssql-service-internal) as long as they have the correct labels.
    The assert command finds services by label, not by name pattern.

## Use Cases

    Pre-deployment validation:
        clio assert k8 --db postgres --redis
        clio assert fs --path iis-clio-root-path

    IIS setup validation:
        clio assert fs --path iis-clio-root-path --user "BUILTIN\IIS_IUSRS" --perm full-control

    Application directory permissions:
        clio assert fs --path "C:\inetpub\wwwroot\myapp" --user "IIS APPPOOL\MyAppPool" --perm modify

    CI/CD pipeline checks:
        clio assert k8 --db postgres --db-connect --redis --redis-ping
        clio assert fs --path iis-clio-root-path

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#assert)
