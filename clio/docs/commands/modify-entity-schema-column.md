# modify-entity-schema-column

Add, modify, or remove a column in a remote Creatio entity schema.


## Usage

```bash
clio modify-entity-schema-column [options]
```

## Description

Add, modify, or remove a column in a remote Creatio entity schema.

After saving the column the command publishes the configuration and requests an
OData entities rebuild, so the changed column becomes visible to lookup pickers
and reachable over OData (`/0/odata/<Entity>`) without a manual compile. The
rebuild runs in the background — OData access appears within a few minutes, not
immediately. A 404 (or "The request is invalid") from OData right after the
change is the expected async gap; wait and retry rather than running a full
compile. Each call publishes once, so to change several columns at once batch
them through `update-entity-schema` instead of one call per column.

## Examples

```bash
clio modify-entity-schema-column -e dev
```

## Options

```bash
--package <VALUE>
Target package name. Required.
--schema-name <VALUE>
Entity schema name. Required.
--action <VALUE>
Column action: add, modify, or remove. Required.
--column-name <VALUE>
Target column name. Required.
--new-name <VALUE>
New column name for rename operations
--type <VALUE>
Column type. Supported values:
Guid, Integer, Float, Boolean, Date, DateTime,
Time, Lookup,
Binary, Image, ImageLookup, File, SecureText,
Text, ShortText, MediumText, LongText, MaxSizeText,
Text50,
Text250, Text500, TextUnlimited, PhoneNumber, WebLink, Email, RichText,
Decimal0, Decimal1, Decimal2, Decimal3, Decimal4, Decimal8, 
Currency0,
Currency1, Currency2, Currency3.
ImageLink is accepted as an alias for ImageLookup.
For image/photo fields rendered with the `crt.ImageInput` Freedom UI component, use
`ImageLookup` ("Image link") — the binary `Image` type does not work with `crt.ImageInput`.
`ImageLookup` references the `SysImage` schema automatically (no `--reference-schema`).
--title <VALUE>
Column title/caption
--description <VALUE>
Column description
--reference-schema <VALUE>
Lookup reference schema name (not used for ImageLookup)
--required
Set required flag
--indexed
Set indexed flag
--cloneable
Set make-copy flag
--track-changes
Set update-change-log flag
--default-value <VALUE>
Set a constant default value
--default-value-source <VALUE>
Default value source: Const or None
--multiline-text
Set multi-line text flag
--localizable-text
Set localizable text flag
--accent-insensitive
Set accent-insensitive flag
--masked
Set masked flag
--format-validated
Set format-validated flag
--use-seconds
Set use-seconds flag
--simple-lookup
Set simple-lookup flag
--cascade
Set cascade-connection flag
--do-not-control-integrity
Set do-not-control-integrity flag
--usage-type
Column usage type: General (default), Advanced, or None (case-insensitive; applies to any column type). On modify the stored value is left unchanged when omitted.
--timeout <NUMBER>
Request timeout in milliseconds. Default: 100000.
```

## Environment Options

```bash
-u, --uri <VALUE>
Application uri
-p, --Password <VALUE>
User password
-l, --Login <VALUE>
User login (administrator permission required)
-i, --IsNetCore
Use NetCore application
-e, --Environment <VALUE>
Environment name
-m, --Maintainer <VALUE>
Maintainer name
-c, --dev <VALUE>
Developer mode state for environment
--WorkspacePathes <VALUE>
Workspace path
-s, --Safe <VALUE>
Safe action in this environment
--clientId <VALUE>
OAuth client id
--clientSecret <VALUE>
OAuth client secret
--authAppUri <VALUE>
OAuth app URI
--silent
Use default behavior without user interaction
--restart-environment
Restart environment after execute command
--db-server-uri <VALUE>
Db server uri
--db-user <VALUE>
Database user
--db-password <VALUE>
Database password
--backup-file <VALUE>
Full path to backup file
--db-working-folder <VALUE>
Folder visible to db server
--db-name <VALUE>
Desired database name
--force
Force restore
--callback-process <VALUE>
Callback process name
--ep <VALUE>
Path to the application root folder
```

## Requirements

cliogate must be installed on the target Creatio environment.

## Notes

- CLI flags `--default-value-source/--default-value` remain shorthand for `Const` and `None`.
- MCP structured `default-value-config` also supports `Settings` and `SystemValue`.
- For `SystemValue`, clio resolves Guid/alias/caption to canonical Guid before save.
- For `Settings`, clio resolves code/name/id to canonical setting code before save.
- For a **lookup** column, a `Const` value is the GUID of a record in the referenced schema. clio validates the record exists before save and rejects an unknown GUID with `Error: ... default value record '<guid>' was not found in referenced schema '<schema>'.` (non-zero exit, schema not saved). The check is point-in-time (TOCTOU) and is skipped when the referenced record cannot be read (e.g. no access), so a write is never blocked on an unverifiable check.
- `--caption-culture <VALUE>` overrides the culture for the written column caption/description (e.g. `en-US`, `uk-UA`). Precedence: override > the connected user's profile culture (see `get-user-culture`) > `en-US`. When omitted, clio resolves the profile culture and falls back to `en-US` if it cannot be resolved. Column READ/display (`get-entity-schema-column-properties`) keeps using the host locale.
- For `add`/`modify`, each `title-localizations` / `description-localizations` value must be written in the language of its culture key. The `en-US` value must be English; a value in a script that does not match a Latin-script culture key (e.g. Cyrillic under `en-US`) is rejected — put localized text under its own culture key such as `uk-UA`.

## See also

- `get-entity-schema-column-properties`
- `get-user-culture`
- `get-entity-schema-properties`

- [Clio Command Reference](../../Commands.md#modify-entity-schema-column)
