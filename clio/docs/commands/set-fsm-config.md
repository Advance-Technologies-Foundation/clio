# set-fsm-config

## Purpose
`set-fsm-config` updates local Creatio configuration values that control file system mode (FSM).

Use this command when you only need to switch the configuration flags in the app config file without loading packages to the file system or database.

## Usage
```bash
clio set-fsm-config <IsFsm> [options]
```

**Aliases**: `fsmc`, `sfsmc`

## Arguments

| Argument | Position | Required | Description |
|----------|----------|----------|-------------|
| `IsFsm` | 0 | Yes | Target FSM mode value: `on` or `off` |

## Options

| Option | Short | Required | Description |
|--------|-------|----------|-------------|
| `--physicalPath` |  | No | Path to the local Creatio application root |
| `--Environment` | `-e` | No | Registered clio environment name |
| `--uri` | `-u` | No | Creatio application URI |
| `--Login` | `-l` | No | Creatio user login |
| `--Password` | `-p` | No | Creatio user password |
| `--IsNetCore` | `-i` | No | Registered environment runtime hint when resolving settings |

Provide either `--physicalPath` or `-e/--Environment`.

## Behavior

The command updates:

- `terrasoft/fileDesignMode@enabled`
- `appSettings/add[@key='UseStaticFileContent']@value`

Config file resolution:

- On Windows, the command checks `Web.config` first and then `Terrasoft.WebHost.dll.config`.
- For registered environments with `IsNetCore=true`, it prefers `Terrasoft.WebHost.dll.config`.
- On macOS and Linux, the command supports NET8 environments and resolves the config path from the registered `EnvironmentPath` or the supplied `--physicalPath`.

## Examples

Update a registered environment:
```bash
clio set-fsm-config on -e dev
```

Update a local app folder directly:
```bash
clio set-fsm-config off --physicalPath /opt/creatio
```

Update a Windows installation directly:
```bash
clio set-fsm-config on --physicalPath C:\Creatio\dev
```

## Output

On success the command prints a table with the old and new values and returns exit code `0`.

On failure the command returns exit code `1` and writes validation or config-path diagnostics.
