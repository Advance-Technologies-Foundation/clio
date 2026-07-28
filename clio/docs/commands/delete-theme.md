# delete-theme

## Command Type

    Theming commands

## Name

delete-theme - delete a custom Creatio theme from an environment

## Description

`delete-theme` removes a custom theme from the target environment via the native
`ThemeService.svc/DeleteTheme` endpoint. The theme is located by `--id`.

Requires Creatio 10.0.0 or later on the target environment, the `CanCustomizeBranding`
license, and the `CanManageThemes` system operation.
Deleting an unknown id is reported as a failure (it is **not idempotent**).

`delete-theme` does not change the `DefaultTheme` system setting. If you delete the
theme that is currently the default, reset `DefaultTheme` to another theme id yourself
(or clear it; an empty value falls back to the stock theme).

## Synopsis

```bash
clio delete-theme --id <id> [options]
```

## Options

```bash
--id                            Id of the theme to delete (required)

--uri               -u          Application uri

--Password          -p          User password

--Login             -l          User login (administrator permission required)

--Environment       -e          Environment name

--Maintainer        -m          Maintainer name
```

## Example

```bash
clio delete-theme --id ocean-theme -e myapp
delete the theme 'ocean-theme' from the environment registered as myapp
```

## Notes

- Find a theme's `id` with [`list-themes`](list-themes.md).

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#delete-theme)
