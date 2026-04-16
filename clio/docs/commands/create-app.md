# create-app

## Command Type

    Application management commands

## Name

create-app - Create a new application in Creatio

## Description

The create-app command creates a new Creatio application using the specified
template and returns the identity of the created application and its primary
package.

Provide the display name with --name and a unique application code starting
with the Usr prefix with --code.

Choose the application template with --template-code. Known values are:
AppFreedomUIv2, AppFreedomUI, AppWithHomePage, EmptyApp.

Optionally supply --icon-background in #RRGGBB format and --icon-id as a GUID
or the special value 'auto' to let Creatio choose a random icon.

## Synopsis

```bash
clio create-app [options]
```

## Options

```bash
--name                           Application display name. Required.

--code                           Application code starting with Usr prefix.
                                 Required.

--template-code                  Technical template name. Required.
                                 Known values: AppFreedomUIv2, AppFreedomUI,
                                 AppWithHomePage, EmptyApp

--icon-background                Icon background color in #RRGGBB format

--description                    Application description

--icon-id                        Application icon GUID or 'auto' to pick a
                                 random icon

--Environment            -e      Environment name. Required.
```

## Output

The command prints the created application name, code, version, and the name
of the primary package that was created together with the application.

## Example

```bash
clio create-app --name "My Orders App" --code UsrOrdersApp --template-code AppFreedomUIv2 -e dev
# create a Freedom UI v2 application in the dev environment

clio create-app --name "Sales" --code UsrSalesApp --template-code EmptyApp --icon-background "#1F5F8B" -e dev
# create an empty application with a custom icon background color
```

## Notes

- --name, --code, and --template-code are required.
- The application code must start with the Usr prefix.
- When --icon-id is omitted the command does not assign an icon automatically.

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#create-app)
