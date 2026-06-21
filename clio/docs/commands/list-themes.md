# list-themes

## Command Type

    CI/CD commands

## Name

list-themes - list the custom Creatio themes available on an environment

## Description

`list-themes` prints the custom themes available on the target environment,
reading them from the native `ThemeService.svc/GetAvailableThemes` endpoint.
For each theme it shows the `id`, `caption`, `cssClassName`, and `cssFilePath`.

The command requires the `CanCustomizeBranding` license; a caller without it
receives an empty list rather than an error. It is read-only and does not change
the environment.

Note: a theme pushed in a package appears here only after the theme cache is
refreshed — run [`clear-themes-cache`](clear-themes-cache.md) if a just-pushed
theme is missing from the list.

## Synopsis

```bash
list-themes [Name] [options]
```

## Options

```bash
Name (pos. 0)	Application name

--uri               -u          Application uri

--Password			-p          User password

--Login             -l          User login (administrator permission required)

--Environment       -e          Environment name

--Maintainer        -m          Maintainer name
```

## Aliases

```bash
get-themes
```

## Example

```bash
clio list-themes
list the themes of the current web application(website)

clio list-themes -e myapp
list the themes of the environment registered as myapp
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#list-themes)
