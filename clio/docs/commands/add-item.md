# add-item

Generate package item models from Creatio metadata.

## Usage

```bash
clio add-item <Item type> [<Item name>] [options]
```

## Description

Generate package item models from Creatio metadata.

## Aliases

`create`

## Examples

```bash
clio add-item <Item type> [<Item name>] [options]
```

## Arguments

```bash
Item type
    Item type. Required.
Item name
    Item name
```

## Options

```bash
-d, --DestinationPath <VALUE>
    Path to source directory
-n, --Namespace <VALUE>
    Name space for service classes
-f, --Fields <VALUE>
    Required fields for model class
-a, --All
    Create all models. Default: True.
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

## Requirements

- cliogate must be installed when generating models from a Creatio environment.

- [Clio Command Reference](../../Commands.md#add-item)
