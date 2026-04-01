# create-data-binding

Create or regenerate a package data binding.


## Usage

```bash
clio create-data-binding [OPTIONS]
```

## Description

Creates or updates a data-binding folder under the target package.

When the requested schema has a built-in offline template, clio uses that
template and does not require Creatio access. In v1, SysSettings and
SysModule are supported offline templates and template metadata always
takes precedence over runtime schema fetches.

For schemas that are not covered by the built-in template catalog, clio
fetches the runtime entity schema from a Creatio environment.

The generated binding lives under:
<workspace>/packages/<package>/Data/<binding-name>

Files created or updated:
- descriptor.json
- data.json
- filter.json
- Localization/data.en-US.json in template mode
- Localization/data.<culture>.json for cultures supplied in --localizations

If --values is omitted, the command creates a single template row with all
schema columns and empty placeholder values.

## Examples

```bash
# Create a SysSettings binding offline from the current workspace
clio create-data-binding --package Custom --schema SysSettings

# Create a non-templated binding in an explicit workspace with initial values
clio create-data-binding -e dev --package Custom --schema UsrCustomEntity --workspace-path C:\Work\MyWorkspace --values '{\"Name\":\"Setting name\"}'

# Create a binding with explicit localizations
clio create-data-binding -e dev --package Custom --schema SysSettings --values "{\"Name\":\"Setting name\"}" --localizations '{\"ru-RU\":{\"Name\":\"Настройка\"}}'

# Create a SysModule binding with an image file for Image16
clio create-data-binding --package Custom --schema SysModule --workspace-path C:\Work\MyWorkspace --values "{\"Code\":\"UsrModule\",\"Image16\":\"assets\\icon.png\"}"

# Create a SysModule binding with explicit lookup display text
clio create-data-binding --package Custom --schema SysModule --values "{\"Code\":\"UsrModule\",\"FolderMode\":{\"value\":\"b659d704-3955-e011-981f-00155d043204\",\"displayValue\":\"Folders\"}}"
```

## Options

```bash
--package              Target package name
--schema               Entity schema name used to fetch template or runtime metadata
--workspace-path       Workspace root path. Defaults to the current workspace
--binding-name         Binding folder name. Defaults to <schema>
--install-type         Descriptor install type. Allowed values: 0, 1, 2, 3.
Default: 0
--values               Optional JSON object keyed by column name for the
initial row. If the GUID primary key column is
omitted or null, it is generated automatically.
For lookup and image-reference columns, pass either
a scalar value or an object with value and
displayValue. When runtime lookup data is available,
create-data-binding resolves a missing displayValue
automatically.
For image-content columns, pass either a base64
string or a local file path inside the workspace and
clio encodes the file
--localizations        Optional JSON object keyed by culture and column name

Environment options are also available:
-e, --Environment      Environment name from the registered configuration
-u, --uri              Application URI
-l, --Login            User login
-p, --Password         User password
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

## Notes

- For templated schemas such as SysSettings and SysModule, --environment and --uri are optional
- For non-templated schemas, the command requires either --environment or --uri
- Unknown columns in --values or --localizations are rejected
- If the primary key column is a Guid and is omitted or null in --values, create-data-binding generates it automatically
- For lookup and image-reference columns, clio writes SchemaColumnUId, Value, and DisplayValue in data.json
- For create-data-binding, lookup and image-reference values may use {"value":"...","displayValue":"..."}; if displayValue is omitted and Creatio runtime lookup data is available, clio resolves it automatically
- For image-content columns, a string value that points to an existing local file inside the workspace is encoded to base64 before writing data.json
- SysModule IconBackground only accepts these colors:
#A6DE00, #20A959, #22AC14, #FFAC07, #FF8800, #F9307F, #FF602E, #FF4013, #B87CCF, #7848EE, #247EE5, #0058EF, #009DE3, #4F43C2, #08857E, #00BFA5
- If the target binding folder already exists for another schema, the
command fails instead of overwriting it
- filter.json is always created as an empty file

## Command Type

    Development commands

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

## See also

- `add`

- [Clio Command Reference](../../Commands.md#create-data-binding)
