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

--Environment            -e      Environment name. Required.
```

## Output

The command prints structured JSON that includes:

- application identity and primary package metadata
- section metadata before the update
- section metadata after the update

## Example

```bash
clio update-app-section --application-code UsrOrdersApp --section-code UsrOrders --caption "Orders" -e dev
replace a broken stored section heading with a plain-text caption

clio update-app-section --application-code UsrSalesApp --section-code AccountSection --description "Key customer accounts" -e dev
update the section description without changing other metadata

clio update-app-section --application-code UsrSalesApp --section-code VisitSection --icon-id 11111111-1111-1111-1111-111111111111 --icon-background "#A1B2C3" -e dev
update only the icon metadata of the selected section
```

## Notes

- `--application-code` is required.
- `--section-code` is required.
- At least one mutable field must be provided: `--caption`, `--description`, `--icon-id`, or `--icon-background`.
- Caption updates are persisted as plain text.

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#update-app-section)
