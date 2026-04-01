# uninstall-app-remote

Uninstall an application package from Creatio.


## Usage

```bash
clio uninstall-app-remote <APP_NAME> [OPTIONS]
```

## Description

Removes an installed application from the target Creatio environment.

## Aliases

`uninstall`

## Examples

```bash
clio uninstall-app-remote MyApp -e dev
Remove an application from the dev environment
```

## Arguments

```bash
Name
    Application name. Required.
```

## Options

```bash
<APP_NAME>
Application name to uninstall

-e, --Environment <ENVIRONMENT_NAME>
Target environment name
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

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

## See also

- `push-pkg`
- `get`

- [Clio Command Reference](../../Commands.md#uninstall-app-remote)
