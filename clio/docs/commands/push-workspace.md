# push-workspace

## Name

push-workspace - Push workspace to selected environment

## Description

Packs the current workspace and installs it into the target environment.

## Prerequisites

cliogate package version 2.0.0.0 or higher must be installed on the Creatio
instance. Install using:

clio install-gate -e <ENVIRONMENT_NAME>

## Synopsis

```bash
clio push-workspace [OPTIONS]
```

## Options

```bash
-e, --environment <ENVIRONMENT_NAME>
Target environment name

--skip-backup <true|false>
Skip backup creation only when explicitly set to true

--unlock
Unlock workspace packages after installing the workspace

--use-application-installer
Use ApplicationInstaller instead of PackageInstaller for installation
```

## Examples

```bash
clio push-workspace -e dev
Push the current workspace to the dev environment

clio push-workspace -e dev --skip-backup true
Push the workspace without creating a backup package first

clio push-workspace -e dev --use-application-installer
Push the workspace using ApplicationInstaller
```

## See Also

create-workspace - Create a workspace
restore-workspace - Restore a workspace from an environment

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#push-workspace)
