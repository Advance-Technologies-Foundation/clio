# update-entity-schema

Apply a batch of structured column operations to a remote entity schema.

```bash
clio update-entity-schema [options]
```

This command is the clio-native batch mutation surface for entity schemas. It applies an ordered list of
column operations and reuses the same validation and DB-first save flow as `modify-entity-schema-column`.
Supported operation types include `Binary`, `Image`, `File`, and `SecureText`. `Blob` is accepted as an alias
for `Binary`, and `Encrypted` and `Password` are accepted as aliases for `SecureText`.

## Options

- `--package` (required): Target package name
- `--schema-name` (required): Entity schema name
- `--operation` (required, repeatable): Structured operation JSON. Repeat the option for each payload.
- `-e, --environment` (required): Target environment

Each operation uses fields such as:
- `action`
- `column-name`
- `new-name`
- `type`
- `title`
- `description`
- `reference-schema-name`
- `required`
- `default-value-source`
- `default-value`

## Examples

```bash
clio update-entity-schema -e dev --package Custom --schema-name UsrVehicle \
  --operation "{\"action\":\"add\",\"column-name\":\"UsrStatus\",\"type\":\"Lookup\",\"title\":\"Status\",\"reference-schema-name\":\"UsrVehicleStatus\",\"required\":true}" \
  --operation "{\"action\":\"add\",\"column-name\":\"UsrDueDate\",\"type\":\"Date\",\"title\":\"Due date\"}"
```

```bash
clio update-entity-schema -e dev --package Custom --schema-name UsrVehicle \
  --operation "{\"action\":\"modify\",\"column-name\":\"Owner\",\"new-name\":\"PrimaryOwner\",\"title\":\"Primary owner\"}" \
  --operation "{\"action\":\"modify\",\"column-name\":\"Status\",\"default-value-source\":\"None\"}"
```

## Notes

- operations are applied in order
- execution stops on the first failed operation
- the batch is saved and materialized once after all operations are applied
- use this command when the caller already knows a full batch of entity column mutations
- use `modify-entity-schema-column` for one-off single-column changes
- `Binary`, `Image`, and `File` operations do not support `default-value` or `default-value-source: Const`
