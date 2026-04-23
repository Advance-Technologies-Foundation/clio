# create-data-binding

## Command Type

    Development commands

## Name

create-data-binding - Create or regenerate a package data binding from a runtime schema

## Synopsis

```bash
clio create-data-binding [OPTIONS]
```

## Description

Creates or updates a data-binding folder under the target package.

When the requested schema has a built-in offline template, clio uses that
template and does not require Creatio access. In v1, SysSettings is the
supported offline template and template metadata always
takes precedence over runtime schema fetches.

For schemas that are not covered by the built-in template catalog, clio
fetches the runtime entity schema from a Creatio environment.

The generated binding lives under:
<workspace>/packages/<package>/Data/<binding-name>

Files created or updated:
- descriptor.json
- data.json
- filter.json
- Localization/data.en-US.json in template mode
- Localization/data.<culture>.json for cultures supplied in --localizations

If --values is omitted, the command creates a single template row with all
schema columns and empty placeholder values.

## Options

```bash
--package              Target package name
--schema               Entity schema name used to fetch template or runtime metadata
--workspace-path       Workspace root path. Defaults to the current workspace
--binding-name         Binding folder name. Defaults to <schema>
--install-type         Descriptor install type. Allowed values: 0, 1, 2, 3.
Default: 0
--values               Optional JSON object keyed by column name for the
initial row. If the GUID primary key column is
omitted or null, it is generated automatically.
For lookup and image-reference columns, pass either
a scalar value or an object with value and
displayValue. When runtime lookup data is available,
create-data-binding resolves a missing displayValue
automatically.
For image-content columns, pass either a base64
string or a local file path inside the workspace and
clio encodes the file
--localizations        Optional JSON object keyed by culture and column name

Environment options are also available:
-e, --Environment      Environment name from the registered configuration
-u, --uri              Application URI
-l, --Login            User login
-p, --Password         User password
```

## Examples

```bash
# Create a SysSettings binding offline from the current workspace
clio create-data-binding --package Custom --schema SysSettings

# Create a non-templated binding in an explicit workspace with initial values
clio create-data-binding -e dev --package Custom --schema UsrCustomEntity --workspace-path C:\Work\MyWorkspace --values '{\"Name\":\"Setting name\"}'

# Create a binding with explicit localizations
clio create-data-binding -e dev --package Custom --schema SysSettings --values "{\"Name\":\"Setting name\"}" --localizations '{\"ru-RU\":{\"Name\":\"Настройка\"}}'

# Create a binding that uses an image-content column from a runtime schema
clio create-data-binding -e dev --package Custom --schema UsrImageBinding --workspace-path C:\Work\MyWorkspace --values "{\"Code\":\"UsrImageBinding\",\"UsrImage\":\"assets\\icon.png\"}"

# Create a binding with explicit lookup display text
clio create-data-binding -e dev --package Custom --schema UsrLookupBinding --values "{\"Code\":\"UsrLookupBinding\",\"UsrStatus\":{\"value\":\"b659d704-3955-e011-981f-00155d043204\",\"displayValue\":\"In Progress\"}}"
```

## Notes

- For the templated schema SysSettings, --environment and --uri are optional
- For non-templated schemas, the command requires either --environment or --uri
- Unknown columns in --values or --localizations are rejected
- If the primary key column is a Guid and is omitted or null in --values, create-data-binding generates it automatically
- For lookup and image-reference columns, clio writes SchemaColumnUId, Value, and DisplayValue in data.json
- For create-data-binding, lookup and image-reference values may use {"value":"...","displayValue":"..."}; if displayValue is omitted and Creatio runtime lookup data is available, clio resolves it automatically
- For image-content columns, a string value that points to an existing local file inside the workspace is encoded to base64 before writing data.json
- If the target binding folder already exists for another schema, the
command fails instead of overwriting it
- filter.json is always created as an empty file

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

## See Also

add-data-binding-row, remove-data-binding-row, call-service

- [Clio Command Reference](../../Commands.md#create-data-binding)
