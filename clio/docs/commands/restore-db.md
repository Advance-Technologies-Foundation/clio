# Restore Database

## Purpose

Restores a database from a backup file either to the default Kubernetes database flow or to a configured local/local-style database server.

For PostgreSQL running in Docker, use `--dbServerName` with a `db` entry that points to the published host/port. In that mode clio runs `pg_restore` on the machine running clio and keeps the backup file on the local filesystem. It does not copy the `.backup` file into Docker or Kubernetes.

## Usage

```bash
clio restore-db [options]
```

## Aliases

- `rdb`

## Options

| Argument | Required | Description |
|---|---|---|
| `--dbName` | Yes | Database name to create or restore. |
| `--backupPath` | Yes for local/local-style restore | Path to `.backup`, `.bak`, or a Creatio ZIP containing `db/*.backup` or `db/*.bak`. |
| `--dbServerName` | No | Local/local-style database server key from `appsettings.json`. When omitted, clio uses the existing Kubernetes/environment behavior. |
| `--drop-if-exists` | No | Automatically drops the target database if it already exists. |

## Local Server Configuration

Add a `db` section to `appsettings.json`:

```json
{
  "db": {
    "docker-postgres": {
      "dbType": "postgres",
      "hostname": "localhost",
      "port": 5433,
      "username": "postgres",
      "password": "postgres",
      "enabled": true,
      "pgToolsPath": "C:\\Program Files\\PostgreSQL\\18\\bin",
      "description": "PostgreSQL container published to localhost:5433"
    },
    "my-local-mssql": {
      "dbType": "mssql",
      "hostname": "localhost",
      "port": 1433,
      "username": "sa",
      "password": "your_password",
      "enabled": true,
      "description": "Local MSSQL Server"
    }
  }
}
```

Configuration notes:

- `dbType` supports `postgres` and `mssql`.
- For Docker PostgreSQL, `hostname` and `port` must be the host-reachable endpoint, such as `localhost:5433` or `host.docker.internal:5432`.
- `pgToolsPath` is optional, but `pg_restore` must be available on the machine running clio.
- When `enabled` is `false`, the entry is ignored by clio commands.

## Behavior

When `--dbServerName` is specified, clio:

- loads the configured server from `appsettings.json`
- tests the connection before making any restore changes
- detects the backup type from file extension
- rejects PostgreSQL-to-MSSQL and MSSQL-to-PostgreSQL mismatches before restore
- restores PostgreSQL with local `pg_restore`
- restores MSSQL with the existing local SQL Server flow

When `--dbServerName` is omitted, clio preserves the existing Kubernetes/environment-based behavior.

## Examples

Restore PostgreSQL to a Docker-published host port:

```bash
clio restore-db --dbServerName docker-postgres --dbName creatiodev \
  --backupPath "C:\Creatio\database.backup"
```

Restore PostgreSQL from a Creatio ZIP package:

```bash
clio restore-db --dbServerName docker-postgres --dbName creatiodev \
  --backupPath "C:\Creatio\8.3.3.1343_Studio_PG_ENU.zip"
```

Restore MSSQL to a configured local server:

```bash
clio restore-db --dbServerName my-local-mssql --dbName creatiodev \
  --backupPath "C:\Creatio\database.bak"
```

Restore and drop the existing database automatically:

```bash
clio restore-db --dbServerName docker-postgres --dbName creatiodev \
  --backupPath "C:\Creatio\database.backup" --drop-if-exists
```

## Troubleshooting

`pg_restore not found`

- Install PostgreSQL client tools on the machine running clio.
- Add the PostgreSQL `bin` directory to `PATH`.
- Or set `pgToolsPath` in `appsettings.json`.
- For Docker PostgreSQL, remember clio still runs `pg_restore` on the host.

Connection test failed

- Verify the database server is running and reachable.
- Check `hostname`, `port`, `username`, and `password`.
- For Docker PostgreSQL, run `docker ps` and verify the PostgreSQL container port is published to the host.

Backup type mismatch

- Use `.backup` with PostgreSQL.
- Use `.bak` with MSSQL.

## Related Commands

- [`deploy-creatio`](./deploy-creatio.md)
- [`restore-configuration`](../../Commands.md#restore-configuration)
- [`Commands Overview`](../../Commands.md)
