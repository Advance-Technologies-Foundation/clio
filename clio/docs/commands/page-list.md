# page-list

List Freedom UI pages.


## Usage

```bash
clio page-list [options]
```

## Description

The page-list command queries SysSchema for Freedom UI client schemas and
returns a JSON envelope with the matching page names, schema UIds,
package names, and parent schema names. Use this command to discover
candidate schema names before calling page-get.

## Examples

```bash
clio page-list -e dev
list Freedom UI pages from the registered dev environment

clio page-list --search-pattern FormPage --limit 20 -e dev
list up to 20 Freedom UI pages whose schema names contain FormPage

clio page-list --package-name UsrApp -u https://my-creatio -l Supervisor -p Supervisor
list Freedom UI pages from the UsrApp package using direct connection arguments
```

## Options

```bash
--package-name                     Optional package name filter

--search-pattern                  Optional schema name contains filter

--limit                           Maximum number of results. Default: 50

--uri                    -u       Application uri

--Password               -p       User password

--Login                  -l       User login (administrator permission required)

--Environment            -e       Environment name

--Maintainer             -m       Maintainer name
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

## Command Type

    Development commands

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#page-list)
