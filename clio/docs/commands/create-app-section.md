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
to an existing entity, provide --entity-schema-name.

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
- When --entity-schema-name is provided, the section reuses that entity.
- The section code is generated automatically from the caption and prefixed
  with the active SchemaNamePrefix system setting from the target environment
  (for example caption "Orders" produces "UsrOrders" when SchemaNamePrefix is
  "Usr", or "Orders" when the setting is empty).

## Timeout budget and failure classification

The section insert runs under a finite budget of **300 seconds**. Set the
`CLIO_CREATE_SECTION_TIMEOUT_SECONDS` environment variable (whole seconds,
positive integer) to change the budget; invalid or non-positive values fall
back to the default.

On failure clio classifies the outcome (ENG-90679) and prints an actionable
next step (`Next step: ...`). The MCP tool surfaces the same classification as
structured fields: `error-class`, `section-created` (`true`/`false`/`unknown`),
and `retry-guidance`.

| `error-class` | Meaning | Retry decision |
| --- | --- | --- |
| `transport` | The request never reached the Creatio server (DNS, connect, or TLS failure). | No section was created; retrying is safe once the environment is reachable. |
| `creatio-timeout` | The request was sent but Creatio produced no response within the budget. | clio automatically checks whether the section appeared anyway: if it did, the command continues and succeeds. If not, do **not** retry blindly — the server may still be processing the insert. Wait a few minutes, run `clio list-app-sections`, and retry only if the section is still absent. |
| `server-error` | Creatio rejected the operation (HTTP error, non-JSON/HTML response, or a rejected insert). | Retrying the same arguments will most likely fail again; fix the inputs or the server state first. |

Failures during the preparation reads (before the insert is attempted) are
classified with the same rules but are always side-effect-free and safe to
retry once the underlying issue is resolved.

## Troubleshooting

- **"Failed to create section ... is already bound to an existing section"** —
  the entity passed via --entity-schema-name already backs another section
  (an entity can back only one section). Reuse the existing section, pick a
  different entity, or omit --entity-schema-name to create a new object. Run
  `clio list-app-sections` to inspect the sections already defined in the
  application before retrying. This commonly happens when binding a system
  entity such as Contact, which the out-of-the-box Contacts section already
  uses.
- **"Failed to create section ... a section with code ... already exists"** —
  the caption produced a section code that collides with an existing section.
  Change the caption to generate a different code, or reuse the existing
  section.
- When the underlying Creatio insert returns its own error text, that message
  is surfaced after `Server error:` so the root cause is visible instead of a
  generic failure.

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#create-app-section)
