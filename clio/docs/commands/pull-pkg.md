# pull-pkg

Download package from a web application.


## Usage

```bash
clio pull-pkg <PACKAGE_NAME>
```

## Description

pull-pkg command can be used in CI/CD pipeline or in development
when you need download package from a web application (website).

## Aliases

`download`

## Examples

```bash
clio pull-pkg <PACKAGE_NAME>
pull-pkg package to local file system from default application

clio pull-pkg <PACKAGE_NAME> -e <ENVIRONMENT_NAME>
pull-pkg package to local file system from non default application
```

## Options

```bash
Package name (pos. 0)	Name of package for download

--uri                   -u          Application uri

--Password              -p          User password

--Login                 -l          User login (administrator permission required)

--Environment           -e          Environment name

--Maintainer            -m          Maintainer name
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#pull-pkg)
