# merge-workspaces

## Name

merge-workspaces - Merge workspaces

## Description

Combines multiple workspaces into a single resulting workspace structure.

With `--install`, when the environment reports the installation as failed, the installer writes `Package installation failed: <reason>`, the command closes with `Merged workspace packages may not have been installed: <packages>.`, and it exits with `1`.

## Synopsis

```bash
clio merge-workspaces [OPTIONS]
```

## Options

```bash
Supports the canonical merge-workspaces command options.
```

## Examples

```bash
clio merge-workspaces --help
Display canonical options and usage examples
```

## Notes

Packing stops when a folder under `Schemas/` or `Data/` of any package has no
`descriptor.json`, because Creatio would reject the installation with
`Invalid descriptor`. The error names the full local path of each such folder; see
[push-workspace](push-workspace.md#folders-without-descriptorjson) for what to do
with it.

## See Also

create-workspace - Create a workspace
push-workspace - Push a workspace after merge

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#merge-workspaces)
