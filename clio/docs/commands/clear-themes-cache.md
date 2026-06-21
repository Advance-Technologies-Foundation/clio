# clear-themes-cache

## Command Type

    CI/CD commands

## Name

clear-themes-cache - refresh only the Creatio theme cache

## Description

`clear-themes-cache` refreshes the Creatio theme catalog cache on the target
environment. It affects only theme data and does not restart the application.

Typical use: after pushing a package that contains a theme (`push-workspace` /
`push-pkg`), run `clear-themes-cache`.

## Synopsis

```bash
clear-themes-cache [Name] [options]
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
flush-themes
```

## Example

```bash
clio clear-themes-cache
clear the theme cache of the current web application(website)

clio clear-themes-cache -e myapp
clear the theme cache of the environment registered as myapp
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#clear-themes-cache)
