# push-nuget-pkg

Push a NuGet package to a feed.

## Usage

```bash
clio push-nuget-pkg <NugetPkgPath> [options]
```

## Description

Push a NuGet package to a feed.

## Aliases

`push-n`, `push-nuget`

## Examples

```bash
clio push-nuget-pkg <NugetPkgPath> [options]
```

## Arguments

```bash
NugetPkgPath
    Nuget package file path. Required.
```

## Options

```bash
-k, --ApiKey <VALUE>
    The API key for the server. Required.
-s, --Source <VALUE>
    Specifies the server URL. Required.
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

- [Clio Command Reference](../../Commands.md#push-nuget-pkg)
