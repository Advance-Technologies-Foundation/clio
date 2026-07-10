# clear-themes-cache

## Command Type

    Theming commands

## Name

clear-themes-cache - refresh only the Creatio theme cache

## Description

`clear-themes-cache` refreshes the Creatio theme catalog cache on the target
environment. It affects only theme data and does not restart the application.

A normal package push (`push-workspace` / `push-pkg`) already refreshes the theme
registry, so this command is only needed when theme files change on the environment
outside a clio install.

## Synopsis

```bash
clio clear-themes-cache [options]
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
flush-themes
```

## Example

```bash
clio clear-themes-cache
clear the theme cache of the default environment

clio clear-themes-cache -e myapp
clear the theme cache of the environment registered as myapp
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#clear-themes-cache)
