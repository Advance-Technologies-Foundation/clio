# compile-package

## Command Type

    Development commands

## Name

compile-package - compile one or more Creatio packages on a target environment

## Aliases

comp-pkg

## Description

The compile-package command recompiles one or more packages in a Creatio
environment. It invokes the remote package rebuild endpoint for each package
name provided and prints start/end progress messages for every package.

You can pass a single package name or a comma-separated list of package names
as the first positional argument.

## Synopsis

```bash
clio compile-package <PACKAGE_NAME>[,<PACKAGE_NAME>...] [OPTIONS]
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

## Examples

```bash
clio compile-package MyPackage -e dev
Rebuilds the MyPackage package in the dev environment

clio compile-package MyPackage -e test
Rebuilds a package using the short alias

clio compile-package PkgOne,PkgTwo -e production
Rebuilds two packages sequentially in the production environment
```

## Output

    For each package the command prints:
    - Start rebuild packages (<PACKAGE_NAME>)
    - End rebuild packages (<PACKAGE_NAME>)
    - Done

## Prerequisites

- Valid Creatio environment configured with -e or direct connection options
- Credentials with permission to compile packages in the target environment
- Network connectivity to the target Creatio instance

## Notes

- The command performs rebuild, not incremental build
- Package names are split by comma before execution
- When one package compilation fails, the command exits with code 1

## Return Values

    0       Package compilation completed successfully
    1       Package compilation failed or an error occurred

## See Also

compile-configuration   Compile all configuration
download-configuration  Download configuration to local workspace
push-pkg                Install package to environment

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#compile-package)
