# lock-package

Lock a package in Creatio.


## Usage

```bash
clio lock-package [package-names] [options]
clio lock-package [package-names] [options]
```

## Description

lock-package - Lock packages in Creatio environment

## Aliases

`lp`

## Examples

```bash
Lock a single package:
clio lock-package MyPackage -e dev
clio lock-package MyPackage -e dev

Lock multiple packages:
clio lock-package Package1,Package2,Package3 -e dev

Lock all packages:
clio lock-package -e dev

Complete development cycle:
clio unlock-package MyPackage -e dev
# Make changes...
clio push-workspace -e dev
clio lock-package MyPackage -e dev

Using direct authentication:
clio lock-package MyPackage --uri https://myapp.com -l admin -p pass
```

## Arguments

```bash
    package-names
        Comma-separated list of package names to lock. If omitted, locks
        all packages in the environment.
        Example: MyPackage,AnotherPackage,ThirdPackage
```

## Options

```bash
-e, --environment <NAME>
Environment name from configuration (recommended)

-u, --uri <URI>
Creatio application URI (alternative to environment)

-l, --login <LOGIN>
Username for authentication

-p, --password <PASSWORD>
Password for authentication

--clientid <ID>
OAuth Client ID (alternative authentication)

--clientsecret <SECRET>
OAuth Client Secret (alternative authentication)

--authappuri <URI>
OAuth Authentication App URI (alternative authentication)
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

This command requires cliogate package version 2.0.0.0 or higher installed
on the target Creatio environment.

To install cliogate:
clio install-gate -e <ENVIRONMENT_NAME>

To check cliogate version:
clio get-info -e <ENVIRONMENT_NAME>

## Notes

- Package names are case-sensitive and must match exactly
- Use environment configuration for easier command usage
- Always lock packages after completing development work
- Administrator permissions required on target environment
- Include package locking as final step in deployment pipelines

## See also

- `set-dev-mode`
- `push-pkg`
- `push-pkg`
- `get`

- [Clio Command Reference](../../Commands.md#lock-package)
