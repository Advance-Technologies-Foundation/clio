# Restore Database

## Purpose

Restores a database from a backup file or Creatio ZIP package, or creates only a reusable PostgreSQL template from that backup.

Every `restore-db` invocation creates a temp database-operation log file. The CLI prints the absolute path in a final `Database operation log:` line, and the MCP tools return the same path in `log-file-path`.

## Usage

```bash
clio restore-db [options]
```

## Aliases

- `rdb`

## Options

| Argument | Required | Description |
|---|---|---|
| `--backupPath` | Yes | Path to `.backup`, `.bak`, or a Creatio ZIP containing `db/*.backup` or `db/*.bak`. |
| `--dbName` | Yes unless `--as-template` | Database name to create or restore. |
| `--dbServerName` | No | Local database server key from `appsettings.json`. When omitted, PostgreSQL `.backup` and ZIP flows can still run directly. |
| `--drop-if-exists` | No | Drops the existing database before restore. In `--as-template` mode, drops the matching existing PostgreSQL template before recreating it. |
| `--as-template` | No | Creates or refreshes only the PostgreSQL template and does not create the target database. Supported only for PostgreSQL `.backup` or ZIP sources. |
| `--disable-reset-password` | No | Hidden advanced option. Defaults to `true` and reuses the same post-restore password-reset disabling behavior as `deploy-creatio`. Set it to `false` to skip that step. |

## Behavior

When `--backupPath` points to a PostgreSQL `.backup` file or a ZIP package that contains `db/*.backup`, `restore-db` can now work without `--dbServerName`.

When `--dbServerName` is specified, clio:

- loads the configured server from `appsettings.json`
- tests the connection before making any restore changes
- detects the backup type from file extension
- rejects PostgreSQL-to-MSSQL and MSSQL-to-PostgreSQL mismatches before restore
- restores PostgreSQL by creating or reusing a template and then creating the target database from that template
- restores MSSQL with the existing local SQL Server flow

When `--as-template` is specified, clio:

- requires a PostgreSQL `.backup` or PostgreSQL ZIP source
- creates or refreshes only the reusable PostgreSQL template
- skips target database creation
- treats `--drop-if-exists` as "drop the existing matching template before recreating it"
- does not run the post-restore password-reset helper because no target database is created

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
- `pgToolsPath` is optional, but `pg_restore` must be available on the machine running clio.
- When `enabled` is `false`, the entry is ignored by clio commands.

## Examples

Restore PostgreSQL from ZIP without `--dbServerName`:

```bash
clio restore-db --backupPath "C:\Creatio\8.3.4.1788_Studio_Softkey_PostgreSQL_ENU.zip" \
  --dbName creatiodev --drop-if-exists
```

Create or refresh only a PostgreSQL template from ZIP:

```bash
clio restore-db --backupPath "C:\Creatio\8.3.4.1788_Studio_Softkey_PostgreSQL_ENU.zip" \
  --as-template --drop-if-exists
```

Restore PostgreSQL to a configured local server:

```bash
clio restore-db --dbServerName docker-postgres --dbName creatiodev \
  --backupPath "C:\Creatio\database.backup"
```

Restore PostgreSQL ZIP to a configured local server:

```bash
clio restore-db --dbServerName docker-postgres --dbName creatiodev \
  --backupPath "C:\Creatio\8.3.4.1788_Studio_Softkey_PostgreSQL_ENU.zip"
```

Create or refresh only the local PostgreSQL template:

```bash
clio restore-db --dbServerName docker-postgres \
  --backupPath "C:\Creatio\database.backup" --as-template --drop-if-exists
```

Restore MSSQL to a configured local server:

```bash
clio restore-db --dbServerName my-local-mssql --dbName creatiodev \
  --backupPath "C:\Creatio\database.bak"
```

## Troubleshooting

`pg_restore not found`

- Install PostgreSQL client tools on the machine running clio.
- Add the PostgreSQL `bin` directory to `PATH`.
- Or set `pgToolsPath` in `appsettings.json`.

Backup type mismatch

- Use `.backup` or PostgreSQL ZIP packages with PostgreSQL/template mode.
- Use `.bak` or MSSQL ZIP packages with MSSQL local server mode.

Database operation log

- Check the final `Database operation log:` line for the temp artifact path.
- The artifact includes normal clio output and native restore-engine messages from `pg_restore` or SQL Server restore progress when available.

## Related Commands

- [`deploy-creatio`](./deploy-creatio.md)
- [`restore-configuration`](../../Commands.md#restore-configuration)
- [`Commands Overview`](../../Commands.md)
