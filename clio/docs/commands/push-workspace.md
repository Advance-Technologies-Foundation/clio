# push-workspace

## Name

push-workspace - Push workspace to selected environment

## Description

Packs the current workspace and installs it into the target environment.

## Synopsis

```bash
clio push-workspace [OPTIONS]
```

## Options

```bash
-e, --Environment <ENVIRONMENT_NAME>
Target environment name

--skip-backup <true|false>
Skip backup creation only when explicitly set to true
```

## Examples

```bash
clio push-workspace -e dev
Push the current workspace to the dev environment

clio push-workspace -e dev --skip-backup true
Push the workspace without creating a backup package first
```

## See Also

create-workspace - Create a workspace
restore-workspace - Restore a workspace from an environment

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#push-workspace)
