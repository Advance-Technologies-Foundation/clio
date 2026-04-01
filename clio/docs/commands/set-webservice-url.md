# set-webservice-url

Set a base URL for a registered web service.


## Usage

```bash
clio set-webservice-url <SERVICE_NAME> <URL> [OPTIONS]
```

## Description

Stores or updates the base URL assigned to a configured web service.

## Aliases

`swu`, `webservice`

## Examples

```bash
clio set-webservice-url CustomerApi https://api.example.com -e dev
Set a service URL in the dev environment
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
<SERVICE_NAME>
Web service name to update

<URL>
New base URL for the service

-e, --Environment <ENVIRONMENT_NAME>
Target environment name
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

- `get`

- [Clio Command Reference](../../Commands.md#set-webservice-url)
