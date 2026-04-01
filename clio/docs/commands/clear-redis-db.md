# clear-redis-db

Clear redis database.


## Usage

```bash
clear-redis-db [Name] [options]
```

## Description

clear-redis-db command can be used in CI/CD pipeline or in development
when you need forcible clear a web application (website) cache. Be
attentive, the command only clear  web application cache and doesn't
restart it.

## Aliases

`flushdb`

## Examples

```bash
clio clear-redis-db
clear current web application(website) cache

clio clear-redis-db myapp
clear web application(website) cache that registered as a myapp
```

## Options

```bash
Name (pos. 0)	Application name

--uri               -u          Application uri

--Password			-p          User password

--Login             -l          User login (administrator permission required)

--Environment       -e          Environment name

--Maintainer        -m          Maintainer name
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

    CI/CD commands

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#clear-redis-db)
