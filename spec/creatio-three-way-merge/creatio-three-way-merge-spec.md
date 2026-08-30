# Creatio-aware three-way merge specification

## Intent

Let developers and agents ask clio to semantically merge one Creatio package artifact from Git's
base, ours, and theirs contents without letting clio orchestrate or change the repository.

## Scope

Add one CLI command and one resident MCP tool, both named `merge-creatio-artifact`, over one shared
`ICreatioArtifactMergeService`. Both surfaces are available by default.

Both surfaces are pure preview operations:

- the CLI reads only the exact stage and descriptor files named by its options;
- MCP accepts inline text plus a repository-relative artifact path and performs no filesystem access;
- neither surface writes, stages, commits, fetches, pushes, or discovers repository state;
- the caller decides whether to write a fully resolved result;
- unsupported artifact families fail closed instead of falling back to textual or generic JSON merge.

There is no apply mode and no Git orchestration.

The tool's `tools/list` description names supported families in one compact line and explicitly says
that ProcessSchema, C#, and SQL merge are not implemented. The complete matrix is available through
the curated `get-tool-contract` entry. Callers must not need to invoke the merge speculatively to
discover whether a known Creatio artifact type is implemented.

## MCP contract

### Input

| Field | Required | Meaning |
|---|---:|---|
| `artifact-path` | yes | Repository-relative classification hint. It is never opened. Rooted paths and `..` segments are rejected. |
| `base-content` | yes | Git stage 1 content. |
| `ours-content` | yes | Git stage 2 content. |
| `theirs-content` | yes | Git stage 3 content. |
| `descriptor-content` | for `metadata.json` and data bindings | Already-resolved, marker-free sibling descriptor used only for in-memory schema classification and data-binding merge context. |

The combined UTF-8 size of all content fields is limited to 4 MiB and is checked before resolver
parsing. The MCP transport necessarily materializes the JSON-RPC request before tool validation, so
this is a resolver-work budget, not a transport-memory guarantee. Rooted paths, `..` segments, and
unknown input properties are rejected.
Flat metadata is additionally limited to 2,500 operations per stage so its JSON transpilation stays
within the resolver's memory budget; larger stages return `invalid-input` before transformation.

For `metadata.json`, the tool parses the supplied descriptor and all three metadata inputs. Every
metadata input must encode the descriptor's schema name and schema UId; when metadata also embeds a
manager name, that must match too. Missing or mismatched identity evidence returns `invalid-input`;
a caller-supplied descriptor is never trusted by itself.

For data bindings, the resolver receives the sibling descriptor through an additive in-memory API.
The clio MCP adapter never invokes the resolver's filesystem fallback and never reads
`artifact-path`.

The tool always requests the resolver's conflict-marker output for supported logical conflicts. It
does not expose the resolver's internal file-type or merge-mode enums to the agent.

### Output

Every handled domain outcome is represented by `status`. An unexpected exception remains an MCP
invocation error.

| Field | Meaning |
|---|---|
| `status` | `resolved`, `conflicts-remain`, `not-implemented`, `unsupported`, or `invalid-input`. |
| `artifact-kind` | The detected Creatio artifact family. |
| `resolver-version` | Version of the resolver assembly used for the result. |
| `content` | Present for `resolved` or `conflicts-remain`; contains the verified merge or explicit logical conflict markers respectively. |
| `report` | Resolution type, winner policy, verification flag, additions, deletions, and true conflicts. |
| `diagnostics` | Caller-safe explanations; recognized EntitySchema column type conflicts also include the exact question to ask the user. |

The response does not use `success` or a top-level `error` field, because those are clio-run failure
classification signals. Diagnostics are composed only from fixed templates, the enumerated
`artifact-kind`, canonical type names, and bounded schema identifiers or reason tokens; raw
exception text and arbitrary input content are never included.
Merge content is never redacted because rewriting valid JSON, XML, JavaScript, paths, or namespaces
would corrupt the artifact. Content and report strings remain untrusted branch data and callers must
not treat a conflict-free semantic result as author trust or deployment approval.

The response state invariants are:

- `resolved` requires resolver verification to pass, no conflict marker, and `content` present;
- any verification failure becomes `invalid-input`, with no content and
  no raw resolver diagnostic;
- `conflicts-remain` requires explicit marker content;
- a recognized EntitySchema `Body.S2` conflict adds a question such as `Which type should
  UsrColumn keep: Number or Date/Time?`; the caller must ask before selecting a marker side;
- every other status has no content.

Returned `content` is limited to 4 MiB UTF-8. An oversized resolver result is withheld and returned
as `invalid-input` with a fixed diagnostic.

`not-implemented` is reserved for a recognized Creatio artifact family whose semantic merge is not
implemented in this release. Its diagnostic names the detected type and uses the stable form
`Merge for <artifact-kind> is not implemented yet.` `unsupported` is reserved for input that can be
classified structurally but is outside the known support matrix. Neither status returns content.

## Artifact-kind and support matrix

This table is normative. The resident description is a compact summary; the curated contract and
tests derive their exact vocabulary and behavior from this table.

| `artifact-kind` | Classification | First-release behavior |
|---|---|---|
| `entity-schema-metadata` | `EntitySchemaManager` metadata | semantic |
| `client-unit-metadata` | `ClientUnitSchemaManager` metadata | semantic |
| `service-schema-metadata` | `ServiceSchemaManager` metadata | semantic |
| `addon-appearance-settings-metadata` | `AddonSchemaManager` / `AppearanceSettings` | semantic |
| `addon-business-rule-metadata` | `AddonSchemaManager` / `BusinessRule` | semantic |
| `addon-related-page-metadata` | `AddonSchemaManager` / `RelatedPage` | semantic |
| `addon-timeline-entity-metadata` | `AddonSchemaManager` / `TimelineEntity` | semantic |
| `client-unit-source` | Freedom UI ClientUnit with supported `SCHEMA_*` sections | semantic |
| `descriptor` | non-process `descriptor.json` | semantic |
| `properties` | `properties.json` | semantic |
| `resource` | non-process resource XML | semantic |
| `data-binding` | `data.json` or localized `data.<culture>.json` | semantic |
| `process-schema-metadata` | `ProcessSchemaManager` metadata | `not-implemented` |
| `process-schema-descriptor` | `ProcessSchemaManager` descriptor | `not-implemented` |
| `process-resource` | ProcessSchema resource XML | `not-implemented` |
| `csharp-source` | C# source | `not-implemented` |
| `sql-script` | SQL script | `not-implemented` |
| `unknown-schema-metadata` | unknown schema manager | `unsupported` |
| `unsupported-client-unit-source` | ClientUnit source without supported markers | `unsupported` |
| `unknown-artifact` | unrecognized path shape | `unsupported` |

Semantic kinds may return `resolved`, `conflicts-remain`, or `invalid-input`. Recognized
not-implemented kinds always return `not-implemented` and the fixed diagnostic
`Merge for <artifact-kind> is not implemented yet.` Unknown schema metadata and unclassifiable
ClientUnit source return `unsupported` with no content. Missing, conflicted, stale, or
identity-mismatched descriptor evidence returns `invalid-input` with no content.

No content is returned for `not-implemented`, `unsupported`, or `invalid-input` because the caller
already has all three inputs and clio has no semantic result to contribute.

## Placement

`merge-creatio-artifact` is resident and advertises:

```text
ReadOnly=true, Destructive=false, Idempotent=true, OpenWorld=false
```

Resident placement keeps those safety claims truthful. Routing the pure preview through the generic
`clio-run` bridge would present it through a tool that is deliberately marked destructive.

Resident placement is an explicit context-budget decision. The tool is present in `tools/list` by
default so an agent can discover the supported semantic merge boundary without local configuration.

The MCP class is a thin adapter over an injected `ICreatioArtifactMergeService`. It does not derive
from environment-oriented `BaseTool<T>` because it has no Creatio connection or command options.
Register the service/interface and tool dependencies in `clio/BindingsModule.cs`.

## CLI contract

The CLI is the primary local and lab interface:

```text
clio merge-creatio-artifact --artifact-path <PATH> --base-file <FILE>
  --ours-file <FILE> --theirs-file <FILE> [--descriptor-file <FILE>]
```

It reads only those explicitly named files, maps them to the same in-memory request used by MCP,
and prints the same result as JSON. Exit code `0` is reserved for `status=resolved`; all other
statuses and file-read failures return `1`. The command has no output-file option: applying a result
remains an explicit caller decision.

## Discovery and guidance

- Add a curated `get-tool-contract` entry with the complete input/output contract, support boundary,
  examples, and anti-patterns.
- Keep a compact support summary in the resident description and the exact matrix in the curated
  contract, guarded by a drift test.
- Add the tool to `McpCoreToolProfile` and `docs/McpCapabilityMap.md`.
- Publish agent workflow guidance from `clio-knowledge`; do not duplicate the article in server
  instructions or workspace templates.
- No MCP prompt or resource is required.

## Resolver ownership and distribution

On 2026-08-22 the rights holder explicitly authorized the resolver snapshot for public modification
and redistribution under clio's MIT license. The imported source is pinned to its source commit and
tree, retains attribution, and carries package/license metadata.

clio independently owns its transferred source and semantic tests. The resolver remains one
separate, `netstandard2.0`-only project referenced directly by clio. `crt-git-integration-app` is an
independent product and remains untouched; it is not a consumer or migration target for this
feature.

The source transfer includes the existing semantic fixture suite and a provenance notice. It does
not transfer the standalone resolver CLI, batch scripts, or a second solution. Resolver behavior for
this feature is maintained only in clio.

The exact source commit, subtrees, baseline test result, and import verification procedure are
recorded in `creatio-three-way-merge-provenance.md`.

The resolver keeps its established assembly identity and uses an independently controlled
package/informational version for `resolver-version`. clio release version properties must not flow
into the resolver ProjectReference. The proven `System.Text.Json` compatibility line remains
centrally pinned until clio's supported runtime paths validate a newer line.

## Non-goals

- Git index access or branch merge orchestration;
- repository discovery or implicit path reads;
- writing or staging resolved content;
- committing or pushing;
- semantic business-process merge;
- textual fallback for unsupported Creatio artifacts;
- a second capabilities tool or a capabilities mode on the merge call;
- modifying, packaging, or migrating `crt-git-integration-app`.

## Definition of done

- the packaged clio CLI executes the command from explicit stage files;
- the packaged clio MCP server advertises and executes the same shared behavior through the resident tool;
- contract, unit, real-process MCP E2E, and redaction-survival tests pass;
- the three-developer/three-Creatio validation in the companion test plan produces and resolves a real
  Git conflict and the merged package works in Creatio;
- ClioRing contract tests and Windows x64 NativeAOT publish pass;
- resolver provenance is documented and reproducible;
- the reusable lab runbook can reset and repeat the three-workspace/three-instance proof;
- no write/apply behavior has entered the preview tool.
