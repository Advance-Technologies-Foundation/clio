# create-data-binding-db

Create a DB-first package data binding by saving data directly to the remote Creatio database.


## Usage

```bash
clio create-data-binding-db -e <ENVIRONMENT_NAME> --package <PACKAGE_NAME> --schema <SCHEMA_NAME>
[--binding-name <BINDING_NAME>] [--rows <JSON_ARRAY>]
```

## Description

Creates a DB-first package data binding by persisting row data directly to the remote Creatio database.

## Examples

```bash
clio create-data-binding-db -e dev --package Custom --schema SysSettings

clio create-data-binding-db -e dev --package Custom --schema SysSettings \
--binding-name UsrMyBinding \
--rows "[{\"values\":{\"Name\":\"Row\",\"Code\":\"UsrRow\"}}]"
```

## Options

```bash
-e, --environment          Creatio environment name (required when --uri is omitted)
--uri                      Creatio application URI (alternative to --environment)
--package                  Target package name (required)
--schema                   Entity schema name (required)
--binding-name             Binding folder name (defaults to <schema>)
--rows                     JSON array of row objects, each with a 'values' key:
[{"values":{"Col":"Value"}}]
-H, --help                 Show this help
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

- [Clio Command Reference](../../Commands.md#create-data-binding-db)
