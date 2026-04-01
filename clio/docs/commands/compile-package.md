# compile-package

Compile one or more packages in Creatio.


## Usage

```bash
clio compile-package <PACKAGE_NAME>[,<PACKAGE_NAME>...] [OPTIONS]
```

## Description

The compile-package command recompiles one or more packages in a Creatio
environment. It invokes the remote package rebuild endpoint for each package
name provided and prints start/end progress messages for every package.

You can pass a single package name or a comma-separated list of package names
as the first positional argument.

## Aliases

`comp-pkg`

## Examples

```bash
clio compile-package MyPackage -e dev
Rebuilds the MyPackage package in the dev environment

clio compile-package MyPackage -e test
Rebuilds a package using the short alias

clio compile-package PkgOne,PkgTwo -e production
Rebuilds two packages sequentially in the production environment
```

## Arguments

```bash
    <PACKAGE_NAME>[,<PACKAGE_NAME>...]
        Required. Package name or comma-separated package names to compile.
```

## Options

```bash
--uri                   -u          Application uri

--Password              -p          User password

--Login                 -l          User login (administrator permission required)

--Environment           -e          Environment name

--Maintainer            -m          Maintainer name

--clientId                          OAuth client id

--clientSecret                      OAuth client secret

--authAppUri                        OAuth app URI
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

- Valid Creatio environment configured with -e or direct connection options
- Credentials with permission to compile packages in the target environment
- Network connectivity to the target Creatio instance

## Notes

- The command performs rebuild, not incremental build
- Package names are split by comma before execution
- When one package compilation fails, the command exits with code 1

## Command Type

    Development commands

## Output

    For each package the command prints:
    - Start rebuild packages (<PACKAGE_NAME>)
    - End rebuild packages (<PACKAGE_NAME>)
    - Done

## Return Values

    0       Package compilation completed successfully
    1       Package compilation failed or an error occurred

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

## See also

- `build-workspace`
- `pull-pkg`
- `push-pkg`

- [Clio Command Reference](../../Commands.md#compile-package)
