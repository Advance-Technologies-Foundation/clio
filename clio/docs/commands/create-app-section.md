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

The section code is generated from the caption. Only ASCII letters and
digits contribute to the generated code: a fully non-Latin caption (for
example "Контакти") yields no code and requires an explicit --code. When a
caption mixes scripts (for example "Контакти 2024"), the ASCII fragment is
salvaged ("2024" → "Usr_2024"); pass --code when the salvaged result is
not what you want.

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

--caption-culture                Override the culture used when displaying the
                                 created section caption (e.g. en-US, uk-UA).
                                 Precedence: override > the connected user's
                                 profile culture (see get-user-culture) > en-US.
                                 The stored caption is localized server-side
                                 under the connected user's profile.

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
  "Usr", or "Orders" when the setting is empty). Only ASCII letters and digits
  feed the generated code. A fully non-Latin caption (for example "Контакти")
  yields no code and must be accompanied by an explicit --code. A mixed caption
  (for example "Контакти 2024") salvages the ASCII fragment ("2024" →
  "Usr_2024"); pass --code to override.
- --code overrides the generated code. It is prefixed with the SchemaNamePrefix
  when it does not already start with it, and the prefix casing is always
  canonicalized (for example --code usrContacts with prefix Usr gives
  UsrContacts). The code must contain only Latin letters, digits, or underscore.

## Timeout budget and failure classification

The section insert runs under a finite budget of **90 seconds**. The default is
deliberately shorter than a typical MCP client's per-request timeout (around
180 seconds for some agent CLIs) so that, when Creatio is slow, clio returns its
structured `creatio-timeout` response *before* the client abandons the call with
an opaque transport error (ENG-91540). Set the
`CLIO_CREATE_SECTION_TIMEOUT_SECONDS` environment variable (whole seconds,
positive integer) to raise the budget for patient clients or large
environments; invalid or non-positive values fall back to the default.

On failure clio classifies the outcome (ENG-90679) and prints an actionable
next step (`Next step: ...`). The MCP tool surfaces the same classification as
structured fields: `error-class`, `section-created` (`true`/`false`/`unknown`),
and `retry-guidance`.

| `error-class` | Meaning | Retry decision |
| --- | --- | --- |
| `transport` | The request never reached the Creatio server (DNS, connect, or TLS failure). | No section was created; retrying is safe once the environment is reachable. |
| `creatio-timeout` | The request was sent but Creatio produced no response within the budget. | clio automatically checks whether the section appeared anyway: if it did, the command continues and succeeds. If not, do **not** retry blindly — the server may still be processing the insert. Wait a few minutes, run `clio list-app-sections`, and retry only if the section is still absent. |
| `server-error` | Creatio rejected the operation (HTTP error, non-JSON/HTML response, or a rejected insert). | Retrying the same arguments will most likely fail again; fix the inputs or the server state first. |

### MCP response deadline (`in-progress`)

Some MCP clients (for example GitHub Copilot CLI) enforce a **hard ~180 s
per-request ceiling that progress notifications do not reset**, so on a cold or
large environment the whole `create-app-section` call can exceed it and the
client abandons the request with an opaque `-32001 Request timed out` (ENG-91316).
To stay under that ceiling the MCP tool bounds its **response** by a wall-clock
deadline (default **150 s**, override with `CLIO_MCP_RESPONSE_DEADLINE_SECONDS`,
whole seconds, `0 < n ≤ 600`). When the work exceeds the deadline the tool returns
`error-class: creatio-timeout` with `section-created: in-progress` **before** the
client gives up, while the section keeps being created in the background on the
long-lived clio MCP server.

`section-created: in-progress` is **not** a failure: do **not** retry
`create-app-section` (a retry would create a duplicate) and do **not** fall back
to `create-page` / `sync-pages`. Wait briefly, then poll `list-app-sections` /
`get-app-info` until the section and its generated `<Code>_ListPage` /
`<Code>_FormPage` appear. This deadline applies only to the MCP surface; the CLI
command returns synchronously.

Failures during the preparation reads (before the insert is attempted) are
classified with the same rules but are always side-effect-free and safe to
retry once the underlying issue is resolved.

## Troubleshooting

- **"Caption ... has no Latin letters or digits to generate a section code"** —
  the caption is non-Latin (for example "Контакти"), so a valid Latin section
  code cannot be generated. Pass an explicit code via `--code` (for example
  `--code Contacts`), or use a Latin caption. The caption stays as the
  localized display title. clio reports this before creating the section.
- **"caption: the '...' value ... contains non-Latin characters ..."** — the
  caption is written in a script that does not match the connected user's
  profile culture (for example Cyrillic text while the profile is the
  Latin-script `en-US`). The stored caption is localized under the profile
  language, so this would render foreign-language labels. Author the caption in
  the profile language. Note that `--caption-culture` only changes which value
  the readback surfaces, not the stored language, so it is **not** an escape
  hatch here. clio reports this before creating the section.
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
