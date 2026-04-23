# add-data-binding-row

## Command Type

    Development commands

## Name

add-data-binding-row - Add or replace a row in an existing package data binding

## Synopsis

```bash
clio add-data-binding-row [OPTIONS]
```

## Description

Updates an existing local package data binding by adding a new row or
replacing the row that has the same primary-key value.

The command reads descriptor.json to resolve column names to schema column
identifiers and writes the updated row to data.json. If --localizations is
provided, matching localization files are created or updated under the
binding Localization folder. Once a binding exists locally, this command
does not require Creatio access, including bindings that were created from
built-in offline templates.

## Options

```bash
--package              Target package name
--binding-name         Binding folder name under package Data
--workspace-path       Workspace root path. Defaults to the current workspace
--values               JSON object keyed by column name for the row payload.
If the GUID primary key column is omitted or null,
it is generated automatically. For image-content
columns, pass either a base64 string or a local file
path inside the workspace and clio encodes the file.
For non-null lookup and image-reference columns, use
an object with value and displayValue
--localizations        Optional JSON object keyed by culture and column name
```

## Examples

```bash
# Add a new row from the current workspace
clio add-data-binding-row --package Custom --binding-name SysSettings --values '{\"Name\":\"Setting name\"}'

# Replace an existing row and update localization data
clio add-data-binding-row --package Custom --binding-name SysSettings --workspace-path C:\Work\MyWorkspace --values '{\"Name\":\"New name\"}" --localizations "{\"en-US\":{\"Name\":\"Localized name\"}}'

# Add a row using a local image file for an image-content column
clio add-data-binding-row --package Custom --binding-name UsrImageBinding --workspace-path C:\Work\MyWorkspace --values "{\"Code\":\"UsrImageBinding\",\"UsrImage\":\"assets\\icon.png\"}"

# Add a row with explicit lookup display text
clio add-data-binding-row --package Custom --binding-name UsrLookupBinding --values "{\"Code\":\"UsrLookupBinding\",\"UsrStatus\":{\"value\":\"b659d704-3955-e011-981f-00155d043204\",\"displayValue\":\"In Progress\"}}"
```

## Notes

- The binding must already exist locally
- The row key is the primary column marked in descriptor.json
- If that primary key is Guid-based and omitted or null in --values, add-data-binding-row generates it automatically
- For non-null lookup and image-reference columns, use {"value":"...","displayValue":"..."} so data.json keeps both Value and DisplayValue
- For image-content columns, a string value that points to an existing local file inside the workspace is encoded to base64 before writing data.json
- Unknown columns in --values or --localizations are rejected
- Existing rows with the same primary key are replaced instead of duplicated

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

## See Also

create-data-binding, remove-data-binding-row

- [Clio Command Reference](../../Commands.md#add-data-binding-row)
