# push-pkg

## Command Type

    CI/CD commands

## Name

push-pkg - Install package from directory you can use the next command:
for non compressed package in current folder

## Description

push-pkg command can be used in CI/CD pipeline or in development
when you need install package to a web application (website).

## Synopsis

```bash
clio push-pkg <PACKAGE_NAME>
```

## Options

```bash
Package name (pos. 0) Name/path of package folder or path for zip or gz
package file

--uri                   -u          Application uri

--Password              -p          User password

--Login                 -l          User login (administrator permission
required)

--Environment           -e          Environment name

--Maintainer            -m          Maintainer name

--skip-backup                       Skip package backup before install only
when explicitly set to true
```

## Example

```bash
clio push-pkg <PACKAGE_NAME>
push-pkg package from directory you can use the next command: for non
compressed package
in current folder

clio push-pkg package.gz
push-pkg package from .gz packages you can use command

clio push-pkg package.gz --InstallSqlScript false --InstallPackageData false
--ContinueIfError true --SkipConstraints false --SkipValidateActions false
--ExecuteValidateActions false --IsForceUpdateAllColumns false
push-pkg package from .gz packages, with options, you can use command

clio push-pkg C:\Packages\package.gz
push-pkg package from .gz packages you can use command

clio push-pkg <PACKAGE_NAME> -r log.txt
installation log file specify report path parameter

clio push-pkg <PACKAGE_NAME> --skip-backup true
push-pkg package without creating backup first; omitted option keeps
the existing backup behavior
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#push-pkg)
