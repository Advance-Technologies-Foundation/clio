# Data-Binding Template Catalog

## Purpose

This document defines the built-in offline template catalog used by the data-binding command family.

## Rules

- `create-data-binding` must resolve schema metadata from the built-in template catalog when the requested schema is present there.
- Built-in template metadata always takes precedence over runtime schema fetches.
- When a schema is not present in the template catalog, `create-data-binding` must fetch runtime metadata from Creatio.
- `add-data-binding-row` and `remove-data-binding-row` operate only on the local binding files once the binding exists and do not require template lookup or Creatio access.

## Built-In Templates

### v1

- `SysSettings`

## SysSettings Template

The built-in `SysSettings` template carries the schema identity, primary column identity, and stable column metadata required to generate `descriptor.json`, `data.json`, and localization scaffolding without contacting Creatio.

The generated descriptor must use the template metadata exactly:

- Schema `UId`: `27aeadd6-d508-4572-8061-5b55b667c902`
- Schema `Name`: `SysSettings`
- Primary column: `Id` / `ae0e45ca-c495-4fe7-a39d-3ab7278e1617`

The v1 template column set is:

- `Code`
- `IsSSPAvailable`
- `Name`
- `IsPersonal`
- `Description`
- `Id`
- `ReferenceSchemaUId`
- `IsCacheable`
- `ValueTypeName`

## Command Impact

- `create-data-binding` may omit `--environment` and `--uri` for `SysSettings`
- MCP `create-data-binding` may omit `environment-name` for `SysSettings`
- Non-templated schemas still require Creatio access
