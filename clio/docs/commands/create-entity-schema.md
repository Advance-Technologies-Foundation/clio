# create-entity-schema

## Command Type

    Package Management commands

## Name

create-entity-schema - Create an entity schema in a remote Creatio package

## Synopsis

```bash
clio create-entity-schema [OPTIONS]
```

## Description

Creates an entity schema in an existing remote package using
EntitySchemaDesignerService.

The command creates a draft schema on the server, assigns a parent schema
(the same-name parent for replacements, BaseEntity otherwise), applies the requested
name, title, and initial columns, saves the result, applies the DB
structure, and publishes the
configuration so the new schema is immediately visible to lookup pickers
and sys-setting reference schema lists. No separate compile is required.
Publishing also requests an OData entities rebuild, so the schema becomes
reachable over OData (/0/odata/<Entity>) without a manual full compile;
that rebuild runs in the background and appears within a few minutes.
Use --is-virtual when the schema must not have a physical database table.
The option defaults to false.

Current clio entity-schema commands are part of the canonical clio MCP
contract. Keep using create-entity-schema / modify-entity-schema-column
rather than frontend-only aliases like entity.create / entity.update.

Supported column types:
- Guid
- Text, ShortText, MediumText, LongText, MaxSizeText
- Text50, Text250, Text500, TextUnlimited, PhoneNumber, WebLink, Email, RichText
- Binary, Image, File, SecureText (Blob is accepted as an alias for Binary; Encrypted and Password are accepted as aliases for SecureText; EmailAddress is accepted as an alias for Email)
- ImageLookup (ImageLink is accepted as an alias; references the SysImage schema automatically — no --reference-schema)
- Integer
- Float
- Decimal0, Decimal1, Decimal2, Decimal3, Decimal4, Decimal8, Currency0, Currency1, Currency2, Currency3
(Money is accepted as an alias for Currency2 — the normal two-decimal Creatio money column;
Decimal is accepted as an alias for Decimal2, same as Float)
- Boolean
- Date, DateTime, Time
- Lookup (requires --reference-schema)
- Color (stores a hex color string such as #RRGGBB; not a text column — the text-only options multiline/accent-insensitive/format-validated/masked do not apply)

For image/photo fields rendered with the crt.ImageInput Freedom UI component, use the
ImageLookup ("Image link") type. The binary Image type does not work with crt.ImageInput.

## Options

```bash
--package              Target package name. Required
--name                 Entity schema name. Required: a call without it
fails before anything is sent to the server
--title                Entity schema title/caption. Required
--parent               Parent schema name. Defaults to the schema name for
replacements, or BaseEntity otherwise
--extend-parent        Create a same-name replacement schema in the target
package. Default: false
--is-virtual           Create a virtual entity schema without a physical
database table. Default: false
--is-db-view           true maps the entity to a database view, false
clears the flag; omit to keep the inherited value.
No table or SQL view is generated; provision the
view separately
--column               Column definition in format
<name>:<type>[:<title>[:<refSchema>]]
or JSON with name/type/title or caption/
reference-schema-name/required/
legacy default-value-source/default-value or
structured default-value-config.
Repeat the option for multiple columns, pass multiple values
after one option, or pass a non-empty JSON array of objects.
Invalid array entries fail before saving the schema.
--caption-culture      Override the culture used for the generated schema and
column captions/labels (e.g. en-US, uk-UA). Precedence:
this override > the connected user's profile culture >
en-US. Supplying it skips the profile-culture lookup.

Environment options are also available:
-e, --environment      Environment name from the registered configuration
-u, --uri              Application URI
-l, --login            User login
-p, --password         User password
```

## Examples

```bash
# Create a bare entity schema (inherits BaseEntity by default)
clio create-entity-schema -e dev --package Custom --name UsrVehicle --title "Vehicle"

# Create an entity schema with columns
clio create-entity-schema -e dev --package Custom --name UsrVehicle --title "Vehicle" --column "Name:Text:Name"

# Create several columns: repeat --column, or pass one JSON array
clio create-entity-schema -e dev --package Custom --name UsrVehicle --title "Vehicle" --column "Notes:Text" --column "Amount:Integer"
clio create-entity-schema -e dev --package Custom --name UsrInvoice --title "Invoice" --column "[{\"name\":\"Notes\",\"type\":\"Text\"},{\"name\":\"Amount\",\"type\":\"Integer\",\"required\":true}]"

# Create a lookup column
clio create-entity-schema -e dev --package Custom --name UsrVehicle --title "Vehicle" --column "Owner:Lookup:Owner:Contact"

# Create a column with frontend-style type alias and default metadata
clio create-entity-schema -e dev --package Custom --name UsrVehicle --title "Vehicle" --column "{\"name\":\"Status\",\"type\":\"ShortText\",\"title\":\"Status\",\"required\":true,\"default-value-source\":\"Const\",\"default-value\":\"Draft\"}"

# Create a column with structured default-value-config metadata
clio create-entity-schema -e dev --package Custom --name UsrVehicle --title "Vehicle" --column "{\"name\":\"UsrStartDate\",\"type\":\"DateTime\",\"title\":\"Start date\",\"default-value-config\":{\"source\":\"SystemValue\",\"value-source\":\"Current Time and Date\"}}"

# Create a column with system setting default (name/code/id accepted)
clio create-entity-schema -e dev --package Custom --name UsrVehicle --title "Vehicle" --column "{\"name\":\"UsrOwner\",\"type\":\"Lookup\",\"title\":\"Owner\",\"reference-schema-name\":\"Contact\",\"default-value-config\":{\"source\":\"Settings\",\"value-source\":\"Maintainer\"}}"

# Create a schema inheriting from a parent schema
clio create-entity-schema -e dev --package Custom --name UsrVehicle --title "Vehicle" --parent BaseEntity

# Create a replacement schema
clio create-entity-schema -e dev --package Custom --name Account --title "Account" --extend-parent

# Create a virtual entity without a physical database table
clio create-entity-schema -e dev --package Custom --name UsrExternalVehicle --title "External vehicle" --is-virtual
```

## Notes

- A schema name is required; a call without one fails before anything is
sent to the server
- An explicit --is-db-view requires designer readback. If it returns HTML,
creation reports an error even though the schema may already be saved.
Inspect it with get-entity-schema-properties before retrying creation.
- The command works against a remote Creatio environment only
- Package resolution requires cliogate to be installed on the target environment
- When --parent is omitted a non-replacement schema inherits BaseEntity. A
parentless root schema gets a prefixed primary column (e.g. UsrId instead
of Id) and is not reachable over OData in either direction, so BaseEntity is
applied automatically. Pass --parent explicitly to inherit from a different
schema. With --extend-parent, the parent defaults to --name and an explicit
parent must match that name. The base schema may live in another package,
but a replacement already in the target package is rejected; use
sync-schemas through MCP to create or reconcile it.
- If no Guid column is supplied for a root schema, Id:Guid is added automatically
- A schema with a parent keeps the parent's primary column; custom Guid columns
remain ordinary columns
- --is-virtual controls only whether a physical database table is created; it
does not change parent defaulting. A virtual schema created without --parent
still inherits BaseEntity (and remains tableless), and --is-virtual combines
with an explicit --parent or --extend-parent. Use it for entities whose data
comes from a custom provider rather than a Creatio table
- --is-db-view true maps the entity to a database view and --is-db-view false
clears the flag; omitting it keeps the inherited value. Creatio skips table
generation for a database-view entity. The option neither creates a SQL view
nor converts or drops an existing table, so provision the view separately
(for example with a package SQL script). --is-virtual is independent of it
- If no display column is set, the first text-like column is used as primary display column
- Prefer default-value-config for Settings, SystemValue, or Sequence; keep
default-value-source/default-value as shorthand only for Const and None
- For default-value-config source=SystemValue:
value-source can be Guid, enum alias (for example CurrentDateTime), or caption
(for example Current Time and Date); values are normalized to Guid before save
- For default-value-config source=Settings:
value-source can be setting code, setting name, or setting id; values are
normalized to setting code before save
- For default-value-config source=Sequence (text columns only):
set the static prefix via sequence-prefix (e.g. LN-) or a value mask
ending with {0} (e.g. LN-{0} produces LN-00001), not both; masks with
static text after {0} are rejected with a validation error
- If Settings or SystemValue lookup is ambiguous, the command fails with a
validation error and requests explicit code/Guid input
- Binary, Image, and File columns do not support --default-value or --default-value-source Const
- After save, the schema is reloaded immediately; save is treated as failed if the schema cannot be read back
- After save, the configuration is published automatically; if publication
fails, the schema stays saved but invisible to lookup pickers and
sys-setting reference lists until the configuration is compiled
- After publish, an OData entities rebuild is requested so the schema is
reachable over OData without a manual compile; it runs in the background
(reachable within a few minutes). A 404 right after creation is the
expected async gap — wait and retry rather than running a full compile.
If requesting the rebuild fails, it is logged as a warning and schema
creation still succeeds
- when --caption-culture is omitted, clio uses the connected user's profile
culture (see get-user-culture) and falls back to en-US if it cannot be resolved
- each title-localizations / description-localizations value must be written in
the language of its culture key: the en-US value must be English, and a value in
a script that does not match a Latin-script culture key (for example Cyrillic
under en-US) is rejected; put localized text under its own culture key (uk-UA)
- every caption culture must exist in the Languages section (System Designer -> Languages).
Creatio silently drops a value in a culture it does not have, so a culture the
environment does not have fails before anything is saved, listing the
available cultures; an inactive culture is saved with a warning

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

## See Also

install-gate, list-packages, add-schema, get-entity-schema-properties,
modify-entity-schema-column, get-user-culture

- [Clio Command Reference](../../Commands.md#create-entity-schema)
