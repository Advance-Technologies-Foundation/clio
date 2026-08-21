# run

Run all commands declared in a YAML scenario in one clio process.

Before every environment-dependent step, `run` refreshes `appsettings.json` and resolves the requested
environment again. This allows a scenario to deploy and register a Creatio environment in one step and use
that environment in the next step. If the environment is still missing, the step fails instead of falling
back to `http://localhost`. A step must identify its target with either a named environment or direct
application/authentication URIs, not both.


## Usage

```bash
clio run [options]
```

## Description

Run scenario.

## Aliases

`run-scenario`, `scenario`

## Examples

```bash
clio run --file-name ./Phase1.yaml
clio run --file-name ./Phase1.yaml -e dev
```

## Options

```bash
--file-name <VALUE>
Scenario file name. Required.
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
--restart-environment
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

- [Clio Command Reference](../../Commands.md#run)
