# switch-nuget-to-dll-reference

Switches nuget references to dll references in csproj files.


## Usage

```bash
clio switch-nuget-to-dll-reference [OPTIONS]
```

## Description

Rewrites project references from NuGet packages to direct DLL references.

## Aliases

`nuget2dll`

## Examples

```bash
clio switch-nuget-to-dll-reference --help
Display canonical options and usage examples
```

## Arguments

```bash
PackageName
    Package name to convert. Required.
```

## Options

```bash
Supports the canonical switch-nuget-to-dll-reference command options.
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

- `ref`

- [Clio Command Reference](../../Commands.md#switch-nuget-to-dll-reference)
