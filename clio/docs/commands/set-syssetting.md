# set-syssetting

## Command Type

    Service commands

## Name

set-syssetting - Set setting value

## Description

set-syssetting command set setting value.
set-syssetting command can be used in CI/CD pipeline or in development
when you need create or update settings on web application (website).

## Options

```bash
Code (pos. 0)    Sys setting code

Value (pos. 1)   Sys setting Value

Type (pos. 2)    Sys setting Type
```

## Example

```bash
set-syssetting ExampleCode True Boolean - create boolean sys setting with code ExampleCode and value True
set-syssetting Maintainer ATF - update Maintainer sys setting with value ATF
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#set-syssetting)
