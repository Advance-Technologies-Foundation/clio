# update-entity-schema

## Command Type

    Package Management commands

## Name

update-entity-schema - Apply batch column operations to a remote Creatio entity schema

## Synopsis

```bash
clio update-entity-schema [OPTIONS]
```

## Description

Applies an ordered batch of structured column operations to an existing remote
entity schema.

This command is the clio-native batch mutation contract for entity schemas.
Each operation uses the same column-level semantics as
modify-entity-schema-column, but several mutations can be applied in one
request.

After saving the batch, the configuration is always published (once for the
whole batch), so changed columns become visible to lookup pickers without a
manual compile. The OData entities are rebuilt only if at least one operation
in the batch changes the published OData contract - adding or removing a
column, renaming one (--new-name), or changing its type or reference schema.
A batch made only of
caption, description, default value, mask, usage type, or required changes
does not trigger a rebuild. When a rebuild is requested, it runs in the
background (~1-2 min); a 404 (or "The request is invalid") from OData right
after the change is the expected async gap, so wait and retry rather than
compiling.

Supported operation types include Binary, Image, ImageLookup,
File, SecureText, and Email. Blob is accepted as an alias for Binary.
ImageLink is accepted as an alias for ImageLookup.
Encrypted and Password are accepted as aliases for SecureText.
EmailAddress is accepted as an alias for Email.
For image/photo fields bound to the crt.ImageInput component, add an
ImageLookup ("Image link") column instead of the binary Image type;
ImageLookup references the SysImage schema automatically (no reference-schema-name).

## Options

```bash
--package              Target package name. Required
--schema-name          Entity schema name. Required
--operation            Structured operation JSON. Pass several values
after one --operation; the flag itself cannot be
repeated
--operations           JSON array of operation objects. Applied after the
--operation values
--operations-file      Path to a file with a JSON array of operation
objects (same format as --operations; multi-line
allowed). A relative path resolves from the current
directory. The file must be UTF-8 (a BOM is allowed;
UTF-16 with a BOM is also read). Applied after
--operation and --operations
--caption-culture      Override the culture for written column captions/
descriptions (e.g. en-US, uk-UA). Precedence:
override > profile culture > en-US. Supplying it
skips the profile-culture lookup.
--timeout              Request timeout in milliseconds. Default: 100000

Environment options are also available:
-e, --environment      Environment name from the registered configuration
-u, --uri              Application URI
-l, --login            User login
-p, --password         User password
```

## Requirements

cliogate must be installed on the target Creatio environment.

## Examples

```bash
# Add two columns in one batch
clio update-entity-schema -e dev --package Custom --schema-name UsrVehicle --operation "{\"action\":\"add\",\"column-name\":\"UsrStatus\",\"type\":\"Lookup\",\"title\":\"Status\",\"reference-schema-name\":\"UsrVehicleStatus\",\"required\":true}" "{\"action\":\"add\",\"column-name\":\"UsrDueDate\",\"type\":\"Date\",\"title\":\"Due date\"}"

# Rename a column and clear its default in one batch
clio update-entity-schema -e dev --package Custom --schema-name UsrVehicle --operation "{\"action\":\"modify\",\"column-name\":\"Owner\",\"new-name\":\"PrimaryOwner\",\"title\":\"Primary owner\"}" "{\"action\":\"modify\",\"column-name\":\"Status\",\"default-value-source\":\"None\"}"

# Set a system-value default in one batch (caption/alias/guid accepted)
clio update-entity-schema -e dev --package Custom --schema-name UsrVehicle --operation "{\"action\":\"modify\",\"column-name\":\"UsrStartDate\",\"default-value-config\":{\"source\":\"SystemValue\",\"value-source\":\"Current Time and Date\"}}"

# Set a system-setting default in one batch (name/code/id accepted)
clio update-entity-schema -e dev --package Custom --schema-name UsrVehicle --operation "{\"action\":\"modify\",\"column-name\":\"UsrOwner\",\"default-value-config\":{\"source\":\"Settings\",\"value-source\":\"Maintainer\"}}"

# Read a multi-line operations array from a file (works in cmd.exe and Windows PowerShell 5.1)
clio update-entity-schema -e dev --package Custom --schema-name UsrVehicle --operations-file operations.json
```

## Notes

- this command applies COLUMN operations only; its per-operation title-localizations
is a COLUMN caption. To change the SCHEMA caption use set-entity-schema-properties
(--title / --title-localizations)
- at least one operation is required; a call with none reports what is missing
- operations are applied in order: the --operation values first, then the
--operations array, then the --operations-file array
- repeating --operation (--operation A --operation B) is rejected by the
parser; pass the values after one --operation (--operation A B) or use
--operations / --operations-file
- --operations-file avoids shell quoting problems with multi-line JSON in
cmd.exe and Windows PowerShell 5.1
- a missing or unreadable --operations-file, a file that is not valid UTF-8
(for example one saved in an ANSI code page by Set-Content without
-Encoding UTF8), or content that is not a JSON array, fails before
anything is saved and names the option and the path
- an error in an operation that came from --operations or --operations-file
names that source and counts the index within it
- an operation field the command does not know (for example a misspelled
colum-name) fails the batch before anything is saved, naming the field and
the nearest known field
- name is accepted as an alias of column-name; an operation that sets both to
different values is rejected
- each operation payload must be a JSON object; a field value of the wrong type
is reported with its JSON path (for example $.required)
- a modify on an INHERITED column may override only its caption/description
(title-localizations/description-localizations); changing its name, type, or
flags is rejected, and stops the batch on that operation
- each operation may set usage-type (General, Advanced, or None; any column
type); on modify the stored value is left unchanged when omitted
- execution stops on the first failed operation
- the batch is saved and materialized once after all operations are applied
- post-save verification checks the final ordered batch state, so a later add may
intentionally reuse a column name removed earlier in the same batch
- operation JSON can also use structured default-value-config; legacy
default-value-source/default-value remain shorthand for Const and None
- For default-value-config source=SystemValue:
value-source can be Guid, enum alias, or caption; values are normalized
to Guid before save
- For default-value-config source=Settings:
value-source can be setting code, setting name, or setting id; values are
normalized to setting code before save
- For default-value-config source=Sequence (text columns only):
set the static prefix via sequence-prefix (e.g. LN-) or a value mask
ending with {0} (e.g. LN-{0} produces LN-00001), not both; masks with
static text after {0} are rejected with a validation error
- If Settings or SystemValue lookup is ambiguous, execution fails with a
validation error and requests explicit code/Guid input
- this is the clio-native alternative to frontend-style entity.update.operationsJson
- Binary, Image, and File operations do not support default-value or default-value-source Const
- when --caption-culture is omitted, clio uses the connected user's profile
culture (see get-user-culture) and falls back to en-US if it cannot be resolved
- each title-localizations / description-localizations value must be written in
the language of its culture key: the en-US value must be English, and a value in
a script that does not match a Latin-script culture key (for example Cyrillic
under en-US) is rejected; put localized text under its own culture key (uk-UA)
- every caption culture must exist in the Languages section (System Designer -> Languages).
Creatio silently drops a value in a culture it does not have, so a culture the
environment does not have fails the whole batch before anything is saved, listing the
available cultures; an inactive culture is saved with a warning

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

## See Also

modify-entity-schema-column, get-entity-schema-properties, get-user-culture,
set-entity-schema-properties

- [Clio Command Reference](../../Commands.md#update-entity-schema)
