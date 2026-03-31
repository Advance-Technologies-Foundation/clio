# add-user-task

Create a user task schema in a workspace package.

## Usage

```bash
clio add-user-task <Code> [options]
```

## Description

Create a user task schema in a workspace package.

## Examples

```bash
clio add-user-task <Code> [options]
```

## Arguments

```bash
Code
    User task code (schema/class name). Must start with Usr. Required.
```

## Options

```bash
--package <VALUE>
    Package name. Required.
-t, --title <VALUE>
    Default localized title. Required.
-d, --description <VALUE>
    Default localized description
--culture <VALUE>
    Culture for --title and --description values. Default: en-US.
--title-localization <VALUE>
    Additional title localization in <culture>=<value> format
--description-localization <VALUE>
    Additional description localization in <culture>=<value> format
--parameter <VALUE>
    Parameter definition in
    'code=<name>;title=<caption>;type=<type>[;lookup=<schemaName|schemaUId>][;direction=<In|Out|Variable|0|1|2>][;required=true][;resulting=true][;serializable=true][;copyValue=true][;lazyLoad=true][;containsPerformerId=true]'
    format. Use lookup only when type=Lookup. Separate multiple values with '|'
--parameter-item <VALUE>
    Composite list item definition in
    'parent=<listParameterName>;code=<name>;title=<caption>;type=<type>[;lookup=<schemaName|schemaUId>][;required=true][;resulting=true][;serializable=true][;copyValue=true][;lazyLoad=true][;containsPerformerId=true]'
    format. The parent parameter must be type=Serializable list of composite
    values. Separate multiple values with '|'
--workspace-path <VALUE>
    Workspace path override. Intended for MCP usage
--timeout <NUMBER>
    Request timeout in milliseconds. Default: 100000.
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

- [Clio Command Reference](../../Commands.md#add-user-task)
