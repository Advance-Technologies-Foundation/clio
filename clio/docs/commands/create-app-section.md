# create-app-section

## Command Type

    Application management commands

## Name

create-app-section - Create a section inside an existing installed application

## Description

The create-app-section command creates a new section shell inside an
existing installed Creatio application and returns structured readback
data for the created section, resolved entity, and created pages.

Provide the target application through --application-code.

By default clio creates a section with a new object. To bind the section
to an existing entity, provide --entity-schema-name.

By default clio creates web and mobile pages. Set --with-mobile-pages false
when the new section must stay web-only.

Clio always selects an application icon automatically and always generates
the icon background color automatically.

## Synopsis

```bash
clio create-app-section [options]
```

## Options

```bash
--application-code               Installed application code. Required.

--caption                        Section caption. Required.

--description                    Section description

--entity-schema-name             Existing entity schema name

--with-mobile-pages              Create mobile pages in addition to web
                                 pages. Default: true

--Environment            -e      Environment name. Required.
```

## Output

The command prints structured JSON that includes:

- application identity and primary package metadata
- created section identity and resolved entity schema name
- resolved entity summary when available
- created page summaries when available

## Example

```bash
clio create-app-section --application-code UsrOrdersApp --caption "Orders" -e dev
create a section with a new object inside the installed UsrOrdersApp application

clio create-app-section --application-code UsrSalesApp --caption "Accounts" --entity-schema-name Account -e dev
create a section bound to the existing Account entity in the selected application

clio create-app-section --application-code UsrSalesApp --caption "Visits" --with-mobile-pages false -e dev
create a web-only section with automatically resolved icon metadata
```

## Notes

- --application-code is required.
- When --entity-schema-name is provided, the section reuses that entity.
- The section code is generated automatically from the caption.

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#create-app-section)
