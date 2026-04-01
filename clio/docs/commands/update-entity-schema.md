# update-entity-schema

Apply batch column operations to a remote Creatio entity schema.

## Usage

```bash
clio update-entity-schema [options]
```

## Description

Apply batch column operations to a remote Creatio entity schema.

## Examples

```bash
clio update-entity-schema -e dev
```

## Options

```bash
--timeout <NUMBER>
    Request timeout in milliseconds. Default: 100000.
--package <VALUE>
    Target package name. Required.
--schema-name <VALUE>
    Entity schema name. Required.
--operation <VALUE>
    Structured operation JSON. Repeat the option for multiple values. Required.
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
    Use NetCore application
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
--db-name <VALUE>
    Desired database name
--force
    Force restore
--callback-process <VALUE>
    Callback process name
--ep <VALUE>
    Path to the application root folder
```

## Requirements

- cliogate must be installed on the target Creatio environment.

## See also

- `modify-entity-schema-column`
- `get-entity-schema-properties`

- [Clio Command Reference](../../Commands.md#update-entity-schema)
