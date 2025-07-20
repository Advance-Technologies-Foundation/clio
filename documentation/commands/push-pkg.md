# push-pkg

## Summary
pkg - Install package from directory you can use the next command:

## Description
push-pkg command can be used in CI/CD pipeline or in development
    when you need install package to a web application (website).

## Aliases
None

## Options

| Name | Short | Description |
|------|-------|-------------|
| `Package name (pos. 0) Name/path of package folder or path for zip or gz` | `` |  |
| `package file` | `` |  |
| `` | `` |  |
| `--uri` | `-u` | Application uri |
| `` | `` |  |
| `--Password` | `-p` | User password |
| `` | `` |  |
| `--Login` | `-l` | User login (administrator permission |
| `required)` | `` |  |
| `` | `` |  |
| `--Environment` | `-e` | Environment name |
| `` | `` |  |
| `--Maintainer` | `-m` | Maintainer name |
| `` | `` |  |

## Examples

```bash
clio push-pkg <PACKAGE_NAME>
        install package from directory you can use the next command: for non
        compressed package
        in current folder

    clio push-pkg package.gz
        install package from .gz packages you can use command

    clio push-pkg package.gz --InstallSqlScript false --InstallPackageData false
            --ContinueIfError true --SkipConstraints false --SkipValidateActions false
            --ExecuteValidateActions false --IsForceUpdateAllColumns false
        install package from .gz packages, with options, you can use command

    clio push-pkg C:\Packages\package.gz
        install package from .gz packages you can use command

    clio push-pkg <PACKAGE_NAME> -r log.txt
        installation log file specify report path parameter
```