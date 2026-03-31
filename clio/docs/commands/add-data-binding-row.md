# add-data-binding-row

Add or replace a row in a package data binding.

## Usage

```bash
clio add-data-binding-row [options]
```

## Description

Add or replace a row in a package data binding.

## Examples

```bash
clio add-data-binding-row [options]
```

## Options

```bash
--package <VALUE>
    Target package name. Required.
--binding-name <VALUE>
    Binding folder name. Required.
--values <VALUE>
    Row values as JSON object keyed by column name. Non-null lookup and
    image-reference columns should use {"value":"...","displayValue":"..."}. Image
    content columns accept either a base64 string or a local file path to encode.
    Required.
--localizations <VALUE>
    Localized values as JSON object keyed by culture and column name
--workspace-path <VALUE>
    Workspace root path. Defaults to the current workspace
```

## See also

- `create-data-binding`
- `remove-data-binding-row`

- [Clio Command Reference](../../Commands.md#add-data-binding-row)
