# get-app-list

List installed applications.


## Usage

```bash
clio get-app-list [OPTIONS]
```

## Description

Displays the list of applications installed in the selected environment.

## Aliases

`app-list`, `apps`, `apps-list`, `lia`, `list-apps`

## Examples

```bash
clio get-app-list -e dev
Show applications installed in the dev environment
```

## Options

```bash
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
- `uninstall-app-remote`

- [Clio Command Reference](../../Commands.md#get-app-list)
