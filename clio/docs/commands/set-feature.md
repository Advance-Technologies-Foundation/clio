# set-feature

Set feature state.


## Usage

```bash
clio set-feature <Code> <State> [<onlyCurrentUser>] [options]
```

## Description

set-feature command set feature state.
set-feature command can be used in CI/CD pipeline or in development
when you need create or update feature state on web application (website).

## Aliases

`feature`

## Examples

```bash
set-feature ExampleCode 1 enable feature with code ExampleCode for all users, if feature doesn`t exists it will be created
set-feature ExampleCode 0 disable feature with code ExampleCode for all users
```

## Options

```bash
Code (pos. 0)    Feature code

State (pos. 1)   Feature state
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#set-feature)
