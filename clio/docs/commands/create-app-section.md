# create-app-section

## Command Type

    Application management commands

## Name

create-app-section - Create a section inside an existing installed application

## Description

The create-app-section command creates a new section shell inside an
existing installed Creatio application and returns structured readback
data for the created section, resolved entity, and created pages.

Provide the target application through --application-code.

By default clio creates a section with a new object. To bind the section
to an existing entity, provide --entity-schema-name. The object must exist
(clio validates this before creating the section and returns a clear,
actionable error otherwise). Several sections may target the same object, so
reusing an object that already backs a section is allowed.

The section code is generated from the caption. A non-Latin caption (for
example "Контакти") cannot produce a valid Latin code, so pass an explicit
code via --code (for example --code Contacts); the caption stays as the
localized display title.

By default clio creates web and mobile pages. Set --with-mobile-pages false
when the new section must stay web-only.

Clio always selects an application icon automatically. Use --icon-background
to set a specific icon background color from the Freedom UI palette, or omit
it to pick a random palette color. Values outside the palette are rejected
because the app manager UI only renders palette colors as tile gradients.

Valid palette colors (16 gradients rendered by the app manager UI):
#A6DE00, #20A959, #22AC14, #FFAC07, #FF8800, #F9307F, #FF602E, #FF4013,
#B87CCF, #7848EE, #247EE5, #0058EF, #009DE3, #4F43C2, #08857E, #00BFA5.

## Synopsis

```bash
clio create-app-section [options]
```

## Options

```bash
--application-code               Installed application code. Required.

--caption                        Section caption. Required.

--description                    Section description

--entity-schema-name             Existing entity schema name

--code                           Explicit section code (Latin identifier).
                                 Generated from the caption when omitted;
                                 required when the caption has no Latin
                                 letters or digits (for example a non-Latin
                                 caption such as "Контакти").

--icon-background                Icon background color in #RRGGBB format.
                                 Must match one of the Freedom UI palette
                                 values listed above. Defaults to a random
                                 palette color when omitted.

--with-mobile-pages              Create mobile pages in addition to web
                                 pages. Default: true

--Environment            -e      Environment name. Required.
```

## Output

The command prints structured JSON that includes:

- application identity and primary package metadata
- created section identity and resolved entity schema name
- resolved entity summary when available
- created page summaries when available

## Example

```bash
clio create-app-section --application-code UsrOrdersApp --caption "Orders" -e dev
create a section with a new object inside the installed UsrOrdersApp application

clio create-app-section --application-code UsrSalesApp --caption "Accounts" --entity-schema-name Account -e dev
create a section bound to the existing Account entity in the selected application

clio create-app-section --application-code UsrSalesApp --caption "Visits" --with-mobile-pages false -e dev
create a web-only section with automatically resolved icon metadata
```

## Notes

- --application-code is required.
- When --entity-schema-name is provided, the section reuses that entity. The
  object must exist (validated before creation); several sections may target
  the same object, so reuse is allowed.
- The section code is generated automatically from the caption and prefixed
  with the active SchemaNamePrefix system setting from the target environment
  (for example caption "Orders" produces "UsrOrders" when SchemaNamePrefix is
  "Usr", or "Orders" when the setting is empty). Only Latin letters and digits
  feed the generated code; a non-Latin caption (for example "Контакти") yields
  no code and must be accompanied by an explicit --code.
- --code overrides the generated code. It is prefixed with the SchemaNamePrefix
  when it does not already start with it, and must be a Latin identifier (start
  with a letter; letters, digits, or underscore only).

## Troubleshooting

- **"Caption ... has no Latin letters or digits to generate a section code"** —
  the caption is non-Latin (for example "Контакти"), so a valid Latin section
  code cannot be generated. Pass an explicit code via `--code` (for example
  `--code Contacts`), or use a Latin caption. The caption stays as the
  localized display title. clio reports this before creating the section.
- **"Entity schema ... does not exist ..."** — the object passed via
  --entity-schema-name was not found in the environment. Object names are
  case-sensitive. Verify the name, or omit --entity-schema-name to create a new
  object. clio checks this before creating the section.
- **"Section code ... is invalid ..."** — the value passed via `--code` is not
  a Latin identifier. Section codes must start with a Latin letter and contain
  only Latin letters, digits, or underscore.
- **Detail-less "Failed to create section ..." rejection** — the server
  rejected the insert without details. A section with the generated or explicit
  code may already exist. Run `clio list-app-sections` to inspect existing
  sections, then change the caption or pass a different `--code`. Several
  sections may target the same object, so reusing an object that already backs a
  section is allowed.
- When the underlying Creatio insert returns its own error text, that message
  is surfaced after `Server error:` so the root cause is visible instead of a
  generic failure.

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#create-app-section)
