# update-page

## Command Type

    Development commands

## Name

update-page - Update the raw schema body of a Freedom UI page

**Aliases:** `page-update`

## Description

The update-page command validates and saves the raw JavaScript body of a
Freedom UI page schema. Pass the full body string directly, typically
after reading raw.body from get-page.

After a successful non-dry-run save, update-page also attempts a
best-effort live Designer Presence notification so active Creatio designers
can be warned that the page was saved outside their session. This live push
reuses the browser-session/forms-auth path and therefore requires
login/password-backed cookies. In OAuth-only or credential-less environments,
the page save still succeeds; the response simply carries a warning when the
live notification is skipped or fails.

When the body contains #ResourceString(key)# macros, update-page can
register missing child-schema localizableStrings before saving. Pass
--resources when you need explicit captions, or let clio derive captions
automatically for missing Usr* keys.

Use --optional-properties to merge custom key-value pairs into the schema's
`optionalProperties` array (for example to set `entitySchemaName`).

Keep each field control bound to the declared view-model attribute from
`viewModelConfig` / `viewModelConfigDiff`. If you add validator or
handler logic on a different attribute for the same field, rebind the
control to that attribute as well so the control, validators, and
handler writes all target the same declared attribute.
If the control is inherited from a parent schema and there is no local
entry for it in `viewConfigDiff`, add a local `merge` for that control
name instead of trying to edit a non-existent local `insert`.

## Validation Rules

**Before editing the body**, understand the validation rules:

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
  `--resources`. Binding expressions (any `$`-prefixed value) and non-string values (e.g.
  `placeholder: false`) are not literals and pass. Call `clio get-guidance --name page-schema-resources`
  for the full rule.
- **Inserted widget/metric titles must resolve.** A `title`/`caption`/`tooltip`/`placeholder` on a
  freshly inserted (`operation:"insert"`) widget/container bound as 
  `#ResourceString(<Key>)#` is **rejected** when `<Key>` will not resolve — i.e. it is not passed in
  `--resources`, is not a DS-bound attribute, and is not a `Usr`-prefixed key clio auto-derives. This
  guards the metric/chart-widget-title case (a title such as
  `#ResourceString(IndicatorWidget_<slug>_title)#` is registered only when you pass it in `--resources`;
  otherwise it renders raw as `$Resources.Strings.IndicatorWidget_<slug>_title`).

A malformed `VendorPrefix.Name` causes a Creatio runtime error:
`"Error when register X. Type property should have format VendorPrefix.TypeName"`.

## Conflict Detection (external modifications)

update-page compares a baseline checksum against the current `SysSchema.Checksum` of the
editable schema **before** saving (baseline sources are described below). If the schema
was modified outside your session (for example, a user edited the page in the Creatio
designer), the save is blocked and the response carries a structured conflict:

```jsonc
{
  "success": false,
  "conflict": true,
  "conflictDetails": {
    "reason": "checksum-mismatch",          // or schema-created-externally |
                                            //    schema-deleted-externally | schema-uid-mismatch
    "expectedChecksum": "…", "actualChecksum": "…",
    "expectedSchemaUId": "…", "actualSchemaUId": "…",
    "modifiedOn": "…"                       // informational only
  },
  "error": "Page schema '…' was modified outside this session …"
}
```

Recovery: re-run `get-page`, re-apply your change on top of the fresh body, then retry.
Pass `--force` to deliberately overwrite the external changes instead.

After a successful save with a baseline in play, the response carries `newChecksum`,
`newModifiedOn`, and `savedSchemaUId` so the caller can refresh its stored baseline.

Successful saves may also return a `warnings` entry about the live Designer Presence
push. Treat that warning as informational only: the schema save already succeeded.

Baseline sources: both the CLI verb and the MCP `update-page` tool arm this check
automatically from the baseline that a previous `get-page` stores in
`.clio-pages/{schema-name}/meta.json` (matching environment required) — so AI-agent CLI
flows that read a page with `get-page` and then save it with `update-page` are protected
without extra flags. `--expected-checksum` overrides the on-disk baseline when passed
explicitly. After a successful save the on-disk baseline is refreshed automatically, so
consecutive updates in the same session do not false-conflict. A small race window
between the check and the save remains (last write wins).

If you pass `--expected-checksum` while an on-disk baseline is also present, the explicit
value wins and the auto-armed baseline is ignored — so supplying a stale checksum by hand
can report a conflict against a page that has not actually changed. This edge fails safe
(it blocks the save rather than overwriting), but if you mix the two, keep
`--expected-checksum` current or omit it and let the on-disk baseline drive the check.

## Synopsis

```bash
clio update-page [options]
```

## Options

```bash
--schema-name                      Page schema name to update

--body                             Full raw JavaScript schema body

--dry-run                          Validate only and do not save

--resources                        Valid JSON object of resource key-value
pairs for #ResourceString(key)# macros
Malformed JSON is rejected during
validation

--optional-properties              JSON array of {key, value} objects to
merge into schema optionalProperties,
e.g. '[{"key":"entitySchemaName","value":"UsrMyEntity"}]'

--expected-checksum                Baseline SysSchema checksum of the editable
schema (from get-page). Blocks the save with
a structured conflict when the server-side
checksum differs

--force                            Skip the external-modification check and
deliberately overwrite out-of-band changes

--uri                    -u       Application uri

--Password               -p       User password

--Login                  -l       User login (administrator permission required)

--Environment            -e       Environment name

--Maintainer             -m       Maintainer name
```

## Example

```bash
clio update-page --schema-name UsrTodo_FormPage --body "<raw body>" --dry-run true -e dev
validate a raw Freedom UI body without saving it

clio update-page --schema-name UsrTodo_FormPage --body "<edited raw body>" -e dev
save the edited raw Freedom UI body to the registered dev environment

clio update-page --schema-name UsrTodo_FormPage --body "<edited raw body>" --resources "{\"UsrDetailsTab_caption\":\"Details\"}" -e dev
save the page and register the missing child-schema localizable string

clio update-page --schema-name UsrTodo_FormPage --body "<edited raw body>" --optional-properties "[{\"key\":\"entitySchemaName\",\"value\":\"UsrTodo\"}]" -e dev
save the page and merge custom optional properties into the schema
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#update-page)
