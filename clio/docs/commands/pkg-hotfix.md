# pkg-hotfix

Enable or disable hotfix mode for a package.

## Synopsis

```bash
clio pkg-hotfix <PACKAGE_NAME> <true|false> [OPTIONS]
```

## Description

`pkg-hotfix` changes the hotfix mode state of a package in the selected environment.

## Examples

```bash
clio pkg-hotfix MyPackage true -e dev
clio pkg-hotfix MyPackage false -e dev
```

## See also

- [Commands.md](../../Commands.md#pkg-hotfix)
