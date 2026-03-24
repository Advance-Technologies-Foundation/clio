# restore-configuration

Restore configuration from the latest backup.

## Synopsis

```bash
clio restore-configuration [OPTIONS]
```

## Description

`restore-configuration` restores Creatio configuration from backup data created during package operations.

## Common options

- `-d` - Restore without rollback data
- `-f` - Restore without SQL backward compatibility check
- `-e`, `--Environment` - Target environment

## Examples

```bash
clio restore-configuration -e dev
clio restore-configuration -d -f -e dev
```

## See also

- [restore-db](./restore-db.md)
- [Commands.md](../../Commands.md#restore-configuration)
