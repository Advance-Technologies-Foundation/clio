# run

## Name

run - Run a YAML scenario and refresh environments between dependent steps

## Synopsis

```bash
clio run --file-name <scenario.yaml> [OPTIONS]
```

## Aliases

scenario, run-scenario

## Description

Runs all commands declared in a YAML scenario. Environment-dependent steps
refresh appsettings.json before resolving their target, so a deployment step
can register an environment for following steps in the same process.

A missing environment fails the step instead of falling back to localhost.
A step cannot combine a named environment with direct application or
authentication URIs.

## Options

```bash
--file-name <VALUE>
Scenario file name. Required.
```

## Environment Options

```bash
-u, --uri <VALUE>
Application URI
-p, --Password <VALUE>
User password
-l, --Login <VALUE>
User login (administrator permission required)
-i, --IsNetCore
Use NetCore application
-e, --Environment <VALUE>
Default environment name for steps that omit a target
-m, --Maintainer <VALUE>
Maintainer name
-c, --dev <VALUE>
Developer mode state for environment
--WorkspacePathes <VALUE>
Workspace path
-s, --Safe <VALUE>
Safe action in this environment
--clientId <VALUE>
OAuth client ID
--clientSecret <VALUE>
OAuth client secret
--authAppUri <VALUE>
OAuth app URI
--silent
Use default behavior without user interaction
--restart-environment
Restart environment after command execution
--db-server-uri <VALUE>
Database server URI
--db-user <VALUE>
Database user
--db-password <VALUE>
Database password
--backup-file <VALUE>
Full path to backup file
--db-working-folder <VALUE>
Folder visible to the database server
--db-name <VALUE>
Desired database name
--force
Force restore
--callback-process <VALUE>
Callback process name
--ep <VALUE>
Path to the application root folder
```

## Examples

```bash
Run a provisioning scenario that creates its first environment:
clio run --file-name ./Phase1.yaml

Supply a default environment for steps that omit a target:
clio run --file-name ./Phase1.yaml -e dev
```

## Notes

Scenarios run non-interactively. A step targeting an environment marked Safe
fails closed because the runner cannot request production confirmation.

## See Also

create-workspace - Create a clio workspace
list-environments - List registered environments

- [Clio Command Reference](../../Commands.md#run)
