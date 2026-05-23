# link-from-repository

## Command Type

    Development commands

## Name

link-from-repository - Link repository package(s) to environment.

## Description

Links workspace package content into a local Creatio package directory.

You can provide either:
- -e / --Environment (registered environment)
- --envPkgPath (direct path to the target package folder)

Environment-name resolution works on Windows, macOS, and Linux when the registered
environment has EnvironmentPath configured and the local package folder exists under it.
On Windows, clio also falls back to IIS/URL discovery for older registrations.
The --envPkgPath value may be absolute or relative to the current working directory.

Use `--unlocked` to automatically query the Creatio site for unlocked packages and link
only those. This requires `-e` or `-u` for API connection. Supports both flat repo
structure (`repo/PackageName/`) and versioned PackageStore structure
(`repo/PackageName/branch/version/`).

When packages are incomplete in the Pkg folder (missing or without `descriptor.json`),
the `--packages` flow automatically prepares them before linking:

1. **Maintainer check** — reads the `Maintainer` field from each package `descriptor.json`
   in the repository and ensures the Creatio site's `Maintainer` sys setting matches.
   This is required because Creatio only allows editing packages owned by the current maintainer.
2. **Unlock** — unlocks the specified packages on the Creatio site so they can be modified
   in file system development mode.
3. **2fs (to file system)** — syncs package content from the database to the local file system,
   creating the folder structure that symlinks will point to.

Use `--skip-preparation` to disable this behavior and link as-is.

`--unlocked` and `--packages` are mutually exclusive.

## Options

| Option | Description |
|---|---|
| `--packages` | Comma-separated list of package names, or `*` for all |
| `--unlocked` | Query the Creatio site for unlocked packages and link only those |
| `--dry-run` | Print a summary of what would happen without executing any mutations |
| `--skip-preparation` | Skip the automatic preparation step (Maintainer check, unlock, 2fs) |
| `--repoPath` | Path to the package repository folder |
| `-e` / `--Environment` | Registered environment name |
| `--envPkgPath` | Direct path to the target Pkg folder |

## Example

```bash
clio link-from-repository -e MyEnvironment --repoPath ./packages --packages "*"

clio link-from-repository --envPkgPath "/path/to/Creatio/Terrasoft.Configuration/Pkg" --repoPath ./packages --packages "PkgA,PkgB"

clio link-from-repository -e dev --repoPath /path/to/git-repo --unlocked

# Preview what would happen without making changes
clio link-from-repository -e dev --repoPath /path/to/git-repo --unlocked --dry-run

# Link packages without automatic preparation (unlock + 2fs)
clio link-from-repository -e dev --repoPath ./packages --packages "PkgA" --skip-preparation
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#link-from-repository)
