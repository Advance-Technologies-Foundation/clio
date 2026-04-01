# set-fsm-config

Set file system mode properties in config file.


## Usage

```bash
clio set-fsm-config [<EnvironmentName>] <IsFsm> [options]
```

## Description

Updates the Creatio configuration file to enable or disable File Design Mode.

It changes:
- terrasoft/fileDesignMode enabled
- appSettings/UseStaticFileContent value (opposite of FSM)

You can provide either:
- --physicalPath (path to the environment folder)
- -e / --Environment (registered environment)

On Windows the command updates either Web.config or Terrasoft.WebHost.dll.config.
On macOS and Linux it supports NET8 environments and uses the registered EnvironmentPath
or the provided --physicalPath to find Terrasoft.WebHost.dll.config.

## Aliases

`fsmc`, `sfsmc`

## Examples

```bash
clio set-fsm-config -e MyEnvironment on

clio set-fsm-config --physicalPath "/path/to/creatio" off
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#set-fsm-config)
