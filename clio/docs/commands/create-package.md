# create-package

## Name

create-package - Create a new package in a Creatio environment

## Description

Creates a new, empty, editable package in a Creatio environment through `PackageService.svc`, the same operation as "Create package" in the Configuration section, then reads the stored package back.

The environment's `SchemaNamePrefix` system setting is prepended when the name does not start with it: `Calls` becomes `UsrCalls`. The maintainer is taken from the environment's `Maintainer` system setting.

With `--application-code` the package is created inside that installed application (`ApplicationPackagesService.svc`); without it the package is standalone and is not registered as an installed application.

The platform ignores dependencies on creation, so clio saves them with a second request. When that request or the readback fails, the package already exists: the command reports it and exits with 1; add missing dependencies with `clio add-package-dependency`. When the create request itself fails in transport and the environment cannot be asked afterwards, the outcome is unknown: check `clio list-packages` before retrying.

Nothing is created when a package with that name already exists, a dependency or the application is not found, or the environment rejects the name.

## Synopsis

```bash
clio create-package --package-name <PACKAGE> [OPTIONS]
```

## Options

```bash
--package-name <PACKAGE>
Name of the package to create (required). The SchemaNamePrefix is prepended when missing.

--description <TEXT>
Package description

--dependencies <DEP[,DEP...]>
Installed packages the new package depends on. Multiple entries can be comma-separated or passed as separate values.

--application-code <CODE>
Code of an installed application to create the package in; omit for a standalone package

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
clio create-package --package-name Calls -e dev
Create a standalone package

clio create-package --package-name Calls --description "Calls migration" --dependencies CrtBase,CrtUIv2 -e dev
Create a package with a description and dependencies

clio create-package --package-name CallsExt --application-code UsrCallsApp -e dev
Create a package inside an installed application
```

## MCP

The `create-package` MCP tool takes `environment-name`, `package-name`, and optional `description`, `dependencies` (array of package names) and `application-code`. It returns `success`, `package-created`, `error`, `package-uid`, `package-name`, `maintainer`, `description`, `dependencies`, `install-type`, `editable` and `application-code`. `package-created=false` means nothing changed. `package-created=true` with `success=false` means the package exists but a later step failed, and `error` says which: dependencies not applied (add them with `add-package-dependency`) or the readback failed (check with `list-packages`). `package-created=null` means the create request failed in transport and the environment could not be asked afterwards; check `list-packages` before creating the package again. Use the returned `package-name`, which includes the prefix, for later calls.

## Notes

- The user needs the `CanManageSolution` system operation.
- A package created from a workspace with `clio push-workspace` is installed as an archive and comes out locked; use this command to get an editable package.

## See Also

add-package-dependency - Add one or more package dependencies to a package
list-packages - List packages in a Creatio environment
delete-pkg-remote - Delete a package from a Creatio environment

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#create-package)
