# read-data-binding-db

## Command Type

    CI/CD commands

## Name

read-data-binding-db - report which columns a DB-first data binding actually ships

## Description

Reads a remote DB-first package data binding and reports its entity schema, the
number of bound rows, the exact set of bound columns, and each row's values.

A binding ships **only** the columns it was created with, and that projection is
the transfer contract: a column that was never passed is absent from the package,
install supplies no default for it, and the row arrives on the next environment
with that column empty. Reading the **live** record proves nothing about this —
the live row and the binding carry different column sets. This command is the
check that matters before calling a navigation change or a seed-data change done.

It replaces exporting the package and parsing `Data/<binding>/data.json`. One
expected difference: localizable columns (for example a workplace `Name`) are
listed inline here, while the package export moves them into a `Localization`
folder — so this command can report one more column than `data.json` for the same
binding.

Read-only. It also lists every bound row, which neither `execute-esq` nor the
write-side binding commands can do: `SysPackageSchemaData` holds one record per
*binding*, not per bound row.

## Synopsis

```bash
read-data-binding-db --package <PACKAGE> --binding-name <BINDING> [options]
```

## Aliases

```bash
get-data-binding-db
```

## Options

```bash
--package                       Target package name (required)

--binding-name                  Binding folder name, i.e. the SysPackageSchemaData.Name (required)

--uri               -u          Application uri

--Password          -p          User password

--Login             -l          User login (administrator permission required)

--Environment       -e          Environment name
```

## Examples

```bash
clio read-data-binding-db --package UsrTodo --binding-name SysWorkplace_Todo -e dev
```

Sample output:

```
binding: SysWorkplace_LastStand
schema:  SysWorkplace
uId:     e86d0b73-65a2-47a1-bdb9-2c12c3ac570a
rows:    1
columns (7): HomePageUId, Id, LoaderId, Name, Position, SysApplicationClientType, Type
row[0]: HomePageUId=4e48900d-..., Id=0e743480-..., LoaderId=3707a058-..., Name=LastStand, Position=25, SysApplicationClientType=Web (195785b4-...), Type=General (000a9225-...)
```

## Exit codes

| Code | Meaning |
|---|---|
| 0 | The binding was read. |
| 1 | The package or the binding does not exist on the environment; the message says which. |

## See also

- [`create-data-binding-db`](create-data-binding-db.md) — create a binding
- [`upsert-data-binding-row-db`](upsert-data-binding-row-db.md) — change a bound row
- [`remove-data-binding-row-db`](remove-data-binding-row-db.md) — **deletes the live record**, not just the binding
