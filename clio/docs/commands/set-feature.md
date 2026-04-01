# set-feature

Set feature state.


## Usage

```bash
clio set-feature <Code> <State> [<onlyCurrentUser>] [options]
```

## Description

set-feature command set feature state.
set-feature command can be used in CI/CD pipeline or in development
when you need create or update feature state on web application (website).

## Aliases

`feature`

## Examples

```bash
set-feature ExampleCode 1 enable feature with code ExampleCode for all users, if feature doesn`t exists it will be created
set-feature ExampleCode 0 disable feature with code ExampleCode for all users
```

## Arguments

```bash
Code
    Feature code. Required.
State
    Feature state. Required. Default: 0.
onlyCurrentUser
    Only current user
```

## Options

```bash
Code (pos. 0)    Feature code

State (pos. 1)   Feature state
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

    Service commands

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#set-feature)
