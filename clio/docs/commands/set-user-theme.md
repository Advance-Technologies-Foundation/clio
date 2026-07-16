# set-user-theme

## Command Type

    Theming commands

## Name

set-user-theme - apply a theme to the current user's profile

## Description

`set-user-theme` applies a Creatio theme to the profile of the user clio is
authenticated as, by updating the virtual `SysUserProfile` entity through the
DataService (the same mechanism the Freedom UI profile page uses). The change
becomes visible on the user's next page refresh — no cache flush is required.

The theme is matched, case-insensitively and in this order, against a theme's
`id`, `css-class-name`, or `caption` (as reported by [`list-themes`](list-themes.md)).
Whichever selector you use, the theme's **`id`** is the value written to the
profile — the Freedom UI Shell maps that id to the theme's css class on the next
page load, so writing the css class name directly would silently fall back to the
default theme.

Requires Creatio 10.0.0 or later on the target environment, the
`CanCustomizeBranding` license, and the `CanChangeOwnTheme` system operation
(granted to all employees by default). The server-side `ChangeTheme` feature
must be enabled; when it is off the write is silently ignored, so clio reads the
value back after writing and reports an error rather than a false success.

Use `--reset` to clear the user's theme selection and fall back to the
environment default (the `DefaultTheme` system setting, or the built-in default).
`--reset` is mutually exclusive with the theme argument.

## Synopsis

```bash
clio set-user-theme <theme> [options]
clio set-user-theme --reset [options]
```

## Options

```bash
<theme>                         Theme to apply: id, css-class-name, or caption (case-insensitive), as reported by list-themes.

--reset                         Clear the user's theme, restoring the environment default. Mutually exclusive with the theme argument.

--uri               -u          Application uri

--Password          -p          User password

--Login             -l          User login (administrator permission required)

--Environment       -e          Environment name

--Maintainer        -m          Maintainer name
```

## Example

```bash
clio set-user-theme "Ocean" -e myapp
apply the theme named 'Ocean' to the current user on the environment registered as myapp

clio set-user-theme ocean-theme -e myapp
apply a theme by its css-class-name

clio set-user-theme --reset -e myapp
clear the user's theme and fall back to the environment default
```

## Notes on visibility

The change is written to the user's profile immediately, but the Freedom UI Shell
only reads the active theme at page load. An already-open session keeps its current
theme until the page is refreshed; a fresh load (or refresh) picks up the new theme.
No cache flush is required.

## Notes

- This applies the theme to the authenticated user only. To make a theme the
  default for **all** users, set the `DefaultTheme` system setting instead with
  [`set-syssetting`](set-syssetting.md).
- Create a custom theme first with [`create-theme`](create-theme.md).

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#set-user-theme)
