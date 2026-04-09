# modify-entity-schema-column

Add, modify, or remove a column in a remote Creatio entity schema.


## Usage

```bash
clio modify-entity-schema-column [options]
```

## Description

Add, modify, or remove a column in a remote Creatio entity schema.

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
Text, ShortText, MediumText, LongText, MaxSizeText,
Text50,
Text250, Text500, TextUnlimited, PhoneNumber, WebLink, Email, RichText,
Decimal0, Decimal1, Decimal2, Decimal3, Decimal4, Decimal8, 
Currency0,
Currency1, Currency2, Currency3
--title <VALUE>
Column title/caption
--description <VALUE>
Column description
--reference-schema <VALUE>
Lookup reference schema name
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
--restartEnvironment
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

## See also

- `get-entity-schema-column-properties`
- `get-entity-schema-properties`

- [Clio Command Reference](../../Commands.md#modify-entity-schema-column)
