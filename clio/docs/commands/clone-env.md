# clone-env

Clone one environment to another.


## Usage

```bash
clio clone-env <SOURCE_ENVIRONMENT> <TARGET_ENVIRONMENT>
```

## Description

Creates a copy of an existing environment definition in clio settings.

## Aliases

`clone`, `clone-environment`

## Examples

```bash
clio clone-env dev test
clone-env the dev environment configuration into test
```

## Arguments

```bash
EnvironmentName
    Application name
```

## Options

```bash
<SOURCE_ENVIRONMENT>
Existing registered environment to copy

<TARGET_ENVIRONMENT>
New environment name to create
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

- `reg-web-app`
- `unreg-web-app`

- [Clio Command Reference](../../Commands.md#clone-env)
