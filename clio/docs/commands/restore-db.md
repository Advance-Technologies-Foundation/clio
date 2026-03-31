# restore-db

Restore a database backup.

## Usage

```bash
clio restore-db [options]
```

## Description

Restore a database backup.

## Aliases

`rdb`

## Examples

```bash
clio restore-db -e dev
```

## Options

```bash
--disable-reset-password
    Disables reset password after restore. Default: True.
--dbName <VALUE>
    dbName
--backupPath <VALUE>
    backup Path
--dbServerName <VALUE>
    Name of database server configuration from appsettings.json
--drop-if-exists
    Automatically drops existing database if present without prompting
--as-template
    Create or refresh only the PostgreSQL template without creating a target
    database
```

## Environment Options

```bash
-u, --uri <VALUE>
    Application uri
-p, --Password <VALUE>
    User password
-l, --Login <VALUE>
    User login (administrator permission required)
-i, --IsNetCore
    Use NetCore application)
-e, --Environment <VALUE>
    Environment name
-m, --Maintainer <VALUE>
    Maintainer name
-c, --dev <VALUE>
    Developer mode state for environment
--WorkspacePathes <VALUE>
    Workspace path
-s, --Safe <VALUE>
    Safe action in this environment
--clientId <VALUE>
    OAuth client id
--clientSecret <VALUE>
    OAuth client secret
--authAppUri <VALUE>
    OAuth app URI
--silent
    Use default behavior without user interaction
--restartEnvironment
    Restart environment after execute command
--db-server-uri <VALUE>
    Db server uri
--db-user <VALUE>
    Database user
--db-password <VALUE>
    Database password
--backup-file <VALUE>
    Full path to backup file
--db-working-folder <VALUE>
    Folder visible to db server
--force
    Force restore
--callback-process <VALUE>
    Callback process name
--ep <VALUE>
    Path to the application root folder
```

- [Clio Command Reference](../../Commands.md#restore-db)
