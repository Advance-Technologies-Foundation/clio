# DB-First Data Binding Commands

## Overview

The DB-first data binding commands (`create-data-binding-db`, `upsert-data-binding-row-db`, `remove-data-binding-row-db`) provide remote-first alternatives to the file-based data binding commands. Instead of generating local workspace files, they write directly to the Creatio database through standard DataService and SchemaDataDesigner endpoints. To sync the result to a local workspace, use `restore-workspace` separately.

## create-data-binding-db

Creates a DB-first package data binding by persisting row data to the remote Creatio DB.

### Options

| Option | Required | Description |
|---|---|---|
| `-e`, `--environment` | Yes (or `--uri`) | Creatio environment name |
| `--uri` | Yes (or `--environment`) | Creatio application URI |
| `--package` | Yes | Target package name |
| `--schema` | Yes | Entity schema name |
| `--binding-name` | No | Binding folder name (defaults to `<schema>`) |
| `--rows` | No | JSON array of row objects, each with a `values` key |

### Behavior

1. Resolves the package UId via `IApplicationPackageListProvider.GetPackages()`.
2. Fetches the entity schema column list via `RuntimeEntitySchemaRequest`.
3. Calls `SchemaDataDesignerService.svc/SaveSchema` to create or update the binding schema data.

### Examples

```bash
# Create a binding for SysSettings on the dev environment
clio create-data-binding-db -e dev --package Custom --schema SysSettings

# Create with initial rows
clio create-data-binding-db -e dev --package Custom --schema SysSettings \
  --binding-name UsrMyBinding \
  --rows "[{\"values\":{\"Name\":\"Row\",\"Code\":\"UsrRow\"}}]"
```

---

## upsert-data-binding-row-db

Upserts a single row in an existing DB-first data binding.

### Options

| Option | Required | Description |
|---|---|---|
| `-e`, `--environment` | Yes (or `--uri`) | Creatio environment name |
| `--uri` | Yes (or `--environment`) | Creatio application URI |
| `--package` | Yes | Target package name |
| `--binding-name` | Yes | Binding folder name |
| `--values` | Yes | Row values as JSON object keyed by column name |

### Behavior

1. Calls `SchemaDataDesignerService.svc/SaveSchema` to upsert the row in the remote DB.

### Examples

```bash
clio upsert-data-binding-row-db -e dev --package Custom --binding-name SysSettings \
  --values "{\"Name\":\"Updated name\",\"Code\":\"UsrSetting\"}"
```

---

## remove-data-binding-row-db

Removes a row from a DB-first data binding and deletes the binding schema data record when no rows remain.

### Options

| Option | Required | Description |
|---|---|---|
| `-e`, `--environment` | Yes (or `--uri`) | Creatio environment name |
| `--uri` | Yes (or `--environment`) | Creatio application URI |
| `--package` | Yes | Target package name |
| `--binding-name` | Yes | Binding folder name |
| `--key-value` | Yes | Primary-key value of the row to remove |

### Behavior

1. Resolves the entity schema name from `SysPackageSchemaData` via `SelectQuery`.
2. Fetches currently bound rows via `GetBoundSchemaData`.
3. Locates the row by primary-key value.
4. Sends a `DeleteQuery` to remove the entity record from the DB.
5. If no bound rows remain → sends `DeletePackageSchemaDataRequest` to remove the binding definition.

### Examples

```bash
clio remove-data-binding-row-db -e dev --package Custom --binding-name SysSettings \
  --key-value 4f41bcc2-7ed0-45e8-a1fd-474918966d15
```

---

## MCP Tools

All three DB-first commands have corresponding MCP tool wrappers:

| MCP Tool Name | CLI Equivalent |
|---|---|
| `create-data-binding-db` | `create-data-binding-db` |
| `upsert-data-binding-row-db` | `upsert-data-binding-row-db` |
| `remove-data-binding-row-db` | `remove-data-binding-row-db` |

MCP tools use `environment-name` instead of `-e` / `--environment`.
