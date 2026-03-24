# ver

Display version information for clio and related components.

## Synopsis

```bash
clio ver [OPTIONS]
```

**Aliases:** `info`, `get-version`, `i`

## Description

The `ver` command displays version information for clio, the bundled cliogate package,
the active .NET runtime, and optionally the settings file path.

## Options

- `--all` - Display all known version information
- `--clio` - Display only the clio version
- `--gate` - Display only the bundled cliogate version
- `--runtime` - Display only the .NET runtime version
- `-s`, `--settings-file` - Display the settings file path

## Examples

```bash
clio ver
clio ver --runtime
clio ver -s
```

## See also

- [Commands.md](../../Commands.md#ver)
