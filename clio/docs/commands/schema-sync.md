# schema-sync

Executes a batch of schema operations in a single MCP call: create lookups, create entities,
seed data, update entities. Reduces MCP round-trips, lock acquisitions, and sleep overhead
compared to calling each operation individually.

> **MCP-only tool** — available through the clio MCP server, not as a standalone CLI command.

## When to Use

Use `schema-sync` instead of sequential calls to `create-lookup`, `create-data-binding-db`,
and `update-entity-schema` when you need to set up multiple related schemas in one step.
A typical scenario is bootstrapping a new application with lookup tables, seed data, and
entity column references.

## Parameters

| Parameter | Required | Description |
|---|---|---|
| `environment-name` | Yes | Creatio environment name |
| `package-name` | Yes | Target package name on the Creatio environment |
| `operations` | Yes | Ordered array of schema operations to execute |

### Operation Types

Each operation in the `operations` array must have a `type` and `schema-name`. Additional
fields depend on the operation type.

Requests use `operations[*].type`. Responses also identify each result by `type`. Do not send `operations[*].operation` in requests.

#### `create-lookup`

Creates a lookup schema inheriting from `BaseLookup` and automatically registers it in the standard `Lookups` section.

| Field | Required | Description |
|---|---|---|
| `type` | Yes | `"create-lookup"` |
| `schema-name` | Yes | Lookup entity schema name |
| `title-localizations` | Yes | Schema title localizations. Must include `en-US` |
| `columns` | No | Initial columns (same format as `create-entity-schema`) |
| `seed-rows` | No | Rows to insert after creation |

A `create-lookup` result is successful only when both the schema creation and the `Lookups` registration complete successfully.

#### `create-entity`

Creates an entity schema with an optional parent.

| Field | Required | Description |
|---|---|---|
| `type` | Yes | `"create-entity"` |
| `schema-name` | Yes | Entity schema name |
| `title-localizations` | Yes | Schema title localizations. Must include `en-US` |
| `parent-schema-name` | No | Parent schema name |
| `extend-parent` | No | Create a replacement schema (default: false) |
| `columns` | No | Initial columns |
| `seed-rows` | No | Rows to insert after creation |

#### `update-entity`

Applies batch column mutations to an existing entity schema.

| Field | Required | Description |
|---|---|---|
| `type` | Yes | `"update-entity"` |
| `schema-name` | Yes | Target entity schema name |
| `update-operations` | Yes | Column mutation operations (same format as `update-entity-schema`) |

### Seed Rows Format

Each seed row must have a `values` key containing column name-value pairs:

```json
[
  {"values": {"Id": "guid-1", "Name": "New"}},
  {"values": {"Id": "guid-2", "Name": "Done"}}
]
```

## Example

```json
{
  "environment-name": "dev",
  "package-name": "UsrTodoList",
  "operations": [
    {
      "type": "create-lookup",
      "schema-name": "UsrTodoStatus",
      "title-localizations": {
        "en-US": "Todo Status",
        "uk-UA": "Статус справи"
      },
      "seed-rows": [
        {"values": {"Name": "New"}},
        {"values": {"Name": "In Progress"}},
        {"values": {"Name": "Done"}}
      ]
    },
    {
      "type": "create-lookup",
      "schema-name": "UsrTodoPriority",
      "title-localizations": {
        "en-US": "Todo Priority"
      },
      "seed-rows": [
        {"values": {"Name": "Low"}},
        {"values": {"Name": "Medium"}},
        {"values": {"Name": "High"}}
      ]
    },
    {
      "type": "update-entity",
      "schema-name": "UsrTodoList",
      "update-operations": [
        {
          "action": "add",
          "column-name": "UsrStatus",
          "type": "Lookup",
          "title-localizations": {
            "en-US": "Status"
          },
          "reference-schema-name": "UsrTodoStatus",
          "required": true
        },
        {
          "action": "add",
          "column-name": "UsrPriority",
          "type": "Lookup",
          "title-localizations": {
            "en-US": "Priority"
          },
          "reference-schema-name": "UsrTodoPriority"
        },
        {
          "action": "add",
          "column-name": "UsrDueDate",
          "type": "Date",
          "title-localizations": {
            "en-US": "Due date"
          }
        }
      ]
    }
  ]
}
```

## Localization Contract

- `create-lookup` and `create-entity` require `title-localizations`.
- `columns` in create operations require `title-localizations`.
- `update-operations` use `title-localizations` and `description-localizations`.
- Every localization map must include a non-empty `en-US` value.
- Legacy scalar `title`, `caption`, and `description` fields are rejected by the MCP contract.

## Masking Behavior

- `update-operations` can include `masked` for `Text` and `SecureText` columns.
- `masked` maps to schema-level `isValueMasked`.
- `masked` controls schema-level masking metadata and does not change the storage type.

## Response

```json
{
  "success": true,
  "results": [
    {"type": "create-lookup", "schema-name": "UsrTodoStatus", "success": true},
    {"type": "seed-data", "schema-name": "UsrTodoStatus", "success": true},
    {"type": "create-lookup", "schema-name": "UsrTodoPriority", "success": true},
    {"type": "seed-data", "schema-name": "UsrTodoPriority", "success": true},
    {"type": "update-entity", "schema-name": "UsrTodoList", "success": true}
  ]
}
```

`type` is the result discriminator for response items. It identifies the executed step, such as `create-lookup`, `update-entity`, or synthetic follow-up steps like `seed-data`.

## Error Handling

Operations execute in order and **stop on first failure**. Subsequent operations may depend
on earlier ones (e.g., a lookup must exist and be registered before it can be maintained through
`Lookups` or referenced as a column type). Partial results are returned so the caller knows which
operations succeeded.

## See Also

- `create-lookup` — create a single lookup schema
- `create-entity-schema` — create a single entity schema
- `update-entity-schema` — apply batch column mutations
- `create-data-binding-db` — seed data into an entity
