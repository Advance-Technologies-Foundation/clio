# add-package

## Name

add-package - Add package to workspace or local folder

## Description

Adds a package to the current workspace or local application structure.
Refer to Commands.md for detailed scenarios and examples.

## Synopsis

```bash
clio add-package <PACKAGE_NAME> [OPTIONS]
```

## Options

```bash
<PACKAGE_NAME>
Package name to add

-a
Controls whether an app-descriptor should be created or updated

-e, --Environment <ENVIRONMENT_NAME>
Optional environment list used for download-configuration scenarios
```

## Examples

```bash
clio add-package MyPackage -a true
Add a package and update app descriptor metadata

clio add-package MyPackage -a true -e env_nf,env_n8
Add a package and download configuration from multiple environments
```

## See Also

new-pkg - Create a package skeleton
download-configuration - Download configuration binaries for workspace use

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#add-package)
