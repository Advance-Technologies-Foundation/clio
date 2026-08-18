# get-classic-list-columns

## Command Type

    Development commands

## Name

get-classic-list-columns - Resolve the effective default columns of a Classic section list

## Description

The `get-classic-list-columns` command resolves a Classic section's default visible column set through
read-only Creatio APIs. It returns JSON with the requested `sectionSchema`, resolved `entity`, ordered
`columns`, explanatory `notes`, and one of these source values:

- `schema-default` — static paths declared by `getGridDataColumns` or `initColumnsConfig` in the section hierarchy;
- `entity-default` — the entity primary display column, used when the section has no static column declaration;
- `none` — neither source exists. This is a successful result with an empty `columns` array.

The command intentionally does not read or write `SysProfileData`. It does not change packages, schemas,
profile settings, or application data. Dynamic JavaScript expressions are not executed.

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

--uri                    -u       Application uri

--Password               -p       User password

--Login                  -l       User login (administrator permission required)

--Environment            -e       Environment name

--Maintainer             -m       Maintainer name
```

## Example

```bash
clio get-classic-list-columns -e dev --schema-name ContactSectionV2
```

Example result:

```json
{
  "success": true,
  "sectionSchema": "ContactSectionV2",
  "entity": "Contact",
  "source": "entity-default",
  "columns": [
    { "name": "Name", "caption": "Full name" }
  ],
  "notes": [
    "The section schema does not define static list columns; using the entity primary display column."
  ]
}
```

### Captions

`caption` is enriched from the section entity's own column metadata, keyed by direct column name. A **dotted
lookup-traversal path** (`Account.PrimaryContact.Name`) therefore comes back with `caption` omitted — the name
belongs to another entity, and attaching the last segment's local title would be a caption from the wrong
place. Read a missing `caption` as "traversal path", not as "unknown column".

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#get-classic-list-columns)
