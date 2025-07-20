# execute-sql-script

## Summary
sql-script - Execute SQL script on a web application

## Description
Executes custom SQL script on a web application.

## Aliases
None

## Options

| Name | Short | Description |
|------|-------|-------------|
| `Value (pos. 0)` | `` | Sql script |
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
| `--File` | `-f` | Path to the sql script file |
| `` | `` |  |
| `--View` | `-v` | View type |
| `` | `` |  |
| `--DestinationPath` | `-d` | Path to results file |
| `` | `` |  |

## Examples

```bash
execute-sql-script "SELECT Id FROM SysSettings WHERE Code = 'CustomPackageId'"
```