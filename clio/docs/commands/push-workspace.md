# push-workspace

Push workspace to selected environment.


## Usage

```bash
clio push-workspace [OPTIONS]
```

## Description

Packs the current workspace and installs it into the target environment.

## Aliases

`pushw`

## Examples

```bash
clio push-workspace -e dev
Push the current workspace to the dev environment

clio push-workspace -e dev --skip-backup true
Push the workspace without creating a backup package first
```

## Options

```bash
-e, --Environment <ENVIRONMENT_NAME>
Target environment name

--skip-backup <true|false>
Skip backup creation only when explicitly set to true
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

## See also

- `add-item`
- `restore-configuration`

- [Clio Command Reference](../../Commands.md#push-workspace)
