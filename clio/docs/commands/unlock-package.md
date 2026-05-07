# unlock-package

## Description

Unlocks one or more packages in a Creatio environment to enable editing and modifications.
Uses ClioGate direct database queries to update `InstallType = 0` on the `SysPackage` record,
bypassing the DataService ESQ permission layer that can deny access even for privileged users.

When unlocking, the package `Maintainer` field is set to the environment's current
`Maintainer` system setting value. The original maintainer is stored in the package
`Description` field and restored automatically when the package is locked again with
`lock-package`.

## Synopsis

```bash
clio unlock-package [package-names] [options]
clio up [package-names] [options]
```

## Arguments

| Argument | Description |
|---|---|
| `package-names` | Comma-separated list of package names to unlock. If omitted, unlocks all packages whose Maintainer matches the environment's sys setting. |

## Options

| Option | Description |
|---|---|
| `-m, --maintainer <NAME>` | Maintainer value. Sets the Maintainer system setting and clears SchemaNamePrefix before unlocking all packages. **Required** when no package names are specified. |
| `-e, --environment <NAME>` | Environment name from configuration (recommended) |
| `-u, --uri <URI>` | Creatio application URI (alternative to `-e`) |
| `-l, --login <LOGIN>` | Username for authentication |
| `-p, --password <PASSWORD>` | Password for authentication |
| `--clientid <ID>` | OAuth Client ID |
| `--clientsecret <SECRET>` | OAuth Client Secret |
| `--authappuri <URI>` | OAuth Authentication App URI |

## Examples

```bash
# Unlock a single package
clio unlock-package MyPackage -e dev
clio up MyPackage -e dev

# Unlock multiple packages
clio unlock-package Package1,Package2,Package3 -e dev

# Unlock all packages (sets Maintainer sys setting first)
clio unlock-package -m Creatio -e dev
```

Output for unlock all:
```
Setting Maintainer sys setting to 'Creatio'.
Setting SchemaNamePrefix sys setting to an empty value.
Unlocking all packages in environment 'dev' for maintainer 'Creatio'.
Done
```

```bash
# Using direct credentials instead of a named environment
clio unlock-package MyPackage --uri https://myapp.creatio.com -l admin -p pass
```

## Requirements

- **cliogate >= 2.0.0.42** must be installed on the target Creatio environment.
- The authenticated user must have the **CanManageSolution** system operation permission.

```bash
# Install or update cliogate
clio install-gate -e <ENVIRONMENT_NAME>

# Check installed cliogate version
clio get-info -e <ENVIRONMENT_NAME>
```

## Notes

- Package names are case-sensitive and must match exactly as stored in Creatio.
- After unlock the package `Maintainer` is set to the environment's `Maintainer` sys setting;
  the original maintainer is preserved in the `Description` field.
- Lock packages again with `lock-package` after completing changes to protect them.
- This command replaced a DataService-based implementation that silently failed with a
  `SecurityException` on `SysPackage` for some permission configurations (see issue #585).

## See Also

- [`lock-package`](lock-package.md) — Lock packages to prevent modifications
- [`install-gate`](install-gate.md) — Install or update the cliogate package
- [`push-workspace`](push-workspace.md) — Push workspace changes to an environment
- [`get-info`](get-info.md) — Check environment and cliogate information
- [Clio Command Reference](../../Commands.md#unlock-package)
