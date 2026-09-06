# pkg-to-db

## Description

pkg-to-db - Load file-system package definitions into the configuration database

## Synopsis

```bash
clio pkg-to-db [options]
```

## Description

Imports the package definitions stored on the web application's file system
into the Creatio configuration database. This is the reverse direction of
pkg-to-file-system (2fs) and the way filesystem edits made in file system
development mode (FSM) reach the running configuration.

Scope: the command registers package CONTENT - schemas, resources and package
descriptors. It does not install package DATA: a Data/<BindingName> folder
(descriptor.json, data.json, filter.json) is not applied to its target table by
this command, and no rows are inserted, updated or counted. Package data is
installed by package installation (push-pkg or push-workspace, which run the
platform's installPackageData step), or applied row by row by the DB-first
commands create-data-binding-db and upsert-data-binding-row-db.

The command requires file system development mode to be enabled on the
environment. When FSM is disabled, when the FSM state cannot be read, or when
the platform refuses the import, nothing is loaded and the command exits with
code 1 and an error message.

## Examples

```bash
Load the environment's file-system packages into the database:
clio pkg-to-db -e dev
clio pkg-to-db -e dev

Apply a data binding on an FSM environment (pkg-to-db does not do it):
clio create-data-binding-db --schema-name UsrMyLookup --package-name UsrPkg -e dev
```

## Requirements

File system development mode (FSM) must be enabled on the environment. Check it
with:
clio get-fsm-mode -e <ENVIRONMENT_NAME>

## Notes

- Aliases: 2db, todb
- Exit code 0 means the platform reported the import as completed; any other
outcome (FSM disabled, unreadable FSM state, platform error) exits with 1
- Configuration changes may additionally require a compilation and a restart
before they take full effect
- A Data/ folder created by create-data-binding is a transferable source
artifact only; it changes nothing on the environment until the package is
installed or the DB-first data-binding commands are used

## See Also

pkg-to-file-system          Export packages from the database to the file system
get-fsm-mode                Show whether file system development mode is enabled
create-data-binding-db      Create a package data binding directly in the database
upsert-data-binding-row-db  Add or update a row of a database-first data binding
push-pkg                    Install a package, including its package data

- [Clio Command Reference](../../Commands.md#pkg-to-db)
