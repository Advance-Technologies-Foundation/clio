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

By default Creatio mints a new canonical entity for the application's primary
section. Pass --entity-schema-name `<Name>` to build that section over an entity
that **already exists** in the environment instead; Creatio then suppresses the
new entity. This is the only way to get an application whose primary section
sits on an existing object — do not follow create-app with create-app-section
for the primary section, which leaves the starter pages of the unused canonical
entity behind. --app-section-description sets the description of the primary
section and can be used on its own.

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

--entity-schema-name             Name of an EXISTING entity schema the primary
                                 section is built over. Optional. The entity
                                 must already exist in the environment; clio
                                 sends it together with
                                 useExistingEntitySchema=true, so no canonical
                                 entity is created.

--app-section-description        Description applied to the application's
                                 primary section. Optional.

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

clio create-app --name "Orders" --code OrdersApp --template-code AppFreedomUI --entity-schema-name UsrExistingOrder -e dev
# build the primary section over the existing UsrExistingOrder entity instead of a newly created one
```

## Notes

- --name, --code, and --template-code are required.
- The active `SchemaNamePrefix` system setting is read from the target environment and prepended to the code automatically. The default prefix is `Usr`. Passing the full prefixed code (e.g. `UsrOrdersApp`) also works — the prefix is not duplicated.
- --icon-background must be one of the Freedom UI palette colors when provided; a random palette color is assigned when omitted.
- When --icon-id is omitted the command does not assign an icon automatically.
- --with-mobile-pages defaults to `true`; existing calls without the flag keep generating the full five-page set. Pass `false` for a web-only app to skip the main entity `_MobileFormPage` and `_MobileListPage`. An explicit client type takes precedence over this flag.
- --entity-schema-name maps to the CreateApp `optionalTemplateData` payload: clio always sends `useExistingEntitySchema: true` alongside `entitySchemaName`, so the "both fields or neither" rule of the underlying service cannot be violated from the CLI. The entity must already exist in the environment before the call — create-app does not create it, and a missing entity fails server-side.
- --app-section-description maps to `optionalTemplateData.appSectionDescription` and is independent of --entity-schema-name. When neither option is passed clio sends an empty `optionalTemplateData`, exactly as before these options existed.
- `useAIContentGeneration` is deliberately not exposed: it is rejected by the MCP `create-app` tool as well.
- The two application commands use different endpoints, which explains their different timing and options: create-app posts to `ServiceModel/AppInstallerService.svc/CreateApp` (the platform's application generator), while create-app-section posts to `DataService/json/SyncReply/InsertQuery` (a direct insert into the section tables) and therefore accepts no template arguments.
- --name and --description must be in the connected user's profile language. The application name is localized server-side under the profile, so a value whose script does not match a Latin-script profile (for example Cyrillic under an `en-US` profile) is rejected with an actionable error.

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#create-app)
