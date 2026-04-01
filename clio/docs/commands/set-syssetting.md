# set-syssetting

Get or set a system setting value.


## Usage

```bash
clio set-syssetting <Code> [<Value>] [<Type>] [options]
```

## Description

set-syssetting command set setting value.
set-syssetting command can be used in CI/CD pipeline or in development
when you need create or update settings on web application (website).

## Aliases

`get-syssetting`, `ss`, `sys-setting`, `syssetting`

## Examples

```bash
set-syssetting ExampleCode True Boolean - create boolean sys setting with code ExampleCode and value True
set-syssetting Maintainer ATF - update Maintainer sys setting with value ATF
```

## Arguments

```bash
Code
    Sys-setting code. Required.
Value
    Sys-setting Value
Type
    Type
```

## Options

```bash
Code (pos. 0)    Sys setting code

Value (pos. 1)   Sys setting Value

Type (pos. 2)    Sys setting Type
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

- [Clio Command Reference](../../Commands.md#set-syssetting)
