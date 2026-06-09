# create-app

## Command Type

    Application management commands

## Name

create-app - Create a new application in Creatio

## Description

The create-app command creates a new Creatio application using the specified
template and returns the identity of the created application and its primary
package.

Provide the display name with --name and a unique application code with
--code. The code should be the business-meaningful part without the prefix
(e.g. "OrdersApp"). clio reads the `SchemaNamePrefix` system setting from the
target environment and prepends it automatically; the default prefix is "Usr".
Passing the full prefixed code (e.g. "UsrOrdersApp") also works — the prefix
is not duplicated.

Choose the application template with --template-code. Known values are:
AppFreedomUIv2, AppFreedomUI, AppWithHomePage, EmptyApp.

Optionally supply --icon-background as one of the Freedom UI palette colors and
--icon-id as a GUID or the special value 'auto' to let Creatio choose a random
icon. When --icon-background is omitted a random palette color is assigned
automatically.

By default create-app generates the full set of five pages, including the main
entity `_MobileFormPage` and `_MobileListPage`. Pass --with-mobile-pages false
to create a web-only application; the two mobile pages are then skipped.

## Synopsis

```bash
clio create-app [options]
```

## Options

```bash
--name                           Application display name. Required.

--code                           Application code. clio reads SchemaNamePrefix
                                 from the environment and applies it
                                 automatically; pass the business-meaningful
                                 part only (e.g. "OrdersApp"). Required.

--template-code                  Technical template name. Required.
                                 Known values: AppFreedomUIv2, AppFreedomUI,
                                 AppWithHomePage, EmptyApp

--icon-background                Freedom UI palette color in #RRGGBB format.
                                 Optional; a random palette color is used
                                 when omitted.

--description                    Application description

--icon-id                        Application icon GUID or 'auto' to pick a
                                 random icon

--with-mobile-pages              Create mobile pages (_MobileFormPage,
                                 _MobileListPage) for the main entity in
                                 addition to web pages. Optional; defaults to
                                 true. Pass false for a web-only application.

--Environment            -e      Environment name. Required.
```

## Output

The command prints the created application name, code, version, and the name
of the primary package that was created together with the application.

## Example

```bash
clio create-app --name "My Orders App" --code OrdersApp --template-code AppFreedomUIv2 -e dev
# create a Freedom UI v2 application; clio prepends the active SchemaNamePrefix automatically

clio create-app --name "Sales" --code SalesApp --template-code EmptyApp --icon-background "#0058EF" -e dev
# create an empty application with a specific Freedom UI palette color

clio create-app --name "Web Portal" --code WebPortal --template-code AppFreedomUI --with-mobile-pages false -e dev
# create a web-only application without the main entity mobile pages
```

## Notes

- --name, --code, and --template-code are required.
- The active `SchemaNamePrefix` system setting is read from the target environment and prepended to the code automatically. The default prefix is `Usr`. Passing the full prefixed code (e.g. `UsrOrdersApp`) also works — the prefix is not duplicated.
- --icon-background must be one of the Freedom UI palette colors when provided; a random palette color is assigned when omitted.
- When --icon-id is omitted the command does not assign an icon automatically.
- --with-mobile-pages defaults to `true`; existing calls without the flag keep generating the full five-page set. Pass `false` for a web-only app to skip the main entity `_MobileFormPage` and `_MobileListPage`. An explicit client type takes precedence over this flag.

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#create-app)
