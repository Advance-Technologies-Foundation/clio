# generate-process-model

Generate process model for ATF.Repository.

## Usage

```bash
clio generate-process-model <Code> [options]
```

## Description

Generate process model for ATF.Repository.

## Aliases

`gpm`

## Examples

```bash
clio generate-process-model <Code> [options]
```

## Arguments

```bash
Code
    Process code as it appears in the process designer. Required.
```

## Options

```bash
-d, --DestinationPath <VALUE>
    Destination folder or explicit .cs file path for the generated process model.
    Default: ..
-n, --Namespace <VALUE>
    Namespace for generated process model classes. Default: AtfTIDE.ProcessModels.
-x, --Culture <VALUE>
    Description culture. Default: en-US.
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

- [Clio Command Reference](../../Commands.md#generate-process-model)
