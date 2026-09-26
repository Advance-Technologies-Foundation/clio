# update-app-section

## Command Type

    Application management commands

## Name

update-app-section - Update metadata of a section inside an existing installed application

## Description

The update-app-section command updates selected metadata of an existing
section inside an installed Creatio application and returns structured
readback data for the application and section before and after the update.

Provide the target application through `--application-code` and the target
section through `--section-code`.

The command supports partial updates. Omitted fields remain unchanged.

Use `--caption` to replace a broken JSON-style heading with a proper
plain-text section caption.

Use `--caption` with `--caption-culture` to write the section title in another
language (for example `es-ES`). Only that language changes; the title in the
other languages is kept. The culture must exist in the Languages section: an
unknown culture fails before anything is written, an inactive one is written
and reported as a warning.

Creatio deletes the section title and description in every non-default
language on each section update. The command reads them first and writes them
back afterwards, then re-saves the application package's
`SysModule_<SectionCode>` data binding so the translations stay part of the
package.

## Synopsis

```bash
clio update-app-section [options]
```

## Options

```bash
--application-code               Installed application code. Required.

--section-code                   Section code inside the installed
                                 application. Required.

--caption                        Updated section caption

--description                    Updated section description

--icon-id                        Updated section icon GUID

--icon-background                Updated section icon background in
                                 #RRGGBB format

--caption-culture                Culture --caption is written in, for
                                 example es-ES. Default: the connected
                                 user's profile culture, else en-US.
                                 Requires --caption

--Environment            -e      Environment name. Required.
```

## Output

The command prints structured JSON that includes:

- application identity and primary package metadata
- section metadata before the update
- section metadata after the update (caption in the connected user's profile culture)
- `CaptionCulture` / `CaptionCultureValue` — the culture the caption was written in and the stored value
- `PreservedCultures` — the non-default languages whose other stored values (title, description) were kept; it can include `CaptionCulture` when its description was kept
- `Warnings` — for example an inactive culture, or a package data binding that could not be re-saved

## Example

```bash
clio update-app-section --application-code UsrOrdersApp --section-code UsrOrders --caption "Orders" -e dev
replace a broken stored section heading with a plain-text caption

clio update-app-section --application-code UsrSalesApp --section-code AccountSection --description "Key customer accounts" -e dev
update the section description without changing other metadata

clio update-app-section --application-code UsrSalesApp --section-code VisitSection --icon-id 11111111-1111-1111-1111-111111111111 --icon-background "#A1B2C3" -e dev
update only the icon metadata of the selected section

clio update-app-section --application-code UsrOrdersApp --section-code UsrOrders --caption "Pedidos" --caption-culture es-ES -e dev
add the Spanish section title; the English and other titles are kept
```

## Notes

- `--application-code` is required.
- `--section-code` is required.
- At least one mutable field must be provided: `--caption`, `--description`, `--icon-id`, or `--icon-background`.
- Caption updates are persisted as plain text.
- `--caption` must be in the language it is written in: `--caption-culture` when given, otherwise the connected user's profile language. `--description` is always written in the profile language. A value whose script does not match (for example Cyrillic under `en-US`) is rejected with an actionable error.
- `--caption-culture` requires `--caption` and writes one language per call. Localization maps are not accepted.
- `--caption-culture` is looked up in the environment's cultures (`SysCulture`) only, case-insensitively; the stored spelling is used (`de-de` → `de-DE`).
- If writing the other languages back fails after the section update deleted them, the command fails and the error lists every value it read before the update (culture → title, description, module header), so they can be re-sent.

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#update-app-section)
