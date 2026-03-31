# create-workspace

Create a local workspace.

## Usage

```bash
clio create-workspace [<WorkspaceName>] [options]
```

## Description

Create a local workspace.

## Aliases

`createw`

## Examples

```bash
clio create-workspace [<WorkspaceName>] [options]
```

## Arguments

```bash
WorkspaceName
    Workspace folder name (used with --empty)
```

## Options

```bash
--empty
    Create workspace in a new subfolder without connecting to any environment
--directory <VALUE>
    Absolute base directory for --empty workspace creation. Falls back to
    appsettings 'workspaces-root' when omitted
--IsNugetRestore
    True if you need to restore nugget package SDK. Default: True.
--IsCreateSolution
    True if you need to create the Solution. Default: True.
-a, --AppCode <VALUE>
    Application code
--AddBuildProps
    Add build props for dll paths in the project file. Default: True.
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

- [Clio Command Reference](../../Commands.md#create-workspace)
