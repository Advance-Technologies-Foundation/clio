# install-dbhub

## Command Type

    Integrations and tools

## Name

install-dbhub - Install, adopt, or repair a local dbHub HTTP MCP server

## Synopsis

```powershell
clio install-dbhub [--config-path <path>] [--host 127.0.0.1] [--port <port>] [--sync-local-environments <true|false>]
```

## Description

Installs the release-pinned `@bytebase/dbhub` npm package and creates or repairs
a current-user Windows Scheduled Task. The task invokes Node.js directly,
starts dbHub at logon, and binds its unauthenticated HTTP server to
`127.0.0.1`.

Existing valid TOML is adopted and preserved only when every source explicitly
keeps clio-managed `execute_sql` tools at `readonly = true`,
and the credential file is not broadly readable. When `--config-path` is omitted,
clio uses the persisted path, then `~/dbhub.toml` when it exists, then a
clio-owned config under the appsettings directory.

## Options

```text
--config-path <path>                 Explicit dbHub TOML path.
--host <host>                        HTTP bind host. Only 127.0.0.1 is accepted.
                                     Default: 127.0.0.1.
--port <port>                        HTTP port. Default: 7999.
--sync-local-environments <boolean>  Synchronize eligible local Creatio sources
                                     after successful deploy and uninstall.
                                     Default: true.
```

## Requirements

- Windows.
- Node.js 22.5 or later and npm on `PATH`.
- An unused loopback port, or a healthy dbHub server on that port.

## Examples

```powershell
clio install-dbhub
clio install-dbhub --config-path C:\Users\me\dbhub.toml --port 7999
clio install-dbhub --sync-local-environments false
```

## Behavior

- Installs exactly the dbHub version pinned by this clio release.
- Validates existing TOML before changing installation state.
- Adds a harmless in-memory SQLite `clio_control` source only when a configuration has no sources;
  dbHub 0.23.0 requires at least one source to start and hot-reload.
- Creates or repairs a hidden, least-privilege, current-user logon task.
- Verifies `/healthz` and the MCP initialize handshake.
- Persists settings only after verification succeeds.
- Refuses non-loopback binding and wildcard-bound existing servers.
- Writes clio-generated SQL tools in read-only mode and refuses adopted writable/default SQL tools,
  writable clio-managed tools. User-maintained sources, custom tools, and permissions outside clio
  markers are preserved and remain the user's responsibility.

Running this command is an explicit opt-in to a local data-access service. dbHub
HTTP does not provide authentication: loopback prevents remote access but does
not isolate other users or processes on the same machine. Read-only SQL prevents
database mutation, not sensitive-data reads. Use this integration only on a
trusted single-user workstation; do not expose or forward its port.

## Persisted Settings

After successful verification, clio stores the effective configuration in its
`appsettings.json` file. Paths are resolved at installation time; no
user-specific path is compiled into clio.

```json
{
  "dbhub": {
    "enabled": true,
    "config-path": "C:\\Users\\me\\dbhub.toml",
    "host": "127.0.0.1",
    "port": 7999,
    "sync-local-environments": true
  }
}
```

## Exit Codes

    0   dbHub was installed, adopted, or repaired and verified
    1   Validation, prerequisite, installation, task, or health check failed

## See Also

- [sync-dbhub](sync-dbhub.md) - Reconcile clio-owned dbHub sources
- [deploy-creatio](deploy-creatio.md) - Deploy a local Creatio environment
- [uninstall-creatio](uninstall-creatio.md) - Uninstall a local Creatio environment

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#install-dbhub)
