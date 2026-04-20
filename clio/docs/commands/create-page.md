# create-page

## Command Type

    Development commands

## Name

create-page - Create a new Freedom UI page from a supported template

**Aliases:** `page-create`

## Description

The create-page command provisions a new Freedom UI page schema by
saving a new ClientUnit schema whose parent is one of the templates
advertised by the target Creatio environment through
`/rest/schema.template.api/templates`. Use `list-page-templates` to
discover valid template values (web and mobile).

create-page validates the input before calling the designer service:
schema-name format, template existence, package existence, schema-name
uniqueness, and optional entity schema resolution. On success it returns
the assigned SchemaUId and the resolved package / template metadata.

create-page does not run AI sampling on the new page; it relies on the
platform template body. After a successful call, use `get-page` to read
the new schema back through the canonical page workflow.

## Synopsis

```bash
clio create-page [options]
```

## Options

```bash
--schema-name                      New page schema name, e.g. UsrMyApp_BlankPage
                                   Must start with a letter and contain only
                                   letters, digits, or underscores

--template                         Template name or UId returned by
                                   list-page-templates, e.g. BlankPageTemplate

--package-name                     Target package name that will own the
                                   new page schema

--caption                          Optional display caption; defaults to
                                   schema-name

--description                      Optional schema description

--entity-schema-name               Optional entity schema to record in the
                                   new page dependencies

--dry-run                          Validate inputs and resolve references
                                   without creating the page

--uri                    -u       Application uri

--Password               -p       User password

--Login                  -l       User login (administrator permission required)

--Environment            -e       Environment name

--Maintainer             -m       Maintainer name
```

## Example

```bash
clio list-page-templates -e dev
discover valid templates in the target environment

clio create-page --schema-name UsrDemo_BlankPage --template BlankPageTemplate --package-name Custom --dry-run -e dev
validate inputs and resolve template and package without creating the page

clio create-page --schema-name UsrDemo_BlankPage --template BlankPageTemplate --package-name Custom --caption "Demo blank page" -e dev
create the Freedom UI page from the BlankPageTemplate parent

clio create-page --schema-name UsrDemo_Mobile --template BlankMobilePageTemplate --package-name Custom -e dev
create a mobile Freedom UI page from the BlankMobilePageTemplate parent
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#create-page)
