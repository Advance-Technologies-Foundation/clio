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
- **Mobile page rules.** Applied to each mobile body when `validate` is `true`. Note this validates the
  WHOLE body, so a page that already stores a rejected shape fails until it is corrected.
  - **Rejected** — an `operation:"insert"`/`"set"` whose `values` object carries no usable `"type"` while a
    `"type"` sits on the operation object (a type in both places is fine when they agree); and a flat `insert` (a `"type"` on the operation object with no `values` object at
    all). The Creatio differ builds the element from `values` alone, so the type is discarded and the
    element never renders even though the save succeeds. A flat `set` is left to the differ, which refuses
    it for the missing required `values`.
  - **Rejected** — a `merge` whose `values` authors child elements on `Scaffold`'s `actions`, `leading` or
    `items` (an array, or a lone object, of item configs carrying a non-empty `name`). `items` is the page body,
    filled with a `MainContainer` by every non-blank template; the same slot on any other container stays
    advisory. Every shipped *form* template
    populates those slots, so the differ strips the property out of the merge and nothing is created even
    though the write succeeds. Put the child in a page container with its own `insert` plus a `layoutConfig`.
    clio validates `viewConfigDiff` against an empty base and cannot see the target, so a page built from
    `BlankMobilePageTemplate` — a bare Scaffold whose slots may be empty — is refused as well, even though the
    merge would have applied there.
  - **Warned** — the same authoring in any other slot. There the target may legitimately lack the slot, in
    which case the merge creates it and the authoring works; clio validates against an empty base and cannot
    distinguish the two. Both are `merge`-only — for `insert`/`set` the `values` object becomes the element,
    so children declared there are created.
  - **Warned** — no type anywhere while `values` carries element properties; two DIFFERENT types (the
    element still renders, as the `values` copy); and an operation whose letter case does not match the
    differ's exact-case dispatch; and a `crt.Button` inserted into `parentName: "Scaffold"`,
    `propertyName: "actions"`, which saves but does not appear on the mobile designer canvas (ENG-95429) —
    place it in a page container's `items` with a `layoutConfig` instead.
  - **Not enforced** — the same type-placement and merge-slot defects break **web** pages identically and are not checked
    there, and `validate: false` skips these checks along with every other one, re-opening the
    silent-persist path; do not use it to get past a rejection.
  Call `clio get-guidance --name mobile-page-modification` for details.
- **User-visible text must be localizable.** Any `label`, `caption`, `title`, `tooltip`, or
  `placeholder` in `viewConfigDiff` (at any nesting depth) set to an inline string literal is
  **rejected**. Bind it via `$Resources.Strings.<Key>` (or `#ResourceString(<Key>)#` for data-grid
  column captions and validator messages) and register the key's default-language value through
  `resources`. Call `clio get-guidance --name page-schema-resources` for the full rule.
  A **component's own data descriptor is exempt**: a `data` object that carries the platform's
  `typeName` marker, on a node declaring a component `type`, is component metadata (uId, schemaType,
  typeName and the caption the platform stamped on it) rather than page-authored text, so a literal
  anywhere inside it is accepted — at any depth, not only at the entry root. This is what a Timeline
  composer (`crt.EmailComposer` / `crt.FeedComposer`) ships as `data.caption: "Email"` / `"Feed"`.
  Do NOT delete such a caption to satisfy the rule: the platform never restores it and the composer
  stays permanently unlabelled. An author-writable input that merely happens to be named `data`
  (e.g. `crt.FilterBuilderSource`) carries no `typeName` and stays fully validated.
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
| `resources` | No | JSON object string with resource key-value pairs for `#ResourceString(key)#` macros. **Additions only** — a key already stored on the schema stays registered and does not have to be re-sent on a later save |
| `optional-properties` | No | JSON array of `{key, value}` objects merged into the schema's `optionalProperties` |
| `checksum` | No | The `editable.checksum` from the `get-page` this page's edit is based on. Becomes the authoritative conflict baseline for **this page** |
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

Advisory findings are different: they appear in each page's `validation.warnings` and never
prevent a save. `sync-pages` forwards every warning `update-page` produces, so a page can come
back successful with a warning that one of its `viewConfigDiff` operations is silently dropped at
apply time because another operation for the same component name cancels it — the differ applies
whole operation groups in a fixed order, not in array order. See
[`update-page`](update-page.md) for the shapes and the remedies.

When a page body contains `#ResourceString(key)#` macros, `sync-pages` forwards each page's
optional `resources` JSON object string to `update-page`. The response returns
`resources-registered` for each page so callers can see how many child-schema resources
were added during save.

`resources` is an **additions** payload, not the full registered set. A key registered by one save
is written into the page schema's `localizableStrings` and stays there, so it resolves at runtime
whether or not a later save repeats it — and re-sending it answers `resources-registered: 0`,
because an already-stored key is never rewritten. The validation gate honours this: a label bound to
a key that is only persisted on the schema is accepted without being repeated. The lookup costs one
extra schema read and is paid ONLY when a label-resource check has already rejected the body, so a
clean page pays nothing. If that read fails (an unreachable environment, a refused schema read), the
stricter verdict stands and the page result carries a warning naming the reason.

When `verify` is `true`, each successful page result also returns:

- `page` — the same metadata shape as `get-page.page`
- `verified-body-file` — path to the local `body.js` file written from the raw body read back from Creatio after save

## Conflict Detection (external modifications)

Pass the per-page `checksum` — the `editable.checksum` from the `get-page` that page's edit is based
on — on every save that follows a `get-page`. It becomes the authoritative baseline for that page,
so the comparison runs against the body the caller actually read.

Without it the check falls back to the baseline the MCP `get-page` tool stored in
`.clio-pages/{schema-name}/meta.json` for the **same environment**. That baseline is keyed by
(anchor directory, schema name) only, so it can be present, environment-matched, and still describe a
different body — a different working directory between the `get-page` and the save is enough to
produce a conflict nothing external caused.

Either way, a page whose schema was modified outside the current session (e.g. edited in the Creatio
designer) fails with a per-page conflict — the rest of the batch continues:

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
confirms overwriting the external changes. Re-sending the conflict response's `actualChecksum`
as the page's `checksum` is **not** a recovery: it discards the external change exactly like
`force: true` and needs the same explicit confirmation.

Baseline maintenance after a successful save:

- `verify: true` — a full fresh `meta.json` (page metadata + new baseline) is written next
  to the verified `body.js`.
- `verify: false` — the existing `meta.json` baseline is updated with the post-save
  checksum; if fresh metadata could not be obtained, the baseline is removed so the next
  write skips the check instead of reporting a false conflict.

Pages with neither a `checksum` nor a baseline (no prior MCP `get-page`, legacy `meta.json`, or a
different environment) are saved without the check — fully backward compatible. A pinned save that no
local baseline corroborates still runs the check, and says so in that page's warnings.

## Error Handling

Pages are independent, so **processing continues** even if one page fails. The overall
`success` flag is `false` if any page failed, but all pages are attempted.

## See Also

- `update-page` — update a single Freedom UI page raw body
- `get-page` — read a Freedom UI page bundle plus raw body
- `list-pages` — list Freedom UI pages
