# turn-fsm

## Command Type

    Development commands

## Name

turn-fsm - Turn file system mode on or off for an environment

## Description

Toggles Creatio file system mode (FSM).

When turning FSM on:
- Updates configuration to enable File Design Mode
- Loads packages to the file system

When turning FSM off:
- Loads packages to the database
- Updates configuration to disable File Design Mode

Use either:
- --physicalPath (path to the environment folder)
- -e / --Environment (registered environment)

On macOS and Linux this command supports NET8 environments and relies on the registered
EnvironmentPath or the provided --physicalPath to update the local config file.

## Example

```bash
clio turn-fsm -e MyEnvironment on

clio turn-fsm -e MyEnvironment off

clio turn-fsm --physicalPath "/path/to/creatio" on
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#turn-fsm)
