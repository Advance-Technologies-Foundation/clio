# turn-fsm

## Purpose
`turn-fsm` toggles Creatio file system mode (FSM) for a local environment.

Use this command when you need the full FSM workflow, not just config edits:

- Turning FSM `on` updates config values, restarts NET8 environments when needed, and loads packages to the file system.
- Turning FSM `off` loads packages to the database and then updates the config values.

## Usage
```bash
clio turn-fsm <IsFsm> [options]
```

**Aliases**: `tfsm`, `fsm`

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

## Platform Notes

- Windows supports local IIS and local NET8 layouts.
- macOS and Linux support NET8 environments and use the registered `EnvironmentPath` or the supplied `--physicalPath` to find the local config file.

## Examples

Turn FSM on for a registered environment:
```bash
clio turn-fsm on -e dev
```

Turn FSM off for a registered environment:
```bash
clio turn-fsm off -e dev
```

Turn FSM on for a local folder directly:
```bash
clio turn-fsm on --physicalPath /opt/creatio
```

## Output

On success the command returns exit code `0` and prints the config changes plus output from the package load step.

On failure the command returns exit code `1` and writes a diagnostic message.
