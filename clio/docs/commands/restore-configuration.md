# restore-configuration

## Name

restore-configuration - Restore configuration from last backup

## Description

Restores Creatio configuration from the latest available backup package.

## Synopsis

```bash
clio restore-configuration [OPTIONS]
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

## Examples

```bash
clio restore-configuration -e dev
restore-configuration configuration from backup on the dev environment

clio restore-configuration -d -f -e dev
restore-configuration configuration with relaxed safety checks
```

## See Also

restore-db - Restore the database from backup
push-pkg - Install packages that may create backup data

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#restore-configuration)
