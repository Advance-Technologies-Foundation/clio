# sync-schemas

Executes a batch of schema operations in a single MCP call: create lookups, create entities,
seed data, update entities. Reduces MCP round-trips, lock acquisitions, and sleep overhead
compared to calling each operation individually.

> **MCP-only tool** — available through the clio MCP server, not as a standalone CLI command.

## When to Use

Use `sync-schemas` instead of sequential calls to `create-lookup`, `create-data-binding-db`,
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
| `update-operations` | Conditionally | Column mutation operations (same format as `update-entity-schema`). Required unless `columns` is supplied. |
| `columns` | Conditionally | Read/create-shape columns (no `action` verbs) treated as an implicit **add** batch. Use this to add columns without writing `update-operations`. |

Supply either `update-operations` (for add/modify/remove) **or** `columns` (add-only shorthand). If both
are present, `update-operations` wins.

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

- `create-lookup` and `create-entity` require a schema-level `title-localizations`.
- `title-localizations` is OPTIONAL for a column add (both the `columns` add-batch and `update-operations`
  with `action: "add"`). When it is omitted, the mandatory `en-US` caption is auto-derived using the
  precedence: explicit `title-localizations.en-US` > scalar legacy `title` > scalar legacy `caption` >
  humanized `column-name` (a `Usr` prefix is stripped and PascalCase is space-split, e.g. `UsrDueDate` →
  `Due Date`). A bare `{column-name, type}` add therefore never fails for a missing localization map.
- `update-operations` use `title-localizations` and `description-localizations`.
- When you provide a localization map it must include a non-empty `en-US` value, and the `en-US` value
  must be English (non-English text such as Cyrillic under `en-US` is rejected).
- For an add, the legacy scalar `title`/`caption` are accepted only as an en-US fallback — prefer
  `title-localizations`. The legacy scalar `description` is rejected; use `description-localizations`.

## Column Vocabulary (read-modify-write round trip)

The column field names accepted here are unified with the shape that `get-app-info` returns, so a
column read from `get-app-info` can be sent back without manual field translation. Canonical field
names are accepted, and the legacy read-shape field names are accepted as aliases:

| Concept | Canonical (write) | Read-shape alias (also accepted) |
|---|---|---|
| Column identity | `column-name` (in `update-operations`) / `name` (in `columns`) | `name` in `update-operations`; `column-name` in `columns` |
| Column type | `type` | `data-value-type` |
| Lookup reference | `reference-schema-name` | `reference-schema` |
| Required flag | `required` | `is-required` |
| Caption (add only) | `title-localizations` (OPTIONAL — auto-derived when omitted) | `caption`/`title` (scalar; promoted to the `en-US` localization) |

`get-app-info` returns each column with both the canonical (`name`, `type`, `reference-schema-name`,
`required`) and the legacy (`data-value-type`, `reference-schema`) field names, plus a scalar `caption`.
To modify or remove a column you read, send the same shape back inside an `update-operations` entry and
add the `action` verb (`modify` or `remove`). To add columns, drop the read/create-shape objects into
`columns` (no `action` needed) — the scalar `caption` is promoted to `title-localizations` automatically,
and when no caption is present the `en-US` value is derived from the column name — or send explicit
`update-operations` with `action: "add"`, which follows the same auto-derivation.

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
