# page-get

## Purpose
`page-get` reads a Freedom UI page as a merged bundle plus raw schema body.

This command upgrades the old raw-body-only read model. The response now contains:

- nested page metadata
- a merged effective bundle built from the full designer hierarchy
- `raw.body` for round-tripping into `page-update`

## Usage
```bash
clio page-get --schema-name <SCHEMA_NAME> [options]
```

## Options

| Option | Short | Required | Description |
|--------|-------|----------|-------------|
| `--schema-name` |  | Yes | Freedom UI page schema name |
| `--Environment` | `-e` | No | Registered clio environment name |
| `--uri` | `-u` | No | Creatio application URL |
| `--Login` | `-l` | No | Creatio user login |
| `--Password` | `-p` | No | Creatio user password |
| `--Maintainer` | `-m` | No | Maintainer name |

## Output

`page-get` returns a JSON envelope like this:

```json
{
  "success": true,
  "page": {
    "schemaName": "UsrTodo_FormPage",
    "schemaUId": "guid",
    "packageName": "UsrApp",
    "packageUId": "guid",
    "parentSchemaName": "PageWithTabsFreedomTemplate"
  },
  "bundle": {
    "name": "UsrTodo_FormPage",
    "viewConfig": [],
    "viewModelConfig": {},
    "modelConfig": {},
    "resources": {
      "strings": {}
    },
    "handlers": "[]",
    "converters": "{}",
    "validators": "{}",
    "parameters": [],
    "deps": "[]",
    "args": "()",
    "optionalProperties": []
  },
  "raw": {
    "body": "define(...)"
  },
  "error": null
}
```

## How to Use the Response

- Inspect `page` to confirm the current schema and package.
- Inspect `bundle.viewConfig` to understand the effective layout and containers.
- Inspect `bundle.viewModelConfig` and `bundle.modelConfig` to understand the effective data model.
- Edit `raw.body` when you need to save changes with `page-update`.

## Examples

Read a page from a registered environment:
```bash
clio page-get --schema-name UsrTodo_FormPage -e dev
```

Read a page using direct connection arguments:
```bash
clio page-get --schema-name UsrTodo_FormPage -u https://my-creatio -l Supervisor -p Supervisor
```

## Recommended Workflow

1. Use `page-list` to discover the schema name.
2. Call `page-get` to inspect the merged bundle and current raw body.
3. Modify `raw.body`.
4. Save the edited raw body with `page-update`.

Use `page-sync` only when you need to save multiple page bodies in one MCP workflow.
