# push-pkg

Install a package into Creatio.


## Usage

```bash
clio push-pkg <PACKAGE_NAME>
```

## Description

push-pkg command can be used in CI/CD pipeline or in development
when you need install package to a web application (website).

## Aliases

`install`, `push`

## Examples

```bash
clio push-pkg <PACKAGE_NAME>
push-pkg package from directory you can use the next command: for non
compressed package
in current folder

clio push-pkg package.gz
push-pkg package from .gz packages you can use command

clio push-pkg package.gz --InstallSqlScript false --InstallPackageData false
--ContinueIfError true --SkipConstraints false --SkipValidateActions false
--ExecuteValidateActions false --IsForceUpdateAllColumns false
push-pkg package from .gz packages, with options, you can use command

clio push-pkg C:\Packages\package.gz
push-pkg package from .gz packages you can use command

clio push-pkg <PACKAGE_NAME> -r log.txt
installation log file specify report path parameter

clio push-pkg <PACKAGE_NAME> --skip-backup true
push-pkg package without creating backup first; omitted option keeps
the existing backup behavior
```

## Arguments

```bash
Name
    Package name
```

## Options

```bash
Package name (pos. 0) Name/path of package folder or path for zip or gz
package file

--uri                   -u          Application uri

--Password              -p          User password

--Login                 -l          User login (administrator permission
required)

--Environment           -e          Environment name

--Maintainer            -m          Maintainer name

--skip-backup                       Skip package backup before install only
when explicitly set to true
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

- [Clio Command Reference](../../Commands.md#push-pkg)
