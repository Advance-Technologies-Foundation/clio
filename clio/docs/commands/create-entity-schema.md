# create-entity-schema

Create an entity schema in a remote Creatio package.


## Usage

```bash
clio create-entity-schema [options]
```

## Description

Create an entity schema in a remote Creatio package.

The command saves the schema, applies the DB structure, and publishes the
configuration, so the new schema is immediately visible to lookup pickers and
sys-setting reference schema lists. No separate compile is required.

Publishing also requests an OData entities rebuild, so the schema becomes
reachable over OData (`/0/odata/<Entity>`) without a manual full compile. That
rebuild runs in the background — OData access appears within a few minutes, not
immediately. A 404 from OData right after creation is the expected async gap;
wait and retry rather than running a full compile.

## Examples

```bash
clio create-entity-schema -e dev
```

## Options

```bash
--package <VALUE>
Target package name. Required.
--name <VALUE>
Schema name. Required.
--title <VALUE>
Schema title. Required.
--parent <VALUE>
Parent schema name
--extend-parent
Create replacement schema
--column <VALUE>
Column spec <name>:<type>[:<title>[:<refSchema>]] or JSON with
name/type/title/reference-schema-name/required/default-value-source/default-value/default-value-config.
Repeat the option for multiple columns.
Supported types include Guid, Text/ShortText/MediumText/LongText/MaxSizeText, Integer, Float,
Boolean, Date/DateTime/Time, Lookup, Binary, Image, ImageLookup, File, SecureText, and Email.
For image/photo fields rendered with the `crt.ImageInput` Freedom UI component, use the
`ImageLookup` ("Image link") type (alias: `ImageLink`) — the binary `Image` type does not work
with `crt.ImageInput`. `ImageLookup` references the `SysImage` schema automatically; do not set
a reference schema for it.
--caption-culture <VALUE>
Override the culture used for the generated schema and column captions/labels (e.g. `en-US`, `uk-UA`).
Precedence: this override > the connected user's profile culture > `en-US`. Supplying it skips the
profile-culture lookup. When omitted, clio resolves the logged-in user's profile culture (see
`get-user-culture`) and falls back to `en-US` if it cannot be resolved.
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

- `default-value-config` is recommended for non-constant sources.
- For `default-value-config.source = SystemValue`, `value-source` can be Guid, alias, or caption; clio persists canonical Guid.
- For `default-value-config.source = Settings`, `value-source` can be code, name, or id; clio persists canonical setting code.
- **Caption language validation.** Every `title-localizations` / `description-localizations` value must be written in the language of its culture key. The mandatory `en-US` value must be English; a value written in a script that does not match a Latin-script culture key (for example Cyrillic text under `en-US`) is rejected with an actionable error. Put localized text under its own culture key (e.g. `uk-UA`). This guarantees generated captions match the connected user's profile language.

## See also

- `get-entity-schema-properties`
- `modify-entity-schema-column`

- [Clio Command Reference](../../Commands.md#create-entity-schema)
