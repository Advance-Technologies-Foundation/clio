# delete-schema

Delete a schema from a workspace package.


## Usage

```bash
clio delete-schema <SCHEMA_NAME> -e <ENVIRONMENT_NAME>
```

## Description

delete-schema removes a schema from Creatio by using Workspace Explorer
service calls. The command first retrieves workspace items from the target
environment, then only allows deleting schemas whose package belongs to the
current local workspace.

This command must be executed from a workspace directory.

## Examples

```bash
clio delete-schema UsrSendInvoice -e docker_fix2
delete schema UsrSendInvoice when it belongs to one of the current
workspace packages

clio delete-schema Activity -e docker_fix2
fail when Activity is not part of the current workspace
```

## Arguments

```bash
SchemaName
    Schema name. Required.
```

## Options

```bash
Schema name (pos. 0)    Schema name to delete

--Environment       -e  Environment name

--uri               -u  Application uri

--Password          -p  User password

--Login             -l  User login

--timeout               Request timeout in milliseconds
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

- [Clio Command Reference](../../Commands.md#delete-schema)
