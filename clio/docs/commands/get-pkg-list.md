# get-pkg-list

List packages in a Creatio environment.


## Usage

```bash
get-pkg-list [OPTIONS]
```

## Description

Retrieves and displays a list of all packages installed in the specified
Creatio environment. The command returns package information including name,
version, and maintainer for each installed package.

The command can filter results by package name and return data in either
table format (default) or JSON format for programmatic use.

This command is useful for:
- Auditing installed packages in an environment
- Finding specific packages by name
- Exporting package lists for documentation or comparison
- Integration with CI/CD pipelines and automation scripts

## Aliases

`packages`

## Examples

```bash
# Get all packages from registered environment
clio get-pkg-list -e MyEnvironment

# Get packages with "clio" in the name
clio get-pkg-list -e MyEnvironment -f clio

# Get packages in JSON format for automation
clio get-pkg-list -e MyEnvironment -j

# Filter and return as JSON
clio get-pkg-list -e MyEnvironment -f Custom -j

# Using short form alias
clio get-pkg-list -e MyEnvironment
```

## Arguments

```bash
EnvironmentName
    Application name
```

## Options

```bash
-e, --Environment       Environment name from the registered configuration
The environment must be registered using
'reg-web-app' command

-f, --Filter           Filter packages by name (case-insensitive partial match)
Shows only packages containing the specified text

-j, --Json             Return results in JSON format
Default: false (returns formatted table)

Standard environment options are also available:
-u, --uri              Application URI
-l, --Login            User login (administrator permission required)
-p, --Password         User password
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

Table format (default):
Name                Version         Maintainer
──────────────────────────────────────────────
PackageName1        1.0.0          Company
PackageName2        2.1.3          Developer

JSON format (-j flag):
Returns an array of package objects with detailed information

## Command Type

    Package Management commands

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

## See also

- `pull`

- [Clio Command Reference](../../Commands.md#get-pkg-list)
