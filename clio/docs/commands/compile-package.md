# compile-package

Compile one or more Creatio packages on a target environment.

## Synopsis

```bash
clio compile-package <PACKAGE_NAME>[,<PACKAGE_NAME>...] [OPTIONS]
```

**Alias:** `comp-pkg`

## Description

The `compile-package` command recompiles specific packages in a Creatio environment.
It calls the remote package rebuild endpoint for each package name provided and prints
start and end messages for every rebuild operation.

Use this command when you need to recompile selected packages without triggering a full
`compile-configuration` run.

## Arguments

### `<PACKAGE_NAME>[,<PACKAGE_NAME>...]`

Required package name or comma-separated package names to rebuild.

Examples:

- `MyPackage`
- `MyPackage,MyDependentPackage`

## Options

### Environment selection

- `-e`, `--Environment` - Environment name from your clio configuration

### Direct connection options

These can be used instead of `-e`:

- `-u`, `--uri` - Application URI
- `-l`, `--Login` - User login
- `-p`, `--Password` - User password
- `-m`, `--Maintainer` - Maintainer name
- `--clientId` - OAuth client ID
- `--clientSecret` - OAuth client secret
- `--authAppUri` - OAuth application URI

## Examples

Compile a single package in a configured environment:

```bash
clio compile-package MyPackage -e dev
```

Compile with the short alias:

```bash
clio comp-pkg MyPackage -e test
```

Compile multiple packages sequentially:

```bash
clio compile-package PkgOne,PkgTwo -e production
```

Compile using direct connection parameters:

```bash
clio compile-package MyPackage -u https://myapp.creatio.com -l administrator -p password
```

## Behavior

- The command splits the first argument by comma and recompiles packages one by one.
- Internally it uses package rebuild semantics, not incremental package build.
- The command prints `Done` when all requested packages are rebuilt successfully.
- If any exception occurs, the command prints the error message and exits with code `1`.

## Prerequisites

- Valid Creatio environment configuration or direct connection parameters
- Credentials with permission to compile packages in the target environment
- Network connectivity to the target Creatio instance

## Output

Typical output for one package looks like this:

```text
Start rebuild packages (MyPackage).
End rebuild packages (MyPackage).
Done
```

## Return values

- `0` - package compilation completed successfully
- `1` - package compilation failed or another error occurred

## Related commands

- [`compile-configuration`](./CompileConfigurationCommand.md) - Compile the full configuration
- [`download-configuration`](./download-configuration.md) - Download configuration to local workspace
- [`push-pkg`](../../Commands.md#push-pkg) - Install a package to an environment