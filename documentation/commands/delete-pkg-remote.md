# delete-pkg-remote

## Summary
pkg-remote - delete package from application

## Description
delete-pkg-remote command can be used in CI/CD pipeline or in development
    when you need delete package from a web application (website).

## Aliases
None

## Options

| Name | Short | Description |
|------|-------|-------------|
| `Package name (pos. 0)	Name/path of package folder or path for zip or gz package file` | `` |  |
| `` | `` |  |
| `--uri` | `-u` | Application uri |
| `` | `` |  |
| `--Password` | `-p` | User password |
| `` | `` |  |
| `--Login` | `-l` | User login (administrator permission required) |
| `` | `` |  |
| `--Environment` | `-e` | Environment name |
| `` | `` |  |
| `--Maintainer` | `-m` | Maintainer name |
| `` | `` |  |

## Examples

```bash
clio delete-pkg-remote <PACKAGE_NAME>
        delete package with <PACKAGE_NAME> from default application

    clio delete-pkg-remote <PACKAGE_NAME> -e dev
        delete package with <PACKAGE_NAME> from specify application with name dev
```