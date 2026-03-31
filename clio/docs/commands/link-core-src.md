# link-core-src

Link core source code to environment for development.

## Usage

```bash
clio link-core-src [<EnvironmentName>] [options]
```

## Description

Link core source code to environment for development.

## Aliases

`lcs`

## Examples

```bash
clio link-core-src [<EnvironmentName>] [options]
```

## Arguments

```bash
EnvironmentName
    Application name
```

## Options

```bash
--core-path <VALUE>
    Path to Creatio core source directory. Required.
--mode <VALUE>
    Creatio mode: NetCore (Terrasoft.WebHost) or NetFramework
    (Terrasoft.WebApp.Loader). Default: NetCore.
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

- [Clio Command Reference](../../Commands.md#link-core-src)
