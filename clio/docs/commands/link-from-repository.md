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

## Example

```bash
clio link-from-repository -e MyEnvironment --repoPath ./packages --packages "*"

clio link-from-repository --envPkgPath "/path/to/Creatio/Terrasoft.Configuration/Pkg" --repoPath ./packages --packages "PkgA,PkgB"

clio link-from-repository -e dev --repoPath /path/to/git-repo --unlocked
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#link-from-repository)
