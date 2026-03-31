# set-webservice-url

Set a base URL for a registered web service.

## Usage

```bash
clio set-webservice-url <WebServiceName> <baseurl> [options]
```

## Description

Set a base URL for a registered web service.

## Aliases

`swu`, `webservice`

## Examples

```bash
clio set-webservice-url <WebServiceName> <baseurl> [options]
```

## Arguments

```bash
WebServiceName
    Web service name. Required.
baseurl
    Base url of a web service. Required.
```

## Options

```bash
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

- [Clio Command Reference](../../Commands.md#set-webservice-url)
