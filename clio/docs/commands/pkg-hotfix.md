# pkg-hotfix

## Name

pkg-hotfix - Enable or disable package hotfix state

## Description

Changes the hotfix mode state for a package in the selected environment.

## Synopsis

```bash
clio pkg-hotfix <PACKAGE_NAME> <true|false> [OPTIONS]
```

## Options

```bash
<PACKAGE_NAME>
Package name to update

<true|false>
Desired hotfix mode state

-e, --Environment <ENVIRONMENT_NAME>
Target environment name
```

## Examples

```bash
clio pkg-hotfix MyPackage true -e dev
Enable hotfix mode for a package

clio pkg-hotfix MyPackage false -e dev
Disable hotfix mode for a package
```

## See Also

activate-pkg - Activate a package
deactivate-pkg - Deactivate a package

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#pkg-hotfix)
