# open-web-app

Open a registered Creatio environment in the browser.


## Usage

```bash
clio open-web-app [options]
clio open-web-app <ENVIRONMENT_NAME>
```

## Description

Opens a registered Creatio environment in your default web browser.
The command uses the stored environment settings from 'reg-web-app'
and navigates to the Creatio simple login page.

Works cross-platform on Windows, macOS, and Linux.

## Aliases

`open`

## Examples

```bash
clio open-web-app
opens the active environment in default web browser

clio open-web-app my-dev-env
opens environment named 'my-dev-env' in default web browser

clio open-web-app -e production
opens the production environment in default web browser
```

## Arguments

```bash
EnvironmentName
    Environment name
```

## Options

```bash
--Environment           -e          Environment name to open
(uses stored environment settings from reg-web-app)
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

## Notes

- The environment must be registered using 'reg-web-app' command first
- Uses the stored environment URI from configuration
- Opens browser to: {environment-uri}/Shell/?simplelogin=true
- If environment URL is empty or invalid, an error message will be displayed
- User must login manually after browser opens

## Command Type

    Environment Management

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#open-web-app)
