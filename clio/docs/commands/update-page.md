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
- **Mobile page rules.** These run only on the MCP `update-page` / `sync-pages` / `validate-page` tools.
  The CLI `update-page` verb does **not** run them — it validates a mobile body only for disallowed
  sections — so a body rejected through MCP still saves from the command line.
  - **Rejected — an authored component's `type` outside `values`.** In `viewConfigDiff`, an
    `operation:"insert"` or `operation:"set"` that supplies a `values` object carrying no usable `"type"`,
    while putting `"type"` on the operation object, is refused. (A type present in BOTH places is fine when the
    two agree — the `values` copy is the one that applies.) The Creatio differ builds the element from `values` alone, so the type is
    discarded and the page would persist an element the mobile runtime cannot render — the write would
    otherwise succeed and the component would simply never appear. (`set` is included because it is
    `remove` + `insert` on the same payload.)
  - **Rejected — a flat `insert`.** A `"type"` on the operation object with no `values` object at all is
    refused for the same reason: `insert` declares no required parameters, so the differ does not reject it
    — it persists a typeless element. A flat `set` is left to the differ, which refuses it for the missing
    required `values`.
  - **Rejected — a `merge` that authors child elements in a Scaffold slot the template already fills.** An array
    (or a lone object) of item configs — objects carrying a non-empty `name` — placed on `Scaffold`'s `actions`,
    `leading` or `items`. `items` is the page body: every non-blank template puts a `MainContainer` there, so a
    merge authoring into it is discarded just like the navigation slots. (Membership is only consulted when the
    merge targets the Scaffold itself, so an `items` slot on any other container stays advisory.) Where the target already holds elements in a slot, the differ strips the whole property out of
    the merge before copying anything, so the write succeeds and the children never reach the page.
    Stand-verified for ENG-95429: the button appeared zero times in the server-merged `viewConfig` while
    remaining in the saved body. Those two slots are blocked rather than warned about because every shipped
    *form* template populates them — `actions` carries its Save button, `leading` its Close/Cancel — so in
    practice a merge into them is the discard case. Put the child in a page container instead: its own `insert`
    with `propertyName: "items"` plus a `layoutConfig`. **Residual:** `BlankMobilePageTemplate` ships a bare
    Scaffold with no content, so on a page built from it those slots may be empty, the merge would apply, and
    clio still refuses — it validates `viewConfigDiff` against an empty base and cannot see which case a body is
    in. Author the child with an `insert` there too rather than reaching for `validate: false`.
  - **Warned — a `merge` that authors child elements in any other slot.** Same mechanism, different odds: a
    slot the target does not carry (`menuItems` on a `crt.Button` or `crt.FloatingActionButton`, `items` on
    `crt.QuickFilterGroup`, `crt.Sort`, `crt.Timeline`) is *created* by the merge and the authoring works.
    clio validates `viewConfigDiff` against an empty base and cannot tell the two apart, so it steers rather
    than refuses. To author into a slot the target genuinely lacks, use the platform's two-step idiom: a
    `merge` creating the slot as an empty array (never flagged), then one `insert` per child — an `insert`
    into a property the target does not carry throws. Both are `merge`-only: for `insert`/`set` the `values`
    object becomes the element, so children declared there are the documented way to author a container.
  - **Warned — no type anywhere**, when `values` carries element properties but declares no `type`
    (an entry that authors nothing — absent or empty `values` — is silent by design).
  - **Warned — two DIFFERENT types**, one on the operation object and one inside `values`. The element still
    renders, as the `values` copy. Two identical types are accepted silently.
  - **Warned — an operation whose letter case does not match** the differ's exact-case dispatch
    (`"Insert"`): the whole operation is discarded, so it authors nothing.
  - **Warned — a `crt.Button` inserted into the Scaffold `actions` slot** (`parentName: "Scaffold"`,
    `propertyName: "actions"`). ENG-95429: the save succeeds, but a button placed there does not appear on the
    Freedom UI mobile designer canvas, so nobody can see or edit it there. Place it as an item of a page
    container instead — `propertyName: "items"` on a container the page or its template actually declares
    (confirm the name with `get-page`) — and give it a `layoutConfig`; that is the shape the designer itself
    emits. Advisory rather than blocking: `actions` is a legitimate runtime slot (the platform's own Save
    button lives there), so the defect is design-time discoverability, not an invalid write.
  - If an offending entry came back from `get-page`, the page already carries the defect — correct it in the
    body you send back.
  - **Not enforced:** the same type-placement and merge-slot defects break **web** pages identically and are not checked
    there; `sync-pages` with `validate: false` skips these checks along with every other one.
  Call `clio get-guidance --name mobile-page-modification` for details.
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

Successful saves may also return `warnings`. Every entry is informational only — the
schema save already succeeded, so never retry on a warning. Today they cover the live
Designer Presence push, a component whose `insert` the submitted body replaced with a
`merge`/`move`/`remove`, and an operation the differ will drop because another operation
for the same component name cancels it (see "Write modes").

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

## Write modes

`--mode replace` (default) saves the body verbatim. `--mode append` loads the current
schema body from the server and merges your incoming fragment into it.

A `viewConfigDiff` entry is replaced only when **both** `operation` and `name` match one of
yours — and, for a `remove`, whether it targets `properties`. Incoming wins, and the replacement
keeps the existing entry's position. Every other existing operation is preserved verbatim and in
place, including a second operation on a component you already target (a `move` and a `merge` for
one name are both valid and both survive the merge — though "survive" means kept in the body, not
necessarily applied; see the group-ordering caveat below).

There is one exception. If your fragment supersedes an identity that the page carries **twice**,
only the first occurrence is replaced and the later one is dropped — keeping it would re-apply its
stale values *after* your replacement. When those two entries set disjoint keys, the later entry's
keys go with it. Handlers dedupe by `request`.

**Preserved is not the same as applied**, and this part is not about append at all — it is how the
platform differ resolves any final body, so a hand-authored `--mode replace` body produces it too.
Operations are applied in whole **groups** in a fixed order (merges, then removes/inserts/moves,
`set` last), never in array order. So a `merge`, `move`, or element `remove` that ends up beside an
`insert` for the same `name` resolves against a base that does not contain the component yet and is
silently dropped; likewise a `move` for a name the same body also element-`remove`s, which the
differ filters out before applying anything. The same applies wherever one operation's group runs
after another's for one name: a `merge` beside an element `remove` or a `set` (the remove deletes,
or the set replaces wholesale, what the merge just patched), and a property `remove` beside an
element `remove` (the element is gone before property removals run — unless an `insert` re-creates
it, which makes the property removal effective again). The save still succeeds and the
response carries an advisory `warnings` entry naming the component and the dead operation. Fix it by folding the transform's values into the
`insert` itself, or by using `set`, which runs after the insert — not by reordering the array, which
changes nothing.

The warning is advisory because it reads one schema body and cannot see the replacing chain: a
parent schema that inserts the same name puts the component in the base and can make the transform
apply after all. A `--dry-run` reports it too, so you can check a body before writing it — in append
mode a dry run sees only your incoming fragment, so a pair formed by the server's `insert` plus your
`merge` is reported on the real save.

Append requires the **diff form**. A full-config body — the `SCHEMA_VIEW_MODEL_CONFIG` /
`SCHEMA_MODEL_CONFIG` markers (mobile: top-level `viewModelConfig` / `modelConfig`) instead
of the `*_DIFF` markers — cannot be merged. Such a body is rejected with an actionable hint;
use `--mode replace` to save it verbatim. Both surfaces refuse it: the CLI verb rejects it
while merging, and the MCP `update-page` tool detects it up front, before any server
round-trip. Note the `--body` value is always a raw string with `/**MARKER*/` pairs, never a
structured object.

The rejection message names **which** body is full-config, because the fix differs:

- **Your incoming body is full-config** — you authored it, so convert it to the diff form
  (`*_DIFF` markers) or use `--mode replace`.
- **The current page on the server is full-config** — every page `create-app-section`
  generates is stored this way. You did not author that body and cannot convert it, so
  append is **not supported** against it by design: a `*_DIFF` is a list of operations
  relative to a base and cannot be losslessly derived from an already-resolved full-config
  body without that base (see ENG-93090), and merging diff-form operations into a full-config
  body would produce an unloadable mixed form. `--mode replace` is the only path for such a page.

## Synopsis

```bash
clio update-page [options]
```

## Options

```bash
--schema-name                      Page schema name to update

--body                             Full raw JavaScript schema body (a string
with /**MARKER*/ pairs, not a structured object)

--body-file                        Path to a file containing the body.
Alternative to --body for large bodies

--mode                             Write mode: 'replace' (default) or 'append'
(merge with the current body). Append requires the
diff form; a full-config body is rejected up front —
use 'replace' for such a body

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
