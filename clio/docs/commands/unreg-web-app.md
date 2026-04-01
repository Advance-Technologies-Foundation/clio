# unreg-web-app

## Name

unreg-web-app - Remove a registered Creatio environment

## Usage

```bash
clio unreg-web-app [<Name>] [options]
```

## Description

Remove a registered Creatio environment.
When <Name> and -e/--Environment are omitted, the command shows the registered
environments and asks you to choose one.

## Aliases

unreg

## Examples

```bash
clio unreg-web-app dev
clio unreg-web-app -e dev
clio unreg-web-app
clio unreg-web-app --all
```

## Arguments

```bash
Name
Application name
```

## Options

```bash
--all
Remove all registered environments
```

## Notes

In --silent mode, pass <Name>, -e/--Environment, or --all because interactive
selection is disabled.
Only -e/--Environment and --silent from the shared environment options affect
this command.

## Environment Options

```bash
-u, --uri <VALUE>
Application uri
-p, --Password <VALUE>
User password
-l, --Login <VALUE>
User login (administrator permission required)
-i, --IsNetCore
Use NetCore application)
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

- [Clio Command Reference](../../Commands.md#unreg-web-app)
