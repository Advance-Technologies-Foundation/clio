# push-pkg

Install a package into Creatio.

## Usage

```bash
clio push-pkg [<Name>] [options]
```

## Description

Install a package into Creatio.

## Aliases

`install`, `push`

## Examples

```bash
clio push-pkg [<Name>] [options]
```

## Arguments

```bash
Name
    Package name
```

## Options

```bash
-r, --ReportPath <VALUE>
    Log file path
--InstallSqlScript
    Install sql script
--InstallPackageData
    Install package data
--ContinueIfError
    Continue if error
--SkipConstraints
    Skip constraints
--SkipValidateActions
    Skip validate actions
--ExecuteValidateActions
    Execute validate actions
--IsForceUpdateAllColumns
    Is force update all columns
--id <VALUE>
    Marketplace application id
--force-compilation
    Runs compilation after install package
--skip-backup
    Skip package backup before install when explicitly set to true
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

- [Clio Command Reference](../../Commands.md#push-pkg)
