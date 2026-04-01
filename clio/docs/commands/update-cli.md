# update-cli

Update clio.


## Usage

```bash
update-cli [OPTIONS]
update-cli [OPTIONS]
```

## Description

Checks for a newer version of clio on NuGet.org and updates the installation
if an update is available. By default, the command proceeds with the update
automatically without prompting for confirmation.

Use the --prompt option if you want to review the changes before updating.

Recommended to use the latest version of clio for bug fixes and new features.

## Aliases

`update`

## Examples

```bash
# Automatic update (default behavior)
update-cli

# Same as above using alias
update-cli
```

## Options

```bash
-g, --global            Install clio globally (default: true)

-y, --no-prompt         Proceed with update automatically without confirmation
(default behavior)
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

    Application management

## Exit Codes

    0   Successful update or already on latest version
    1   User cancelled or update failed
    2   Error checking for updates (network issue, etc.)

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#update-cli)
