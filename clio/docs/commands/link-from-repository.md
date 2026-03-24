# link-from-repository

## Purpose
`link-from-repository` links workspace package content into a local Creatio environment package directory.

Use this command when you want a local environment to point package folders at repository content instead of keeping separate copies.

## Usage
```bash
clio link-from-repository [options]
```

**Aliases**: `l4r`, `link4repo`

## Options

| Option | Short | Required | Description |
|--------|-------|----------|-------------|
| `--Environment` | `-e` | No | Registered clio environment name |
| `--envPkgPath` |  | No | Direct path to the target environment package folder |
| `--repoPath` | `-r` | Yes | Path to the package repository folder |
| `--packages` | `-p` | No | Package selector. Use `*` for all packages or a comma-separated package list |

Provide either `-e/--Environment` or `--envPkgPath`.

## Environment Resolution

- On Windows, `-e/--Environment` first tries the registered local `EnvironmentPath` and then falls back to IIS/URL discovery for older registrations.
- On macOS and Linux, `-e/--Environment` works when the registered environment has `EnvironmentPath` configured and the local package folder exists under it.
- `--envPkgPath` works on all platforms and bypasses environment-name resolution.
- `--envPkgPath` may be absolute or relative to the current working directory.

Expected package-folder layouts under `EnvironmentPath`:

- NET8: `Terrasoft.Configuration/Pkg`
- Classic: `Terrasoft.WebApp/Terrasoft.Configuration/Pkg`

## Examples

Link all repository packages into a registered environment:
```bash
clio link-from-repository -e dev --repoPath ./packages --packages "*"
```

Link selected packages into a registered environment:
```bash
clio l4r -e dev --repoPath ./packages --packages "PkgA,PkgB"
```

Link by direct package path:
```bash
clio link-from-repository --envPkgPath /opt/creatio/Terrasoft.Configuration/Pkg --repoPath ./packages --packages "*"
```

## Output

On success the command returns exit code `0` and reports the target package folder and repository path.

On failure the command returns exit code `1` and writes a validation or environment-resolution error.
