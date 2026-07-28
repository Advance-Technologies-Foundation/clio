# get-app-info

## Command Type

    Application management commands

## Name

get-app-info - Get information about an installed Creatio application

## Description

The get-app-info command retrieves identity, version, package, and entity
information for an installed Creatio application and prints it to the console.

Identify the target application with --code (application code) or --id
(application GUID). At least one of these must be provided.

By default the command prints a readable summary table. Use --json to output
the raw JSON response instead.

## Synopsis

```bash
clio get-app-info [options]
```

## Options

```bash
--code                           Installed application code

--id                             Installed application identifier (GUID)

--json                           Output as indented JSON instead of a table

--Environment            -e      Environment name. Required.
```

## Output

The command prints:
- Application name, code, and version
- Primary package name
- Entity list with column counts (when present)

With --json the full JSON response is printed.

Each CLI JSON entity includes `IsVirtual`; the MCP result exposes the same value as `virtual`.
A `true` value means the schema has no physical database table.

### Column fields (round-trip with `sync-schemas`)

Each entity column in the JSON response carries a vocabulary unified with the `sync-schemas`
write surfaces, so a column read here can be sent back to `sync-schemas update-entity` without
manual field translation:

| Field | Meaning |
|---|---|
| `name` | Column identity (matches the write-side column name) |
| `type` | Canonical column type |
| `data-value-type` | Legacy alias of `type`, retained for backward compatibility |
| `reference-schema-name` | Canonical lookup reference schema (omitted when not a lookup) |
| `reference-schema` | Legacy alias of `reference-schema-name`, retained for backward compatibility |
| `required` | Whether the column is required |
| `caption` | Localized column caption |

To modify or remove a column you read, send the same object back inside a `sync-schemas`
`update-operations` entry and add the `action` verb (`modify`/`remove`). To add columns, place
read/create-shape objects into the `columns` array.

## Example

```bash
clio get-app-info --code UsrOrdersApp -e dev
# print a summary of the UsrOrdersApp application

clio get-app-info --code UsrOrdersApp --json -e dev
# print the full JSON response for the application
```

## Notes

- At least one of --code or --id must be provided.
- Both --code and --id may be provided simultaneously; the service uses whichever is available.

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#get-app-info)
