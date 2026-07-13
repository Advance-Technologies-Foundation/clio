# list-themes

## Command Type

    Theming commands

## Name

list-themes - list the custom Creatio themes available on an environment

## Description

`list-themes` prints the custom themes available on the target environment,
reading them from the native `ThemeService.svc/GetAvailableThemes` endpoint.
For each theme it shows the `id`, `caption`, `cssClassName`, and `cssFilePath`.

The command requires Creatio 10.0.0 or later on the target environment and the
`CanCustomizeBranding` license; a caller without the license receives an empty
list rather than an error. It is read-only and does not change the environment.

## Synopsis

```bash
clio list-themes [options]
```

## Options

```bash
--uri               -u          Application uri

--Password          -p          User password

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
list the themes of the default environment

clio list-themes -e myapp
list the themes of the environment registered as myapp
```

## Notes

- A theme deployed by a clio push appears here automatically; run [`clear-themes-cache`](clear-themes-cache.md) only when a theme was changed on the environment outside a clio install and is missing here.

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#list-themes)
