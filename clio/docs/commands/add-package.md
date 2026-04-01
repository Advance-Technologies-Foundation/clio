# add-package

Add package to workspace or local folder.


## Usage

```bash
clio add-package <PACKAGE_NAME> [OPTIONS]
```

## Description

Adds a package to the current workspace or local application structure.
Refer to Commands.md for detailed scenarios and examples.

## Aliases

`ap`

## Examples

```bash
clio add-package MyPackage -a true
Add a package and update app descriptor metadata

clio add-package MyPackage -a true -e env_nf,env_n8
Add a package and download configuration from multiple environments
```

## Arguments

```bash
Name
    Package name. Required.
```

## Options

```bash
<PACKAGE_NAME>
Package name to add

-a
Controls whether an app-descriptor should be created or updated

-e, --Environment <ENVIRONMENT_NAME>
Optional environment list used for download-configuration scenarios
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

- `new`
- `pull-pkg`

- [Clio Command Reference](../../Commands.md#add-package)
