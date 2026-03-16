# add-data-binding-row

## Purpose

Adds a new row to an existing package data binding or replaces the existing row that has the same primary-key value. Once the binding exists locally, the command works entirely from the binding files and does not require Creatio access, including bindings created from built-in offline templates such as `SysSettings`.

## Usage

```bash
clio add-data-binding-row [options]
```

## Arguments

### Required Arguments

| Argument | Description | Example |
|----------|-------------|---------|
| `--package` | Target package name | `--package Custom` |
| `--binding-name` | Binding folder name under package `Data` | `--binding-name SysSettings` |
| `--values` | JSON object keyed by column name for the row payload. If the GUID primary key column is omitted or null, it is generated automatically. For non-null lookup and image-reference columns, use an object like `{"value":"...","displayValue":"..."}`. For image-content columns, pass either a base64 string or a local file path inside the workspace and clio encodes the file | `--values "{\"Name\":\"Value\"}"` |

### Optional Arguments

| Argument | Description | Example |
|----------|-------------|---------|
| `--workspace-path` | Workspace root path. Defaults to the current workspace | `--workspace-path C:\Work\MyWorkspace` |
| `--localizations` | JSON object keyed by culture and column name | `--localizations "{\"en-US\":{\"Name\":\"Localized name\"}}"` |

## Behavior

- Loads `descriptor.json` to resolve column names to `SchemaColumnUId` values
- Uses the primary key defined in the binding descriptor as the row identity
- Generates a GUID primary key when the binding primary key is GUID-based and the payload omits it or sets it to `null`
- For non-null lookup and image-reference columns, writes `SchemaColumnUId`, `Value`, and `DisplayValue` into `data.json`
- If an image-content column receives a string that points to an existing local file inside the workspace, clio reads that file and writes its base64 content into `data.json`
- `SysModule.IconBackground` accepts only the predefined 16-color palette
- Replaces an existing row when the same primary-key value already exists
- Writes localization rows into `Localization/data.<culture>.json` when `--localizations` is supplied
- Rejects unknown columns and invalid JSON payload shapes

## JSON Shapes

### Values

```json
{
  "Name": "New name"
}
```

For image-content columns such as `SysModule.Image16` or `SysModule.Image20`, the same JSON value may be a local file path:

```json
{
  "Code": "UsrModule",
  "Image16": "assets/icon.png"
}
```

For non-null lookup and image-reference columns, use the structured object form so the local binding keeps both the identifier and display text:

```json
{
  "FolderMode": {
    "value": "b659d704-3955-e011-981f-00155d043204",
    "displayValue": "Folders"
  },
  "Logo": {
    "value": "1171d0f0-63eb-4bd1-a50b-001ecbaf0001",
    "displayValue": "Module logo"
  }
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
    "Name": "Localized name"
  }
}
```

## Examples

### Add a New Row

```bash
clio add-data-binding-row --package Custom --binding-name SysSettings \
  --values "{\"Name\":\"Setting name\"}"
```

### Replace an Existing Row and Update Localization

```bash
clio add-data-binding-row --package Custom --binding-name SysSettings \
  --workspace-path C:\Work\MyWorkspace \
  --values "{\"Name\":\"New name\"}" \
  --localizations "{\"en-US\":{\"Name\":\"Localized name\"}}"
```

### Add a SysModule Row from a Local Image File

```bash
clio add-data-binding-row --package Custom --binding-name SysModule \
  --workspace-path C:\Work\MyWorkspace \
  --values "{\"Code\":\"UsrModule\",\"Image16\":\"assets\\icon.png\"}"
```

### Add a SysModule Row with Explicit Lookup Display Text

```bash
clio add-data-binding-row --package Custom --binding-name SysModule \
  --values "{\"Code\":\"UsrModule\",\"FolderMode\":{\"value\":\"b659d704-3955-e011-981f-00155d043204\",\"displayValue\":\"Folders\"}}"
```

## Validation and Requirements

- The workspace must be resolvable and must contain the target package
- The binding directory and its `descriptor.json` and `data.json` files must already exist
- The row payload may omit the binding primary key only when that key is Guid-based; in that case the command generates it automatically
- Non-null lookup and image-reference values must include `displayValue` because `add-data-binding-row` works only from local binding files
- Localized columns must be string-compatible according to the binding descriptor
- Image-content file-path values are resolved relative to `--workspace-path` when it is supplied, otherwise relative to the resolved current workspace
- Image-content file-path values must stay inside the resolved workspace; paths outside it are rejected
- `SysModule.IconBackground` values outside the predefined 16-color palette are rejected

## Related Commands

- [`create-data-binding`](../../Commands.md#create-data-binding) - Create the binding folder and descriptor
- [`remove-data-binding-row`](../../Commands.md#remove-data-binding-row) - Remove an existing row from the binding
