# delete-pkg-remote

Delete a package from Creatio.


## Usage

```bash
clio delete-pkg-remote <PACKAGE_NAME>
```

## Description

delete-pkg-remote command can be used in CI/CD pipeline or in development
when you need delete package from a web application (website).

## Aliases

`delete`

## Examples

```bash
clio delete-pkg-remote <PACKAGE_NAME>
delete-pkg-remote package with <PACKAGE_NAME> from default application

clio delete-pkg-remote <PACKAGE_NAME> -e dev
delete-pkg-remote package with <PACKAGE_NAME> from specify application with name dev
```

## Options

```bash
Package name (pos. 0)	Name/path of package folder or path for zip or gz package file

--uri                   -u          Application uri

--Password              -p          User password

--Login                 -l          User login (administrator permission required)

--Environment           -e          Environment name

--Maintainer            -m          Maintainer name
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#delete-pkg-remote)
