# set-fsm-config

Set file system mode properties in config file.


## Usage

```bash
clio set-fsm-config [<EnvironmentName>] <IsFsm> [options]
```

## Description

Updates the Creatio configuration file to enable or disable File Design Mode.

It changes:
- terrasoft/fileDesignMode enabled
- appSettings/UseStaticFileContent value (opposite of FSM)

You can provide either:
- --physicalPath (path to the environment folder)
- -e / --Environment (registered environment)

On Windows the command updates either Web.config or Terrasoft.WebHost.dll.config.
On macOS and Linux it supports NET8 environments and uses the registered EnvironmentPath
or the provided --physicalPath to find Terrasoft.WebHost.dll.config.

## Aliases

`fsmc`, `sfsmc`

## Examples

```bash
clio set-fsm-config -e MyEnvironment on

clio set-fsm-config --physicalPath "/path/to/creatio" off
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

- [Clio Command Reference](../../Commands.md#set-fsm-config)
