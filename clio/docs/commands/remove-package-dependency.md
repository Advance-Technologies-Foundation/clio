# remove-package-dependency

## Name

remove-package-dependency - Remove one or more package dependencies from a package

## Description

Removes one or more package dependencies from a Creatio package via the `PackageService.svc` endpoint, then persists the change. This is the symmetric counterpart of [`add-package-dependency`](add-package-dependency.md).

Use this command to roll back a dependency that was added only to unblock the schema designer once it is no longer needed.

Dependencies are matched by name (case-insensitive); a trailing `:version` is accepted for symmetry with `add-package-dependency` but ignored. The operation is idempotent: removing a dependency that is not present is a no-op.

> **Caution:** keep the dependency if your package still extends an object whose upper layer the dependency owns — removing it will break the schema designer again.

## Synopsis

```bash
clio remove-package-dependency --package-name <PACKAGE> --dependencies <DEP[,DEP...]> [OPTIONS]
```

Aliases: `remove-pkg-dependency`, `remove-pkg-dep`.

## Options

```bash
--package-name <PACKAGE>
Target package whose dependency list is trimmed (required)

--dependencies <DEP[,DEP...]>
One or more dependency package names to remove (required), matched by name.
Multiple entries can be comma-separated or passed as separate values.

-e, --environment <ENVIRONMENT_NAME>
Target environment name

-u, --uri <URI>
Application URI (instead of -e)

-l, --Login <LOGIN>
User login (administrator permission required)

-p, --Password <PASSWORD>
User password
```

## Examples

```bash
clio remove-package-dependency --package-name MyApp --dependencies CrtLeadOppMgmtApp -e dev
Remove a single dependency using a configured environment

clio remove-package-dependency --package-name MyApp --dependencies CrtLeadOppMgmtApp,CrtCase -e dev
Remove several dependencies at once

clio remove-pkg-dep --package-name MyApp --dependencies CrtLeadOppMgmtApp -e dev
Using the shortest alias
```

## Notes

- Administrator permissions are required on the target environment.
- Removing a dependency may require a configuration compilation afterwards.

## See Also

add-package-dependency - Add one or more package dependencies to a package
list-packages - List packages in a Creatio environment
compile-package - Compile a package in Creatio

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#remove-package-dependency)
