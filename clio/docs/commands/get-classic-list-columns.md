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

### Column order when a section declares both methods

`getGridDataColumns` and `initColumnsConfig` are merged in that fixed order, regardless of the order they
appear in the section body, and a path declared by both is reported once at its first position. In Classic
these two methods are not interchangeable — `initColumnsConfig` describes what the grid renders while
`getGridDataColumns` declares what the section loads — so a section declaring both can report columns that
are loaded but never rendered, ahead of the rendered ones. Consumers that need the strictly displayed set
should treat this case as approximate.

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
