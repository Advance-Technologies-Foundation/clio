# page-sync

Updates multiple Freedom UI page schemas in a single MCP call. For each page: validates the
body client-side (optional), saves to Creatio, and verifies the update (optional). Continues
processing remaining pages on failure.

> **MCP-only tool** — available through the clio MCP server, not as a standalone CLI command.

## When to Use

Use `page-sync` instead of sequential calls to `page-update` when you need to save multiple
page schemas at once. A typical scenario is updating both a form page and a list page for
a single application entity.

## Parameters

| Parameter | Required | Default | Description |
|---|---|---|---|
| `environment-name` | Yes | — | Creatio environment name |
| `pages` | Yes | — | Array of page objects to update |
| `validate` | No | `true` | Run client-side validation (markers + JS syntax) before saving |
| `verify` | No | `false` | Read back each page after saving to confirm the update |

### Page Object

Each entry in the `pages` array must have:

| Field | Required | Description |
|---|---|---|
| `schema-name` | Yes | Freedom UI page schema name |
| `body` | Yes | Full JavaScript page body |
| `resources` | No | JSON object string with resource key-value pairs for `#ResourceString(key)#` macros |

## Example

```json
{
  "environment-name": "dev",
  "pages": [
    {
      "schema-name": "UsrTodoList_FormPage",
      "body": "define(\"UsrTodoList_FormPage\", ...full page body...)",
      "resources": "{\"UsrDetailsTab_caption\":\"Details\"}"
    },
    {
      "schema-name": "UsrTodoList_ListPage",
      "body": "define(\"UsrTodoList_ListPage\", ...full page body...)"
    }
  ],
  "validate": true,
  "verify": true
}
```

## Response

```json
{
  "success": true,
  "pages": [
    {
      "schema-name": "UsrTodoList_FormPage",
      "success": true,
      "body-length": 3775,
      "validation": {"markers-ok": true, "js-syntax-ok": true},
      "resources-registered": 1
    },
    {
      "schema-name": "UsrTodoList_ListPage",
      "success": true,
      "body-length": 2181,
      "validation": {"markers-ok": true, "js-syntax-ok": true}
    }
  ]
}
```

## Validation

When `validate` is `true` (the default), each page body is validated client-side before
being sent to Creatio:

- **Marker integrity** — checks that all required Freedom UI schema markers are present
  (`SCHEMA_DEPS`, `SCHEMA_ARGS`, `SCHEMA_VIEW_CONFIG_DIFF`, `SCHEMA_HANDLERS`,
  `SCHEMA_CONVERTERS`, `SCHEMA_VALIDATORS`, and model config markers)
- **JS syntax** — checks bracket matching and string literal balance

Validation failures prevent the page from being saved and are reported in the response.
This replaces the need for separate dry-run calls.

When a page body contains `#ResourceString(key)#` macros, `page-sync` forwards each page's
optional `resources` JSON object string to `page-update`. The response returns
`resources-registered` for each page so callers can see how many child-schema resources
were added during save.

## Error Handling

Pages are independent, so **processing continues** even if one page fails. The overall
`success` flag is `false` if any page failed, but all pages are attempted.

## See Also

- `page-update` — update a single Freedom UI page raw body
- `page-get` — read a Freedom UI page bundle plus raw body
- `page-list` — list Freedom UI pages
