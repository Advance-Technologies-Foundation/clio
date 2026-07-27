# get-classic-page-sources

## Command Type

    Development commands

## Name

get-classic-page-sources - Collect the Classic page sources for folding and write the manifest JSON to disk

**Aliases:** `classic-page-sources` (deprecated: `get-classic-migration-bundle`, `classic-migration-bundle`)

## Description

The get-classic-page-sources command collects, server-side, everything the Classic->Freedom
migration engine (`migrate.mjs`) needs to fold a classic page, and writes it to disk as a manifest JSON.
It gathers the raw page sources for folding — it does not itself fold them, so the same manifest is equally
useful for page audits, layer diffing, and tracing where a container came from.

It:

- resolves the page's whole replacing-schema layer chain **and** its parent-template `seed` in a single
  page-designer hierarchy call (`GetParentSchemas`, full hierarchy), ordered **base->top** by package
  hierarchy level: the page's own layers (with raw bodies) become `schemas`, the ancestor templates the `seed`;
- falls back to a per-layer enumeration — fetch each layer body, then walk the parent-template chain,
  enumerating every same-named template layer — when the hierarchy call is unavailable, producing the same
  manifest shape;
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
clio get-classic-page-sources [options]
```

## Options

```bash
--schema-name                      Classic client-unit (page) schema name to collect
                                   the page sources for (required)

--entity                           Entity schema name (optional; inferred from the page
                                   body when omitted). Drives entityColumns/columnTitles

--output-file                      Manifest output path. Must resolve inside the
                                   workspace or the OS temp directory; a path outside
                                   both is rejected. Default:
                                   <workspace-root>/.clio-migration/<schema>/manifest.json

--uri                    -u       Application uri

--Password               -p       User password

--Login                  -l       User login (administrator permission required)

--Environment            -e       Environment name
```

## Example

```bash
clio get-classic-page-sources --schema-name ContactPageV2 -e dev
# Collect the ContactPageV2 sources -> <workspace-root>/.clio-migration/ContactPageV2/manifest.json

clio get-classic-page-sources --schema-name UsrCasePage --entity UsrCase --output-file ./sources.json -e dev
# Collect with an explicit entity and output path
```

## Output format

The response JSON reports `success`, `schemaName`, `entity`, `manifestPath`, `layerCount`, `seedCount`,
`resourceCount`, `columnCount`, `detailCount`, `sectionLayerCount`, `childPageCount`, `warnings`, and
`error`. The manifest file written to disk contains `schemas` (`[{ pkg, body }]`, base->top), and, when
resolvable, `seed`, `entity`, `entityColumns`, `columnTitles`, `resources`, `detailSchemas`, `section`, and
`childPageSchemas`.

`warnings` is present only when the collected sources are incomplete in a way the caller must weigh, and is
omitted from a complete collection. It is raised when no section could be resolved (`sectionLayerCount: 0`) —
which empties the List-page side of a migration plan and is not the same as "this entity has no section" —
when the section metadata lookup failed and the run fell back to naming conventions, and when pattern matching
over a schema body timed out and that body was skipped, so `detailCount` / `sectionLayerCount` may read lower
than the page actually has. Over MCP the warning text is redacted the same way `error` is, so a backend host or
URI carried in an underlying failure never reaches the caller's context.

## Notes

- Read-only: the command only reads schema metadata and writes the manifest file; it does not modify the
  Creatio environment and does not invoke the Node engine.
- A schema name that exists in several packages resolves its layers deterministically by package hierarchy
  level (see also `get-client-unit-schema`).
- The section is resolved from `SysModule` metadata first (the module bound to the entity), and only then by
  the `<Entity>Section[V2]` / `<PagePrefix>Section[V2]` naming conventions. Metadata leads because a section
  can be renamed or carry a UId/app infix (entity `ASPContractData` -> section `ASPContractDatac145c7efSection`),
  which no name derivation can reach. A failed metadata lookup degrades to the conventions and is reported in
  `warnings`.
- `--output-file` is confined to the workspace anchor or the OS temp directory. The command is MCP-callable, so
  the output path can be supplied by an agent rather than typed at a shell; writing an unconstrained path
  verbatim would let a `..` traversal or an absolute system path overwrite an arbitrary file. A path escaping
  both allowed locations fails before any write. The default path is always inside the workspace anchor.

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#get-classic-page-sources)
