# lock-package

## Description

Locks one or more packages in a Creatio environment to prevent modifications and
unintended changes. Uses ClioGate direct database queries to update `InstallType = 1`
on the `SysPackage` record, bypassing the DataService ESQ permission layer that can
deny access even for privileged users.

When locking, the original package `Maintainer` (stored in the `Description` field
during a previous `unlock-package` call) is automatically restored.

## Synopsis

```bash
clio lock-package [package-names] [options]
clio lp [package-names] [options]
```

## Arguments

| Argument | Description |
|---|---|
| `package-names` | Comma-separated list of package names to lock. If omitted, locks all packages whose Maintainer matches the environment's sys setting. |

## Options

| Option | Description |
|---|---|
| `-e, --environment <NAME>` | Environment name from configuration (recommended) |
| `-u, --uri <URI>` | Creatio application URI (alternative to `-e`) |
| `-l, --login <LOGIN>` | Username for authentication |
| `-p, --password <PASSWORD>` | Password for authentication |
| `--clientid <ID>` | OAuth Client ID |
| `--clientsecret <SECRET>` | OAuth Client Secret |
| `--authappuri <URI>` | OAuth Authentication App URI |

## Examples

```bash
# Lock a single package
clio lock-package MyPackage -e dev
clio lp MyPackage -e dev

# Lock multiple packages
clio lock-package Package1,Package2,Package3 -e dev

# Lock all packages belonging to the environment's maintainer
clio lock-package -e dev

# Full development cycle
clio unlock-package MyPackage -e dev
# Make changes, then push...
clio push-workspace -e dev
clio lock-package MyPackage -e dev

# Using direct credentials instead of a named environment
clio lock-package MyPackage --uri https://myapp.creatio.com -l admin -p pass
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
- Locking restores the original `Maintainer` from the `Description` field set during
  a previous `unlock-package`; if the package was never unlocked the current maintainer
  is preserved unchanged.
- Always lock packages after completing development work to protect them from accidental edits.
- Include `lock-package` as the final step in deployment pipelines.
- This command replaced a DataService-based implementation that silently failed with a
  `SecurityException` on `SysPackage` for some permission configurations (see issue #585).

## See Also

- [`unlock-package`](unlock-package.md) — Unlock packages to enable modifications
- [`install-gate`](install-gate.md) — Install or update the cliogate package
- [`push-workspace`](push-workspace.md) — Push workspace changes to an environment
- [`get-info`](get-info.md) — Check environment and cliogate information
- [Clio Command Reference](../../Commands.md#lock-package)
