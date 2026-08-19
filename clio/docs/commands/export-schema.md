# export-schema

Export a single schema from a Creatio environment into a reviewable bundle folder.

**Alias:** `schema-export`

## Why

A fix confined to one schema could not, until now, be delivered as one schema. The only transfer path
was `pull-pkg` / `push-pkg`, which carries the entire package — every customization the customer has in
it. Installing that package on a production site to deliver a one-schema fix can overwrite unrelated work
that exists only there.

`export-schema` produces a small artifact for exactly one schema, which a reviewer can read and
[`import-schema`](import-schema.md) can apply.

## Usage

```bash
clio export-schema <SchemaName> [--package-name <Package>] -e <Environment> [-d <Folder>]
```

| Option | Description |
|---|---|
| `<SchemaName>` | Name of the schema to export. Required. |
| `--package-name` | Package that owns the layer to export. Required when the name is ambiguous. There is no `-p` short form — `EnvironmentOptions` already binds `-p` to `--password`. |
| `--manager-name` | Schema manager to narrow the lookup to, e.g. `AddonSchemaManager`. |
| `-d`, `--destination` | Directory that will receive the bundle folder. Default: the current directory. Must resolve inside the workspace/current directory or the OS temp directory — the command is MCP-callable, so the write path is confined the same way `get-schema --output-file` is. |
| `-e`, `--environment` | Environment name. |

## Coverage

The export goes through the platform's own schema exporter, which is type-agnostic. That means every
schema kind the platform can export is covered — **including addon schemas** (business rules, related
pages), which have no other read surface in clio.

## Disambiguation

A schema name is unique only per (manager, package) pair, so the same name legitimately exists in several
packages. When it does, the command **fails and lists the packages** rather than picking one:

```
Schema 'Contact' exists in 5 packages: Completeness, CrtCoreBase, CrtMobile7x, MLangContent, SSP.
Specify the package to export.
```

Re-run with `--package-name <Package>`.

## Bundle layout

```
<SchemaName>/
  descriptor.json      provenance: schema name, UId, manager, source package,
                       source environment, export timestamp, clio version
  schema-data.json     the verbatim platform payload — the ONLY file import reads
  metadata.json        projection of the payload's MetaData, expanded for reading
  properties.json      projection of the payload's Properties
  resources/           projection of the payload's LocalizableValues, one file per culture
```

Only `schema-data.json` is authoritative. The projections exist because a single escaped-JSON blob is not
reviewable, and the point of the feature is a reviewable handover. Import reads `schema-data.json` and
ignores the projections, so editing a projection can never silently change what ships.

Export never overwrites an existing bundle folder.

## Requirements

cliogate `2.0.0.46` or newer on the environment:

```bash
clio install-gate -e <environment>
```

## Examples

```bash
clio export-schema Leads_FormPageBusinessRule --package-name UsrCustomerApp -e dev
clio export-schema UsrMyService --package-name UsrCustomerApp -e dev -d ./handover
```

## MCP

Exposed as the `export-schema` MCP tool (read-only, non-destructive).

## See also

- [import-schema](import-schema.md)
- [delete-schema](delete-schema.md)
