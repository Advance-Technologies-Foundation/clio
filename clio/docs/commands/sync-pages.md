# sync-pages

Updates multiple Freedom UI page schemas in a single MCP call. For each page: validates the
body client-side (optional), saves to Creatio, and verifies the update (optional). Continues
processing remaining pages on failure.

> **MCP-only tool** — available through the clio MCP server, not as a standalone CLI command.

## When to Use

Use `sync-pages` instead of sequential calls to `update-page` when you need to save multiple
page schemas at once. A typical scenario is updating both a form page and a list page for
a single application entity.

Before editing handler or validator sections in raw page bodies, use `get-guidance` with
`name` set to `page-schema-handlers` or `page-schema-validators`.

## Validation Rules

When `validate` is `true` (the default), the body is checked client-side before save:

- **SCHEMA_CONVERTERS keys** (object form) must follow `VendorPrefix.ConverterName` format
  (e.g., `usr.MyConverter`). Call `clio get-guidance --name page-schema-converters` for details.
- **SCHEMA_HANDLERS** must be an array of `{ request, handler }` entries. Each `request` value
  must follow `VendorPrefix.HandlerName` format (e.g., `crt.HandleViewModelInitRequest`,
  `usr.HandleSomeRequest`). Call `clio get-guidance --name page-schema-handlers` for details.
- **SCHEMA_VALIDATORS keys** (object form) must follow `VendorPrefix.ValidatorName` format
  (e.g., `usr.RequiredValidator`). Call `clio get-guidance --name page-schema-validators` for details.
- **User-visible text must be localizable.** Any `label`, `caption`, `title`, `tooltip`, or
  `placeholder` in `viewConfigDiff` (at any nesting depth) set to an inline string literal is
  **rejected**. Bind it via `$Resources.Strings.<Key>` (or `#ResourceString(<Key>)#` for data-grid
  column captions and validator messages) and register the key's default-language value through
  `resources`. Call `clio get-guidance --name page-schema-resources` for the full rule.
- **Inserted widget/metric titles must resolve.** A `title`/`caption`/`tooltip`/`placeholder` on a
  freshly inserted (`operation:"insert"`) widget/container bound as `$Resources.Strings.<Key>` or
  `#ResourceString(<Key>)#` is **rejected** when `<Key>` will not resolve — i.e. it is not passed in
  `resources`, is not a DS-bound attribute, and is not a `Usr`-prefixed key. This guards the
  metric/chart-widget-title case (`#ResourceString(IndicatorWidget_<slug>_title)#` is registered only
  when passed in `resources`; otherwise it renders raw as `$Resources.Strings.IndicatorWidget_<slug>_title`).

A malformed `VendorPrefix.Name` in any of these sections causes a Creatio runtime error:
`"Error when register X. Type property should have format VendorPrefix.TypeName"`.

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
| `force` | No | Skip the external-modification (checksum) conflict check for this page and deliberately overwrite out-of-band changes. Default `false` |

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
      "resources-registered": 1,
      "page": {
        "schemaName": "UsrTodoList_FormPage",
        "schemaUId": "11111111-1111-1111-1111-111111111111",
        "packageName": "UsrTodoList",
        "packageUId": "22222222-2222-2222-2222-222222222222",
        "parentSchemaName": "PageWithTabsFreedomTemplate"
      },
      "verified-body-file": ".clio-pages/UsrTodoList_FormPage/body.js"
    },
    {
      "schema-name": "UsrTodoList_ListPage",
      "success": true,
      "body-length": 2181,
      "validation": {"markers-ok": true, "js-syntax-ok": true},
      "page": {
        "schemaName": "UsrTodoList_ListPage",
        "schemaUId": "33333333-3333-3333-3333-333333333333",
        "packageName": "UsrTodoList",
        "packageUId": "22222222-2222-2222-2222-222222222222",
        "parentSchemaName": "BaseSectionTemplate"
      },
      "verified-body-file": ".clio-pages/UsrTodoList_ListPage/body.js"
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
- **Marker content shape** — JSON-backed markers must still parse as structured data, while
  `SCHEMA_HANDLERS` must remain a JavaScript array section and `SCHEMA_CONVERTERS` /
  `SCHEMA_VALIDATORS` must remain JavaScript object sections, so function-based runtime
  handler and validator entries stay valid

Validation failures prevent the page from being saved and are reported in the response.
This replaces the need for separate dry-run calls.

When a page body contains `#ResourceString(key)#` macros, `sync-pages` forwards each page's
optional `resources` JSON object string to `update-page`. The response returns
`resources-registered` for each page so callers can see how many child-schema resources
were added during save.

When `verify` is `true`, each successful page result also returns:

- `page` — the same metadata shape as `get-page.page`
- `verified-body-file` — path to the local `body.js` file written from the raw body read back from Creatio after save

## Conflict Detection (external modifications)

When the MCP `get-page` tool previously stored a checksum baseline in
`.clio-pages/{schema-name}/meta.json` for the **same environment**, each page write first
compares the stored `SysSchema.Checksum` against the server. A page whose schema was
modified outside the current session (e.g. edited in the Creatio designer) fails with a
per-page conflict — the rest of the batch continues:

```jsonc
{
  "schema-name": "UsrTodoList_FormPage",
  "success": false,
  "conflict": true,
  "conflict-details": { "reason": "checksum-mismatch", "expectedChecksum": "…", "actualChecksum": "…" },
  "error": "Page schema '…' was modified outside this session …"
}
```

Recovery: re-run `get-page` for the conflicted schema, re-apply the change on top of the
fresh body, then retry — or set the per-page `force: true` after the user explicitly
confirms overwriting the external changes.

Baseline maintenance after a successful save:

- `verify: true` — a full fresh `meta.json` (page metadata + new baseline) is written next
  to the verified `body.js`.
- `verify: false` — the existing `meta.json` baseline is updated with the post-save
  checksum; if fresh metadata could not be obtained, the baseline is removed so the next
  write skips the check instead of reporting a false conflict.

Pages without a baseline (no prior MCP `get-page`, legacy `meta.json`, or a different
environment) are saved without the check — fully backward compatible.

## Error Handling

Pages are independent, so **processing continues** even if one page fails. The overall
`success` flag is `false` if any page failed, but all pages are attempted.

## See Also

- `update-page` — update a single Freedom UI page raw body
- `get-page` — read a Freedom UI page bundle plus raw body
- `list-pages` — list Freedom UI pages
