# set-background-color

## Command Type

    Branding commands

## Name

set-background-color - set a solid color as the background of a custom Creatio theme

## Description

Sets a solid color (`#RGB`, `#RRGGBB` or `#RRGGBBAA`) as a custom theme's background and turns off the
panel icon background unless `--keep-icon-background` is passed. Requires Creatio 10.2.96 or later.

## Synopsis

```bash
clio set-background-color <color> --theme <theme-id> [--keep-icon-background] -e <environment>
```

`--theme` is the theme id from [`list-themes`](list-themes.md).

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#set-background-color)
