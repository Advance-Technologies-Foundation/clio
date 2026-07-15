# list-schema-hierarchy

## Command Type

    Development commands

## Name

list-schema-hierarchy - List every package schema of a client unit schema by name (base + all replacing schemas)

**Aliases:** `schema-hierarchy-list`

## Description

The list-schema-hierarchy command lists every package schema that shares one client unit schema
name — the base schema plus every replacing schema across packages — each with its package,
maintainer, InstallType, an is-client-editable flag, the base template, and a `HierarchyLevel`.
The schemas are ordered so the base comes first and replacing schemas follow by depth.

This is the foundation for Classic→Freedom migration discovery: use it to see the full override
chain of a schema, pick which schema to read by UId with `get-classic-schema-by-uid`, and identify
the client-editable package to write into.

## Synopsis

```bash
clio list-schema-hierarchy [options]
```

## Options

```bash
--schema-name                      Client unit schema name shared across all schemas,
                                   e.g. 'ContractPageV2' (required)

--manager-name                     SysSchema.ManagerName to filter by
                                   (default: ClientUnitSchemaManager)

--uri                    -u       Application uri

--Password               -p       User password

--Login                  -l       User login (administrator permission required)

--Environment            -e       Environment name

--Maintainer             -m       Maintainer name
```

## Example

```bash
clio list-schema-hierarchy --schema-name ContactPageV2 -e dev
# List every package schema of ContactPageV2 with package, level and flags

clio schema-hierarchy-list --schema-name ContractPageV2 -e dev
# Same using the alias
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#list-schema-hierarchy)
