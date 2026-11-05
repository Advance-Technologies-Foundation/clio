# update-theme

## Command Type

    Theming commands

## Name

update-theme - overwrite an existing custom Creatio theme on an environment, or change its background

## Description

`update-theme` overwrites an existing custom theme on the target environment via the
native `ThemeService.svc/UpdateTheme` endpoint. The theme is located by `--id` and
rewritten in its current package; the package cannot be changed.

Without a background argument this is a **full overwrite**: `--caption`, `--css-class-name`, and the theme CSS are all
required. Requires Creatio 10.0.0 or later on the target environment, the
`CanCustomizeBranding` license, and the `CanManageThemes` system operation.

Provide the CSS through exactly one of `--css-content` (inline) or `--css-content-file`
(a UTF-8 file, up to 1 MiB).

Pass at most one background argument: `--background-image-id` (an image uploaded with
[`upload-image`](upload-image.md)), `--background-color` or `--remove-background`. It needs Creatio 10.2.96 or
later, checked before any write. With one, `--caption`, `--css-class-name` and the CSS are optional and the
omitted ones are kept. After an image or a color is written, the `UsePanelIconBackground` feature is turned off for
All employees unless `--keep-icon-background` is passed, and an image is bound into the theme's package.

## Synopsis

```bash
clio update-theme --id <id> --caption <caption> --css-class-name <name> [options]
clio update-theme --id <id> --background-color <color> [options]
```

## Options

```bash
--id                            Id (a GUID) of the existing theme to overwrite (required)

--caption                       Human-readable theme caption, max 250 (required without a background argument)

--css-class-name                CSS class applied when active (^[A-Za-z][A-Za-z0-9_-]*$, max 100) (required without a background argument)

--css-content                   Inline theme CSS, max 1 MiB (mutually exclusive with --css-content-file)

--css-content-file              Path to a UTF-8 CSS file (mutually exclusive with --css-content)

--background-image-id           Id of an image uploaded with upload-image, set as the theme background

--background-color              Solid theme background color: #RGB, #RRGGBB or #RRGGBBAA

--remove-background             Remove the theme's own background; the image binding stays in the package

--keep-icon-background          Keep the UsePanelIconBackground feature as is

--uri               -u          Application uri

--Password          -p          User password

--Login             -l          User login (administrator permission required)

--Environment       -e          Environment name

--Maintainer        -m          Maintainer name
```

## Example

```bash
clio update-theme --id d53a816b-65ed-4b5b-ad3b-f739280add45 --caption "Ocean" --css-class-name ocean-theme --css-content ".ocean-theme{color:#039}" -e myapp
overwrite the theme 'ocean-theme' with new inline CSS

clio update-theme --id d53a816b-65ed-4b5b-ad3b-f739280add45 --caption "Ocean" --css-class-name ocean-theme --css-content-file ./theme.css -e myapp
overwrite the theme reading the CSS from a file

clio update-theme --id d53a816b-65ed-4b5b-ad3b-f739280add45 --background-image-id 6d2e4b1f-93a8-4c57-b0e1-2f7a9c3d5e81 -e myapp
give the theme an image background and keep its caption, class name and CSS
```

## Notes

- Find a theme's `id` with [`list-themes`](list-themes.md).

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#update-theme)
