# create-lookup

## Command Type

    Development commands

## Name

create-lookup - Create a lookup entity schema in a remote Creatio package

## Description

The create-lookup command creates a new lookup entity schema that inherits
from BaseLookup in the specified Creatio package and registers it in Creatio's
lookup catalog so it appears in the Lookups section.

BaseLookup already provides Name and Description columns. Do not add them
via --column.

Optionally add extra columns with --column using the format
`<name>:<type>[:<title>]`, repeating the option for each column.

## Synopsis

```bash
clio create-lookup [options]
```

## Options

```bash
--package                        Target package name. Required.

--name                           Schema name (max 22 characters). Required.

--title                          Schema title. Required.

--column                         Column spec <name>:<type>[:<title>] or JSON.
                                 Repeat for multiple columns.

--Environment            -e      Environment name. Required.
```

## Output

The command prints a confirmation message when the lookup schema is created and
registered. On failure it prints the error message and exits with a non-zero
code.

## Example

```bash
clio create-lookup --package UsrOrdersApp --name UsrOrderStatus --title "Order Status" -e dev
# create a simple lookup with no extra columns

clio create-lookup --package UsrSalesApp --name UsrDealType --title "Deal Type" --column "Code:ShortText:Code" -e dev
# create a lookup with an additional Code column
```

## Notes

- --package, --name, and --title are required.
- Schema name must not exceed 22 characters.
- BaseLookup already provides Name and Description; do not add those via --column.
- cliogate must be installed in the target environment.

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#create-lookup)
