# import-schema

Import a schema bundle produced by [`export-schema`](export-schema.md) into a package of a Creatio
environment, creating or replacing exactly one schema.

**Alias:** `schema-import`

## Usage

```bash
clio import-schema <Path> --package-name <Package> -e <Environment> [--dry-run] [--allow-new-layer]
```

| Option | Description |
|---|---|
| `<Path>` | Bundle folder produced by `export-schema`, or its `schema-data.json`. Required. |
| `--package-name` | Target package that will own the schema. Required. There is no `-p` short form — `EnvironmentOptions` already binds `-p` to `--password`. |
| `--dry-run` | Report the planned action and write nothing. |
| `--allow-new-layer` | Proceed when the schema name is already owned by a different package. |
| `-e`, `--environment` | Environment name. |

## Identity is preserved

The bundle carries the source schema's `UId` and the import keeps it, so the target holds the *same*
schema rather than a divergent copy — later updates line up instead of colliding. This is what makes a
repeated export/import cycle safe.

## What it will do, before it does it

Before writing, the command looks up where the schema name already exists on the target and reports one
of three plans:

| Plan | Meaning |
|---|---|
| `CREATE` | The schema does not exist on the target. |
| `REPLACE` | The schema already exists in the target package; that layer is overwritten. |
| `NEW LAYER` | The schema exists in **other** packages only; the import would add a layer. |

`NEW LAYER` is **refused by default**, naming the packages found:

```
Schema 'Leads_FormPageBusinessRule' already exists in package(s) 'CrtCustomer360App', not in
'UsrCustomerApp'. Importing it here would create an additional layer. Re-run with --package-name of
the owning package to replace it, or with --allow-new-layer to create the layer deliberately.
```

Creating a same-named schema in a second package is sometimes exactly what the operator wants, and
sometimes the `IU_Name_Manager_Package` duplicate-key defect this feature was written for. The two are
indistinguishable from here, so the safe branch is the default.

Use `--dry-run` first on any environment you care about — it prints the plan and writes nothing.

## After the import

The schema is saved but **not built**:

- `clio compile-configuration` when the schema carries source code.
- `clio update-db-structure` when the schema changes the database structure.

The command prints this reminder on every successful import.

## Requirements

- cliogate `2.0.0.46` or newer on the environment (`clio install-gate -e <environment>`).
- The target package must be unlocked (`clio unlock-package`).

## Examples

```bash
clio import-schema ./Leads_FormPageBusinessRule --package-name UsrCustomerApp -e prod --dry-run
clio import-schema ./Leads_FormPageBusinessRule --package-name UsrCustomerApp -e prod
```

## MCP

Exposed as the `import-schema` MCP tool (destructive; `dry-run` is the safe probe).

## See also

- [export-schema](export-schema.md)
- [delete-schema](delete-schema.md)
