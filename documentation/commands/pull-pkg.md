# pull-pkg

## Summary
pkg - download package to local file system from default a web application

## Description
pull-pkg command can be used in CI/CD pipeline or in development
    when you need download package from a web application (website).

## Aliases
None

## Options

| Name | Short | Description |
|------|-------|-------------|
| `Package name (pos. 0)	Name of package for download` | `` |  |
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
clio pull-pkg <PACKAGE_NAME>
        download package to local file system from default application

    clio pull-pkg <PACKAGE_NAME> -e <ENVIRONMENT_NAME>
        download package to local file system from non default application
```