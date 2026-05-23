# list-page-templates

## Command Type

    Development commands

## Name

list-page-templates - List Freedom UI page templates available for create-page

**Aliases:** `page-templates`, `page-templates-list`

## Description

The list-page-templates command returns the catalog of Freedom UI page
templates advertised by the target Creatio environment through
`/rest/schema.template.api/templates`. The visible subset depends on
platform feature flags such as `ShowSidebarTemplate`,
`UseListPageV3Template`, and `UseMobilePageDesigner`.

Use this command before calling `create-page` to discover the valid
`--template` values for the current environment. Each entry exposes the
template `name`, `uId`, `title`, `groupName`, and `schemaType` (9 for
web pages, 10 for mobile pages).

## Synopsis

```bash
clio list-page-templates [options]
```

## Options

```bash
--schema-type                      Optional filter: 'web' (FreedomUIPage=9)
                                   or 'mobile' (MobilePage=10). Defaults to all

--uri                    -u       Application uri

--Password               -p       User password

--Login                  -l       User login (administrator permission required)

--Environment            -e       Environment name

--Maintainer             -m       Maintainer name
```

## Example

```bash
clio list-page-templates -e dev
list every Freedom UI template (web and mobile) visible in the dev environment

clio list-page-templates --schema-type web -e dev
list only web Freedom UI templates visible in the dev environment

clio list-page-templates --schema-type mobile -e dev
list only mobile Freedom UI templates visible in the dev environment
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#list-page-templates)
