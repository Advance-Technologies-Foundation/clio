# execute-assembly-code

## Summary
assembly-code - execute class code that implements IExecuter

## Description
execute-assembly-code helps developers execute class code without package
    installation. This feature is useful in application development flow

## Aliases
None

## Options

| Name | Short | Description |
|------|-------|-------------|
| `Name (pos. 0)` | `` | Required. Path to executed assembly |
| `` | `` |  |
| `--ExecutorType` | `-t` | Required. Assembly type name for proceed |
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
| `` | `` |  |

## Examples

```bash
clio execute-assembly-code myassembly.dll -t MyNamespace.CodeExecutor
```