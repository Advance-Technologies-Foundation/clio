# get-classic-migration-bundle

## Command Type

    Development commands

## Name

get-classic-migration-bundle - Assemble a Classic->Freedom migration bundle and write the manifest JSON to disk

**Aliases:** `classic-migration-bundle`

## Description

The get-classic-migration-bundle command assembles, server-side, everything the Classic->Freedom
migration engine (`migrate.mjs`) needs to fold a classic page, and writes it to disk as a manifest JSON.

It:

- enumerates the whole replacing-schema layer chain, ordered **base->top** by package hierarchy level;
- fetches every layer's raw body (`schemas`);
- walks the parent-template chain into the `seed`, enumerating **every layer** of each parent template
  (base->top) so base containers defined in any package layer are seeded, not just the linked layer;
- resolves the entity (from `--entity` or inferred from the page body) and gathers `entityColumns` and
  `columnTitles` from the merged entity schema;
- gathers the localizable strings merged across the hierarchy into `resources`;
- best-effort, gathers the related schemas the page references: custom `detailSchemas` (body + title), the
  `*Section` chain, and each detail's child edit page as a nested `childPageSchemas` manifest. These use
  conservative heuristics; anything that cannot be resolved is **omitted, never fabricated**.

The layer bodies are written to the manifest file, **never returned** in the command output. The response
carries only the manifest path and a small summary (layer/seed/resource/column counts and the resolved
entity), keeping the often-large schema bodies out of the caller's context.

The manifest matches the input contract of the migration engine, so it can be folded directly:
`node engine/migrate.mjs <manifest>`.

## Synopsis

```bash
clio get-classic-migration-bundle [options]
```

## Options

```bash
--schema-name                      Classic client-unit (page) schema name to assemble
                                   the bundle for (required)

--entity                           Entity schema name (optional; inferred from the page
                                   body when omitted). Drives entityColumns/columnTitles

--output-file                      Manifest output path. Default:
                                   <workspace-root>/.clio-migration/<schema>/manifest.json

--uri                    -u       Application uri

--Password               -p       User password

--Login                  -l       User login (administrator permission required)

--Environment            -e       Environment name
```

## Example

```bash
clio get-classic-migration-bundle --schema-name ContactPageV2 -e dev
# Assemble the ContactPageV2 bundle -> <workspace-root>/.clio-migration/ContactPageV2/manifest.json

clio get-classic-migration-bundle --schema-name UsrCasePage --entity UsrCase --output-file ./bundle.json -e dev
# Assemble with an explicit entity and output path
```

## Output format

The response JSON reports `success`, `schemaName`, `entity`, `manifestPath`, `layerCount`, `seedCount`,
`resourceCount`, `columnCount`, `detailCount`, `sectionLayerCount`, `childPageCount`, and `error`. The
manifest file written to disk contains `schemas` (`[{ pkg, body }]`, base->top), and, when resolvable,
`seed`, `entity`, `entityColumns`, `columnTitles`, `resources`, `detailSchemas`, `section`, and
`childPageSchemas`.

## Notes

- Read-only: the command only reads schema metadata and writes the manifest file; it does not modify the
  Creatio environment and does not invoke the Node engine.
- A schema name that exists in several packages resolves its layers deterministically by package hierarchy
  level (see also `get-client-unit-schema`).

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#get-classic-migration-bundle)
