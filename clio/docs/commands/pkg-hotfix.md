# pkg-hotfix

Enable/disable hotfix state for package.


## Usage

```bash
clio pkg-hotfix <PACKAGE_NAME> <true|false> [OPTIONS]
```

## Description

Changes the hotfix mode state for a package in the selected environment.

## Aliases

`hf`, `hotfix`

## Examples

```bash
clio pkg-hotfix MyPackage true -e dev
Enable hotfix mode for a package

clio pkg-hotfix MyPackage false -e dev
Disable hotfix mode for a package
```

## Arguments

```bash
PackageName
    Package name. Required.
HotFixState
    HotFix state. Required.
```

## Options

```bash
<PACKAGE_NAME>
Package name to update

<true|false>
Desired hotfix mode state

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

- `activate`
- `deactivate`

- [Clio Command Reference](../../Commands.md#pkg-hotfix)
