# link-from-repository

Link repository package(s) to environment.


## Usage

```bash
clio link-from-repository [options]
```

## Description

Links workspace package content into a local Creatio package directory.

You can provide either:
- -e / --Environment (registered environment)
- --envPkgPath (direct path to the target package folder)

Environment-name resolution works on Windows, macOS, and Linux when the registered
environment has EnvironmentPath configured and the local package folder exists under it.
On Windows, clio also falls back to IIS/URL discovery for older registrations.
The --envPkgPath value may be absolute or relative to the current working directory.

## Aliases

`l4r`, `link4repo`

## Examples

```bash
clio link-from-repository -e MyEnvironment --repoPath ./packages --packages "*"

clio link-from-repository --envPkgPath "/path/to/Creatio/Terrasoft.Configuration/Pkg" --repoPath ./packages --packages "PkgA,PkgB"
```

## Options

```bash
--envPkgPath <VALUE>
Path to environment package folder
({LOCAL_CREATIO_PATH}Terrasoft.WebApp\Terrasoft.Configuration\Pkg)
--packages <VALUE>
Package(s)
--repoPath <VALUE>
Path to package repository folder. Required.
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

    Development commands

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#link-from-repository)
