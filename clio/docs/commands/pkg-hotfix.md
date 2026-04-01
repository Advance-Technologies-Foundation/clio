# pkg-hotfix

Enable/disable hotfix state for package.


## Usage

```bash
clio pkg-hotfix <PACKAGE_NAME> <true|false> [OPTIONS]
```

## Description

Changes the hotfix mode state for a package in the selected environment.

## Aliases

`hf`, `hotfix`

## Examples

```bash
clio pkg-hotfix MyPackage true -e dev
Enable hotfix mode for a package

clio pkg-hotfix MyPackage false -e dev
Disable hotfix mode for a package
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

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

## See also

- `activate`
- `deactivate`

- [Clio Command Reference](../../Commands.md#pkg-hotfix)
