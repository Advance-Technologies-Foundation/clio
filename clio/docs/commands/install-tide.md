# install-tide

Install T.I.D.E. for the current environment.


## Usage

```bash
clio install-tide [<EnvironmentName>] [options]
```

## Description

Installs the T.I.D.E. extension on the selected Creatio environment.
T.I.D.E. enables isolated development environments and workspace-based workflows
with Git synchronization capabilities.

The command performs the following steps:
1. Installs cliogate package (if not already installed)
2. Waits for the server to become ready
3. Installs the T.I.D.E. NuGet package (atftide)

This extension is required for advanced workspace development features.

## Aliases

`itide`, `tide`

## Examples

```bash
clio install-tide -e <ENVIRONMENT NAME>

clio install-tide -e demo

clio install-tide -e production
```

## Arguments

```bash
EnvironmentName
    Application name
```

## Options

```bash
-e, --environment <ENVIRONMENT_NAME>
The target Creatio environment name (required)
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

## Requirements

- Creatio instance must be accessible
- Valid credentials for the target environment
- Sufficient permissions to install packages

## Command Type

    Service commands

## Related Commands

    install-gate - installs cliogate package
    push-workspace - push workspace to environment
    git-sync - synchronize environment with Git repository

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#install-tide)
