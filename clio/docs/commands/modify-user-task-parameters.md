# modify-user-task-parameters

Add or remove parameters in a user task schema.

## Usage

```bash
clio modify-user-task-parameters <UserTaskName> [options]
```

## Description

Add or remove parameters in a user task schema.

## Examples

```bash
clio modify-user-task-parameters <UserTaskName> [options]
```

## Arguments

```bash
UserTaskName
    Existing user task schema name. Required.
```

## Options

```bash
--add-parameter <VALUE>
    Parameter definition in
    'code=<name>;title=<caption>;type=<type>[;lookup=<schemaName|schemaUId>][;direction=<In|Out|Variable|0|1|2>][;required=true][;resulting=true][;serializable=true][;copyValue=true][;lazyLoad=true][;containsPerformerId=true]'
    format. Use lookup only when type=Lookup. Separate multiple values with '|'
--add-parameter-item <VALUE>
    Composite list item definition in
    'parent=<listParameterName>;code=<name>;title=<caption>;type=<type>[;lookup=<schemaName|schemaUId>][;required=true][;resulting=true][;serializable=true][;copyValue=true][;lazyLoad=true][;containsPerformerId=true]'
    format. The parent parameter must be type=Serializable list of composite
    values. Separate multiple values with '|'
--remove-parameter <VALUE>
    Parameter name to remove. Separate multiple values with '|'
--set-direction <VALUE>
    Set direction for an existing parameter in '<name>=<In|Out|Variable|0|1|2>'
    format. Separate multiple values with '|'
--culture <VALUE>
    Culture for added parameter titles. Default: en-US.
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

## See also

- `add-user-task`
- `delete-schema`

- [Clio Command Reference](../../Commands.md#modify-user-task-parameters)
