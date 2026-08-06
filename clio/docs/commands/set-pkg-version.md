# set-pkg-version

## Command Type

    Service commands

## Name

set-pkg-version - set package version

## Description

Writes the specified version into the package's `descriptor.json`, and moves the descriptor's
`ModifiedOnUtc` timestamp with it.

Both fields are written together on purpose. Creatio rewrites a package's recorded version (the
`SysPackage` row) **only when `ModifiedOnUtc` changes**, so a version bumped without a fresh
timestamp installs successfully and silently leaves the OLD version recorded on the environment —
which then fails every dependency and requirement check that reads it. Editing `descriptor.json` by
hand means taking on that pairing yourself; this command exists so you cannot forget it.

## Arguments

| Argument | Required | Description |
|---|---|---|
| `<PACKAGE PATH>` | yes | Path to the package folder holding `descriptor.json` |
| `-v`, `--package-version` | yes | The version to write, for example `1.2.3.4` |

The version must be present and must parse as a version. Both checks refuse **before** the
descriptor is touched: writing an empty or unparseable version while still moving the timestamp
would produce a descriptor announcing a change it cannot describe.

## Exit codes

| Code | Meaning |
|---|---|
| `0` | The descriptor was updated |
| `1` | No usable version was supplied, or `descriptor.json` was not found. On a refusal the descriptor is left exactly as it was |

## Example

```bash
clio set-pkg-version <PACKAGE PATH> -v <PACKAGE VERSION>
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#set-pkg-version)
