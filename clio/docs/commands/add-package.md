# add-package

Add package to workspace or local folder.


## Usage

```bash
clio add-package <PACKAGE_NAME> [OPTIONS]
```

## Description

Adds a package to the current workspace or local application structure.
Refer to Commands.md for detailed scenarios and examples.

## Aliases

`ap`

## Examples

```bash
clio add-package MyPackage -a true
Add a package and update app descriptor metadata

clio add-package MyPackage -a true -e env_nf,env_n8
Add a package and download configuration from multiple environments
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

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

## See also

- `new`
- `pull-pkg`

- [Clio Command Reference](../../Commands.md#add-package)
