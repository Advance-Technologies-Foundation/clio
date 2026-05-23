# delete-app-section

## Command Type

    Application management commands

## Name

delete-app-section - Delete a section from an existing installed application

## Description

The delete-app-section command removes a section and all its associated
metadata artifacts from a Creatio application. The following artifacts
are deleted in order:

- SysModuleInWorkplace records (workplace visibility entries)
- SysModuleLcz records (section localizations, if any)
- Freedom UI page schemas and addon schemas (via WorkspaceExplorer)
- SysModuleEntity record (section-to-entity binding)
- SysModule record (the section itself)

By default, the underlying entity schema is preserved so that data in the
underlying database table is not lost. Pass `--delete-entity-schema` to also
remove the entity schema.

## Synopsis

```bash
clio delete-app-section [options]
```

## Options

```bash
--application-code               Installed application code. Required.

--section-code                   Section code (schema name). Required.

--delete-entity-schema           Also delete the underlying entity schema.
                                 Default: false

--Environment            -e      Environment name. Required.
```

## Output

The command prints structured JSON that includes:

- application identity
- deleted section identity and metadata

## Example

```bash
clio delete-app-section --application-code UsrOrdersApp --section-code UsrOrders -e dev
# delete the UsrOrders section from UsrOrdersApp, keeping the entity schema

clio delete-app-section --application-code UsrOrdersApp --section-code UsrOrders --delete-entity-schema -e dev
# delete the section and also remove the UsrOrders entity schema
```

## Notes

- The entity schema is preserved by default so data in the underlying table is not lost.
- The command is destructive and cannot be undone.
- cliogate must be installed on the target environment.
- Page schemas (Freedom UI pages, addon related-page schemas) are deleted through WorkspaceExplorer and require developer mode to be active on the target environment.

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#delete-app-section)
