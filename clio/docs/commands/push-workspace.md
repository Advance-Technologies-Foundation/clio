# push-workspace

Push a workspace to the selected environment.

## Synopsis

```bash
clio push-workspace [OPTIONS]
```

## Description

Packages the current workspace and installs it into the target Creatio environment.

## Common options

- `-e`, `--Environment` - Target environment name
- `--skip-backup true` - Skip backup creation only when explicitly requested

## Examples

```bash
clio push-workspace -e dev
clio push-workspace -e dev --skip-backup true
```

## See also

- [restore-workspace](./restore-workspace.md)
- [Commands.md](../../Commands.md#push-workspace)
