# install-application

Install an application package into Creatio.


## Usage

```bash
clio install-application <NAME> [options]
```

## Description

The install-application command installs an application package file into
a Creatio environment. It supports registered environments and direct
connection arguments inherited from EnvironmentOptions.

## Aliases

`install-app`, `push-app`

## Examples

```bash
clio install-application C:\Packages\application.gz -e dev
install an application package into the registered dev environment

clio install-application C:\Packages\application.gz --check-compilation-errors true -e dev
install an application package and stop when compilation errors are detected

clio install-application C:\Packages\application.gz -r install.log -u https://my-creatio
install an application package and write the command report to install.log
```

## Arguments

```bash
Name
    Package name
```

## Options

```bash
Name (pos. 0)            Application package path or name

--ReportPath             -r          Optional path to the installation log file

--check-compilation-errors           Check compilation errors during installation

--uri                    -u          Application uri

--Password               -p          User password

--Login                  -l          User login (administrator permission required)

--Environment            -e          Environment name

--Maintainer             -m          Maintainer name
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

- [Clio Command Reference](../../Commands.md#install-application)
