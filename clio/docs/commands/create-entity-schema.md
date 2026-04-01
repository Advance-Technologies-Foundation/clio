# create-entity-schema

Create an entity schema in a remote Creatio package.


## Usage

```bash
clio create-entity-schema [options]
```

## Description

Create an entity schema in a remote Creatio package.

## Examples

```bash
clio create-entity-schema -e dev
```

## Options

```bash
--package <VALUE>
Target package name. Required.
--name <VALUE>
Schema name. Required.
--title <VALUE>
Schema title. Required.
--parent <VALUE>
Parent schema name
--extend-parent
Create replacement schema
--column <VALUE>
Column spec <name>:<type>[:<title>[:<refSchema>]] or JSON with
name/type/title/reference-schema-name/required/default-value-source/default-value.
Repeat the option for multiple columns
--timeout <NUMBER>
Request timeout in milliseconds. Default: 100000.
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

cliogate must be installed on the target Creatio environment.

## See also

- `get-entity-schema-properties`
- `modify-entity-schema-column`

- [Clio Command Reference](../../Commands.md#create-entity-schema)
