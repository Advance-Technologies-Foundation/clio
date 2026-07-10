# set-syssetting

## Command Type

    Service commands

## Name

set-syssetting - Get or set a system setting value

Aliases: `get-syssetting`, `ss`, `sys-setting`, `syssetting`

## Description

set-syssetting reads or writes a Creatio system setting value.
It can be used in a CI/CD pipeline or during development when you need to
create, update, or read settings on a web application (website).

- **Read:** use the `get-syssetting` alias, or pass the `--get` flag. A read never modifies the setting.
- **Write:** use `set-syssetting` with a value. A write with **no value is rejected** so that a value-less invocation can never clear an existing setting.

## Options

```bash
Code (pos. 0)    Sys setting code

Value (pos. 1)   Sys setting Value (required to write; omit only when reading)

Type (pos. 2)    Sys setting Type (default: Text)

--get            Read the setting value instead of writing it.
                 The get-syssetting alias implies this automatically.
```

## Example

```bash
set-syssetting ExampleCode True Boolean - create boolean sys setting with code ExampleCode and value True
set-syssetting Maintainer ATF - update Maintainer sys setting with value ATF
get-syssetting Maintainer - read the Maintainer sys setting value
set-syssetting Maintainer --get - read the Maintainer sys setting value
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#set-syssetting)
