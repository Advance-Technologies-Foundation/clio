# healthcheck

Run Creatio health checks.


## Usage

```bash
healthcheck [options]
```

## Description

The healthcheck command performs health monitoring of Creatio web
applications by checking the availability and responsiveness of WebHost
and/or WebAppLoader endpoints. This command is useful for monitoring
application status in CI/CD pipelines or during development.

## Aliases

`hc`

## Examples

```bash
clio healthcheck -a true
checks WebAppLoader health status

clio healthcheck -h true
checks WebHost health status

clio healthcheck -a true -h true
checks both WebAppLoader and WebHost health status

clio healthcheck -a true -e myenv
checks WebAppLoader health status for environment named myenv
```

## Options

```bash
--WebHost               -h          Check web-host endpoint
(/0/api/HealthCheck/Ping)

--WebApp                -a          Check web-app endpoint
(/api/HealthCheck/Ping)

--uri                   -u          Application uri

--Password              -p          User password

--Login                 -l          User login (administrator permission required)

--Environment           -e          Environment name
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

- [Clio Command Reference](../../Commands.md#healthcheck)
