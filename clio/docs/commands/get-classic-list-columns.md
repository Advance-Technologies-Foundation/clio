# get-classic-list-columns

## Command Type

    Development commands

## Name

get-classic-list-columns - Resolve the effective default columns of a Classic section list

## Description

The `get-classic-list-columns` command resolves a Classic section's effective list column set through
read-only Creatio APIs. It returns JSON with the requested `sectionSchema`, resolved `entity`, ordered
`columns`, explanatory `notes`, and one of these source values, tried in this order:

- `profile` — the saved grid profile, which is what the list actually **renders**. A `profile` result also
  carries `view`, `viewType` and `profileScope` (see below);
- `schema-default` — static paths declared by `getGridDataColumns` or `initColumnsConfig` in the section hierarchy;
- `entity-default` — the entity primary display column, used when the section has no static column declaration;
- `none` — no source exists. This is a successful result with an empty `columns` array.

**A product section normally resolves to `profile`, and the difference is large.** `AccountSectionV2` declares
`Name` and `PrimaryContact` in code while its list opens with five columns, and two of the columns the user sees
(`Web`, `Phone`) appear nowhere in the section's JavaScript. In a product section `getGridDataColumns` /
`initColumnsConfig` are a small load-adding layer, not the view definition, so the static branches answer a
narrower question: *what does the section declare?* Pass `--ignore-profile` when that is the question you mean.

The command reads profile data but never writes it. It does not change packages, schemas, profile settings, or
application data. Dynamic JavaScript expressions are not executed.

### The saved profile, and why `profileScope` matters

The profile is read over the platform's own `QueryProfile` DataService route, which resolves the **active view**
(`<Section>ActiveViewSettingsProfile`) and then that view's stored grid configuration
(`<Section>GridSettings<ViewName>`). A grid stores a `listed` and a `tiled` configuration with **different sets
and orders**, so `viewType` names the one reported; when the active configuration is empty the other one is
reported instead and `notes` says so.

`QueryProfile` answers for the **calling user** and silently falls back to the shared product/system row, so the
payload alone cannot say which one it served. `profileScope` supplies that:

| Value | Meaning |
|---|---|
| `shared` | Only the product/system row exists — this is the section's shared default. |
| `user` | The calling user also has a personal row for this list, so the set may be that user's own customization. `notes` says so too. |
| `unknown` | The distinction could not be established (the contact or the row check failed). `notes` says why. |

A consumer that needs the section's canonical set must treat `user` as "ask before adopting". There is
deliberately **no** option to read another user's profile: the platform route ignores a supplied contact and
always answers for the caller, so an option promising otherwise would be a false contract.

### Column provenance, and what the merge does when a section declares both methods

Every column resolved from the section schema carries an `origin`: `getGridDataColumns`, `initColumnsConfig`,
or `both`. In Classic the two methods are not interchangeable — `initColumnsConfig` describes what the grid
**renders**, `getGridDataColumns` declares what the section **loads** — so `origin` is what lets a consumer
take the rendered set, the loaded set, or the union under its own fidelity rules instead of inheriting this
command's merge order as a ruling. The field is omitted on `entity-default` results, where no Classic method
declared the column.

The flattened `columns` array itself is produced by merging the two methods in the fixed order
`getGridDataColumns` → `initColumnsConfig`, regardless of the order they appear in the section body, and a
path declared by both is reported once at its first position. **The merge is an approximation in two ways
that are not conservative**, and both apply whenever a section declares both methods:

- **Order.** A path declared by both takes its position from `getGridDataColumns`, so load-only service
  columns lead the reported list rather than trailing it.
- **Suppression.** A most-derived layer that fully overrides `initColumnsConfig` (no `callParent`) truncates
  only its own chain — ancestor `getGridDataColumns` harvesting is untouched. A section that renders one
  column can therefore be reported with three. The rendered set does **not** survive later in the list.

When both methods are declared, `notes` carries an entry saying so, so the approximation is visible at
runtime and not only here. Use `origin` rather than position to decide what a column is.

### Subtractive overrides (`delete`) are not applied

Layer composition is additive only. A Classic override that composes its parent and then removes a key —
`var c = this.callParent(arguments); delete c.StartDate; return c;` — contributes no literal of its own, so
the removed column **survives in the reported set**. Subtraction is not applied; instead `notes` carries a
`… layer(s) remove inherited columns with 'delete' …` entry whenever such a layer is seen, so the
degradation is visible rather than silent. A result carrying that note may include columns the section hides.

### Skipped schema layers

A hierarchy layer whose body cannot be parsed as JavaScript is skipped rather than failing the command, and
`notes` then carries a `… section schema layers could not be parsed …` entry. When that note is present the
resolved `columns` may be incomplete — an unparseable most-derived layer lets an ancestor layer, or the
entity fallback, supply the answer.

## Synopsis

```bash
clio get-classic-list-columns [options]
```

## Options

```bash
--schema-name                      Classic section schema name, for example
                                   'ContactSectionV2' (required)

--ignore-profile                   Skip the saved grid profile and resolve only
                                   statically declared columns

--uri                    -u       Application uri

--Password               -p       User password

--Login                  -l       User login (administrator permission required)

--Environment            -e       Environment name

--Maintainer             -m       Maintainer name
```

## Example

```bash
clio get-classic-list-columns -e dev --schema-name AccountSectionV2
```

Example result — the set the list actually renders:

```json
{
  "success": true,
  "sectionSchema": "AccountSectionV2",
  "entity": "Account",
  "source": "profile",
  "view": "GridDataView",
  "viewType": "listed",
  "profileScope": "shared",
  "columns": [
    { "name": "Name", "caption": "Name" },
    { "name": "PrimaryContact", "caption": "Primary contact" },
    { "name": "Phone", "caption": "Primary phone" },
    { "name": "Type", "caption": "Type" },
    { "name": "AccountCategory", "caption": "Category" }
  ],
  "notes": []
}
```

The same section with the profile taken out of the resolution order:

```bash
clio get-classic-list-columns -e dev --schema-name AccountSectionV2 --ignore-profile
```

```json
{
  "success": true,
  "sectionSchema": "AccountSectionV2",
  "entity": "Account",
  "source": "schema-default",
  "columns": [
    { "name": "Name", "caption": "Name", "origin": "getGridDataColumns" },
    { "name": "PrimaryContact", "caption": "Primary contact", "origin": "getGridDataColumns" }
  ],
  "notes": []
}
```

### Captions

On a `profile` result the caption the profile itself stored wins — that is the header text the user sees,
including one they renamed — and the entity column title fills only the gaps. `origin` is omitted on a `profile`
result: no Classic column method declared those paths, so naming one would be a false claim about the body.

On the static branches `caption` is enriched from the section entity's own column metadata, keyed by direct
column name. A **dotted
lookup-traversal path** (`Account.PrimaryContact.Name`) therefore comes back with `caption` omitted — the name
belongs to another entity, and attaching the last segment's local title would be a caption from the wrong
place. Read a missing `caption` as "traversal path", not as "unknown column".

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#get-classic-list-columns)
