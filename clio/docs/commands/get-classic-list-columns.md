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

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#get-classic-list-columns)
