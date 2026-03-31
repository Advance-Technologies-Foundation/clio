# create-data-binding

Create or regenerate a package data binding.

## Usage

```bash
clio create-data-binding [options]
```

## Description

Create or regenerate a package data binding.

## Examples

```bash
clio create-data-binding -e dev
```

## Options

```bash
--environment <VALUE>
    Environment name
--package <VALUE>
    Target package name. Required.
--schema <VALUE>
    Entity schema name. Required.
--binding-name <VALUE>
    Binding folder name
--install-type <NUMBER>
    Descriptor install type. Default: 0.
--values <VALUE>
    Row values as JSON object keyed by column name. Lookup and image-reference
    columns may use {"value":"...","displayValue":"..."}; if displayValue is
    omitted, create-data-binding resolves it from Creatio when runtime lookup data
    is available. Image content columns accept either a base64 string or a local
    file path to encode
--localizations <VALUE>
    Localized values as JSON object keyed by culture and column name
--workspace-path <VALUE>
    Workspace root path. Defaults to the current workspace
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

## See also

- `add-data-binding-row`
- `remove-data-binding-row`
- `call-service`

- [Clio Command Reference](../../Commands.md#create-data-binding)
