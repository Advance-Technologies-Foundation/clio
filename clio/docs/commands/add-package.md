# add-package

Add a package to a workspace or local folder.

## Synopsis

```bash
clio add-package <PACKAGE_NAME> [OPTIONS]
```

## Description

Use `add-package` to add one or more packages to the current workspace structure.
This command can also participate in application descriptor and configuration download flows.

## Common options

- `<PACKAGE_NAME>` - Package name to add
- `-a` - Enable or disable app descriptor update/creation
- `-e`, `--Environment` - Optional environment list for related configuration scenarios

## Examples

```bash
clio add-package MyPackage -a true
clio add-package MyPackage -a true -e env_nf,env_n8
```

## See also

- [download-configuration](./download-configuration.md)
- [Commands.md](../../Commands.md#add-package)
