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

## Exit Codes

| Code | Meaning |
|---|---|
| `0` | The installation finished and nothing else in the run failed. Schemas the platform skipped because they were modified on the environment are reported as `[WAR]` warnings and do not fail the command &mdash; the package itself was installed. |
| `1` | The installation actually failed, or clio could not reach the environment. The reason the platform reported and the package that failed are both named on the closing lines. A run that skipped a locally modified schema **and** failed to compile also lands here: `--continue-if-error` is on by default, so a single archive can carry both, and the closing line then names the compiler diagnostic instead of the platform's generic message. |

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#push-pkg)
