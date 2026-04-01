# page-get

Get a Freedom UI page bundle and raw schema body.


## Usage

```bash
clio page-get [options]
```

## Description

The page-get command resolves the requested Freedom UI page, loads its full
designer hierarchy, builds the effective merged bundle, and returns a JSON
envelope with nested page metadata, bundle data, and raw.body. Use raw.body
as the editable payload for page-update.

## Examples

```bash
clio page-get --schema-name UsrTodo_FormPage -e dev
return the merged Freedom UI bundle and raw body for UsrTodo_FormPage

clio page-get --schema-name UsrTodo_FormPage -u https://my-creatio -l Supervisor -p Supervisor
read a Freedom UI page using direct connection arguments
```

## Options

```bash
--schema-name                      Page schema name to read

--uri                    -u       Application uri

--Password               -p       User password

--Login                  -l       User login (administrator permission required)

--Environment            -e       Environment name

--Maintainer             -m       Maintainer name
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

## Command Type

    Development commands

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#page-get)
