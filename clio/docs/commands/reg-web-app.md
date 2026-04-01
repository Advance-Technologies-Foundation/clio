# reg-web-app

Register a Creatio environment.


## Usage

```bash
clio reg-web-app <ENVIRONMENT_NAME> -u http://mysite.creatio.com -l administrator -p password
```

## Description

Register a new web application settings or update existing

## Aliases

`cfg`, `reg`

## Examples

```bash
clio reg-web-app <ENVIRONMENT_NAME> -u http://mysite.creatio.com -l administrator -p password
creates new environment, named <ENVIRONMENT_NAME> or updates existing environment settings
```

## Arguments

```bash
EnvironmentName
    Application name
```

## Options

```bash
Name (pos. 0)	Environment(web application) name

--ActiveEnvironment     -a          Set a web application by default

--Safe                  -s          Safe action in this environment

--uri                   -u          Application uri

--Password              -p          User password

--Login                 -l          User login (administrator permission required)

--Maintainer            -m          Maintainer name
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

- [Clio Command Reference](../../Commands.md#reg-web-app)
