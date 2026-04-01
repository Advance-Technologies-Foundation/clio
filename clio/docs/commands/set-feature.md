# set-feature

## Command Type

    Service commands

## Name

set-feature - Set feature state

## Description

set-feature command set feature state.
set-feature command can be used in CI/CD pipeline or in development
when you need create or update feature state on web application (website).

## Options

```bash
Code (pos. 0)    Feature code

State (pos. 1)   Feature state
```

## Example

```bash
set-feature ExampleCode 1 enable feature with code ExampleCode for all users, if feature doesn`t exists it will be created
set-feature ExampleCode 0 disable feature with code ExampleCode for all users
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#set-feature)
