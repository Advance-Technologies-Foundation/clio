# pull-pkg

Download package from a web application.


## Usage

```bash
clio pull-pkg <PACKAGE_NAME>
```

## Description

pull-pkg command can be used in CI/CD pipeline or in development
when you need download package from a web application (website).

## Aliases

`download`

## Examples

```bash
clio pull-pkg <PACKAGE_NAME>
pull-pkg package to local file system from default application

clio pull-pkg <PACKAGE_NAME> -e <ENVIRONMENT_NAME>
pull-pkg package to local file system from non default application
```

## Arguments

```bash
Name
    Package name. Required.
```

## Options

```bash
Package name (pos. 0)	Name of package for download

--uri                   -u          Application uri

--Password              -p          User password

--Login                 -l          User login (administrator permission required)

--Environment           -e          Environment name

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

- [Clio Command Reference](../../Commands.md#pull-pkg)
