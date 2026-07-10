# update-theme

## Command Type

    Theming commands

## Name

update-theme - overwrite an existing custom Creatio theme on an environment

## Description

`update-theme` overwrites an existing custom theme on the target environment via the
native `ThemeService.svc/UpdateTheme` endpoint. The theme is located by `--id` and
rewritten in its current package; the package cannot be changed.

This is a **full overwrite**: `--caption`, `--css-class-name`, and the theme CSS are all
required. Requires the `CanCustomizeBranding` license and the `CanManageThemes` system operation.

Provide the CSS through exactly one of `--css-content` (inline) or `--css-content-file`
(a UTF-8 file, up to 1 MiB).

## Synopsis

```bash
clio update-theme --id <id> --caption <caption> --css-class-name <name> [options]
```

## Options

```bash
--id                            Id of the existing theme to overwrite (required)

--caption                       Human-readable theme caption, max 250 (required)

--css-class-name                CSS class applied when active (^[A-Za-z][A-Za-z0-9_-]*$, max 100) (required)

--css-content                   Inline theme CSS, max 1 MiB (mutually exclusive with --css-content-file)

--css-content-file              Path to a UTF-8 CSS file (mutually exclusive with --css-content)

--uri               -u          Application uri

--Password          -p          User password

--Login             -l          User login (administrator permission required)

--Environment       -e          Environment name

--Maintainer        -m          Maintainer name
```

## Example

```bash
clio update-theme --id ocean-theme --caption "Ocean" --css-class-name ocean-theme --css-content ".ocean-theme{color:#039}" -e myapp
overwrite the theme 'ocean-theme' with new inline CSS

clio update-theme --id ocean-theme --caption "Ocean" --css-class-name ocean-theme --css-content-file ./theme.css -e myapp
overwrite the theme reading the CSS from a file
```

## Notes

- Find a theme's `id` with [`list-themes`](list-themes.md).

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#update-theme)
