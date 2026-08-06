# apply-manifest

## Name

apply-manifest - Apply environment manifest

## Description

Applies a saved environment manifest to the selected Creatio instance: its applications, features,
system settings and web services.

## Synopsis

```bash
clio apply-manifest [OPTIONS]
```

## Options

```bash
Supports the canonical apply-manifest command options.
```

## Examples

```bash
clio apply-manifest --help
Display canonical options and usage examples
```

## Notes

- Every stage runs to the end. A manifest entry the environment refuses does not abandon the entries
  after it, and does not skip the stages that follow it, so a refused feature never silently drops the
  system settings and web services the manifest names.
- The refused entries are listed once, after all stages have run, and the command exits with code 1. An
  exit code of 0 means every entry in the manifest reached the environment.
- A manifest that cannot be read at all still fails outright, because there is nothing to apply
  partially.

## See Also

save-state - Create a manifest before applying changes
show-diff - Compare manifests or environments

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#apply-manifest)
