# restore-configuration

Restore the configuration from the last backup.


## Usage

```bash
clio restore-configuration [OPTIONS]
```

## Description

Restores Creatio configuration from the latest available backup package.

## Aliases

`rc`, `restore`

## Examples

```bash
clio restore-configuration -e dev
restore-configuration configuration from backup on the dev environment

clio restore-configuration -d -f -e dev
restore-configuration configuration with relaxed safety checks
```

## Options

```bash
-d
Restore configuration without rollback data

-f
Restore configuration without SQL backward-compatibility check

-e, --Environment <ENVIRONMENT_NAME>
Target environment name
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

## See also

- `restore-configuration`
- `push-pkg`

- [Clio Command Reference](../../Commands.md#restore-configuration)
