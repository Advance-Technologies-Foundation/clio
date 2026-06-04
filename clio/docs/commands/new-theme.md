# new-theme

## Name

new-theme - Add a new theme

## Description

Scaffolds a Freedom UI theme into a workspace package. Creates
`<package>/Files/themes/<cssClassName>/theme.json` and `theme.css` from the canonical
baseline (CSS custom properties scoped under `.<cssClassName>`, default font Montserrat).
The hosting package is created when it does not exist yet and reused otherwise.

A new theme is created fully from the baseline template; colour, token and font
customization are separate edits to the generated `theme.css`.

After scaffolding, deploy the package with `push-workspace` and clear the theme cache with
`clear-redis-db` so the environment picks up the new theme.

## Requirements

Themes are supported on Creatio 10.x and later.

## Synopsis

```bash
clio new-theme <CSS_CLASS_NAME> --package <PACKAGE_NAME> [OPTIONS]
```

## Options

```bash
<CSS_CLASS_NAME>
[Required] Theme CSS class name (e.g. acme-dark-theme).
Must match ^[A-Za-z][A-Za-z0-9_-]*$ and be at most 100 characters.

--package <PACKAGE_NAME>
[Required] Package name that will host the theme.

--caption <CAPTION>
Theme caption. Defaults to Title Case of the CSS class name. At most 250 characters.

--id <ID>
Theme id stored in theme.json. Defaults to a generated UUID. When provided, must match
^[A-Za-z0-9_-]+$ and be at most 100 characters.
```

## Examples

```bash
clio new-theme acme-dark-theme --package UsrThemes
Scaffold the "Acme Dark" theme (with a generated UUID id) into the UsrThemes package

clio new-theme acme-dark-theme --package UsrThemes --caption "Acme Dark" --id AcmeDark
Scaffold a theme with an explicit caption and id
```

## See Also

new-ui-project - Add a new Freedom UI project

push-workspace - Push workspace to selected environment

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#new-theme)
