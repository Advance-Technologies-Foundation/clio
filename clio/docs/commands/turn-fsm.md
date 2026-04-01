# turn-fsm

Turn file system mode on or off for an environment.


## Usage

```bash
clio turn-fsm [<EnvironmentName>] <IsFsm> [options]
```

## Description

Toggles Creatio file system mode (FSM).

When turning FSM on:
- Updates configuration to enable File Design Mode
- Loads packages to the file system

When turning FSM off:
- Loads packages to the database
- Updates configuration to disable File Design Mode

Use either:
- --physicalPath (path to the environment folder)
- -e / --Environment (registered environment)

On macOS and Linux this command supports NET8 environments and relies on the registered
EnvironmentPath or the provided --physicalPath to update the local config file.

## Aliases

`fsm`, `tfsm`

## Examples

```bash
clio turn-fsm -e MyEnvironment on

clio turn-fsm -e MyEnvironment off

clio turn-fsm --physicalPath "/path/to/creatio" on
```

## Arguments

```bash
EnvironmentName
    Application name
IsFsm
    on or off. Required.
```

## Options

```bash
--physicalPath <VALUE>
Path to applications
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

- [Clio Command Reference](../../Commands.md#turn-fsm)
