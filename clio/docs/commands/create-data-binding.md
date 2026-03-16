# create-data-binding

## Purpose

Creates or regenerates a package data binding from either a built-in offline template or a live Creatio runtime schema. When a built-in template exists for the requested schema, clio uses it locally and does not contact Creatio. In v1, `SysSettings` and `SysModule` are supported offline templates, and template metadata always wins when it is defined.

## Usage

```bash
clio create-data-binding [options]
```

## Arguments

### Required Arguments

| Argument | Description | Example |
|----------|-------------|---------|
| `--package` | Target package name | `--package Custom` |
| `--schema` | Entity schema name used for template or runtime metadata lookup | `--schema SysSettings` |

### Optional Arguments

| Argument | Description | Example |
|----------|-------------|---------|
| `--workspace-path` | Workspace root path. Defaults to the current workspace | `--workspace-path C:\Work\MyWorkspace` |
| `--binding-name` | Binding folder name. Defaults to `<schema>` | `--binding-name SysSettings` |
| `--install-type` | Descriptor install type. Default is `0` | `--install-type 3` |
| `--values` | JSON object keyed by column name for the initial row. If the GUID primary key column is omitted or null, it is generated automatically. For image-content columns, pass either a base64 string or a local file path inside the workspace and clio encodes the file | `--values "{\"Name\":\"Value\"}"` |
| `--localizations` | JSON object keyed by culture and column name | `--localizations "{\"ru-RU\":{\"Name\":\"Значение\"}}"` |

### Environment Configuration

| Argument | Short | Description | Example |
|----------|-------|-------------|---------|
| `--environment` | `-e` | Environment name from configuration. Optional for templated schemas such as `SysSettings`; required otherwise | `-e dev` |
| `--uri` | `-u` | Creatio application URI. Optional for templated schemas such as `SysSettings`; required otherwise | `--uri http://localhost:8080` |
| `--login` | `-l` | Username for authentication | `--login Supervisor` |
| `--password` | `-p` | Password for authentication | `--password Supervisor` |

## Generated Files

The command writes the binding folder under:

```text
<workspace>/packages/<package>/Data/<binding-name>
```

Generated files:

- `descriptor.json`
- `data.json`
- `filter.json`
- `Localization/data.en-US.json` when template mode is used
- `Localization/data.<culture>.json` for cultures supplied in `--localizations`

## Behavior

- Uses a built-in offline template when the schema is covered by the template catalog
- Otherwise fetches schema metadata from `DataService/json/SyncReply/RuntimeEntitySchemaRequest`
- Uses the resolved schema `uId`, `name`, `primaryColumnUId`, and column metadata to build `descriptor.json`
- Always creates `filter.json` as an empty file
- If `--values` is omitted, creates a single template row with all schema columns and empty placeholder values
- If `--values` is supplied, keeps only the primary key column plus the explicitly provided columns
- If the primary key column is GUID-based and omitted or set to `null` in `--values`, generates it automatically
- If an image-content column receives a string that points to an existing local file inside the workspace, clio reads that file and writes its base64 content into `data.json`
- `SysModule.IconBackground` accepts only the predefined 16-color palette
- Rejects unknown columns instead of silently writing invalid files
- Reuses an existing binding folder only when it already targets the same schema

## JSON Shapes

### Values

```json
{
  "Code": "UsrSetting",
  "Name": "Setting name"
}
```

For image-content columns such as `SysModule.Image16` or `SysModule.Image20`, the same JSON value may be a local file path:

```json
{
  "Code": "UsrModule",
  "Image16": "assets/icon.png"
}
```

For `SysModule.IconBackground`, use one of these colors only:

```text
#A6DE00, #20A959, #22AC14, #FFAC07, #FF8800, #F9307F, #FF602E, #FF4013,
#B87CCF, #7848EE, #247EE5, #0058EF, #009DE3, #4F43C2, #08857E, #00BFA5
```

### Localizations

```json
{
  "en-US": {
    "Name": "Setting name"
  },
  "ru-RU": {
    "Name": "Настройка"
  }
}
```

## Examples

### Create a Template Binding

```bash
clio create-data-binding --package Custom --schema SysSettings
```

### Create a Binding with Explicit Row Values

```bash
clio create-data-binding --package Custom --schema SysSettings \
  --workspace-path C:\Work\MyWorkspace \
  --values "{\"Code\":\"UsrSetting\",\"Name\":\"Setting name\"}"
```

### Create a Non-Templated Binding with Runtime Metadata

```bash
clio create-data-binding -e dev --package Custom --schema UsrCustomEntity \
  --workspace-path C:\Work\MyWorkspace \
  --values "{\"Name\":\"Runtime schema row\"}"
```

### Create a Binding with Extra Localization Files

```bash
clio create-data-binding --package Custom --schema SysSettings \
  --values "{\"Name\":\"Setting name\"}" \
  --localizations "{\"ru-RU\":{\"Name\":\"Настройка\"}}"
```

### Create a SysModule Binding from a Local Image File

```bash
clio create-data-binding --package Custom --schema SysModule \
  --workspace-path C:\Work\MyWorkspace \
  --values "{\"Code\":\"UsrModule\",\"Image16\":\"assets\\icon.png\"}"
```

## Validation and Requirements

- For templated schemas such as `SysSettings` and `SysModule`, `--environment` and `--uri` are optional
- For non-templated schemas, provide either `--environment` or `--uri`
- The workspace must be resolvable and must contain `.clio/workspaceSettings.json`
- The package must already exist in the workspace
- The resolved schema must exist and expose a primary column
- `--install-type` must be between `0` and `3`
- Value parsing enforces supported data types including Guid, bool, numeric, DateTime, and string-compatible values
- Image-content file-path values are resolved relative to `--workspace-path` when it is supplied, otherwise relative to the resolved current workspace
- Image-content file-path values must stay inside the resolved workspace; paths outside it are rejected
- `SysModule.IconBackground` values outside the predefined 16-color palette are rejected

## Related Commands

- [`add-data-binding-row`](../../Commands.md#add-data-binding-row) - Add or replace rows in an existing binding
- [`remove-data-binding-row`](../../Commands.md#remove-data-binding-row) - Remove rows from an existing binding
- [`call-service`](../../Commands.md#call-service) - Call the runtime schema request endpoint directly
