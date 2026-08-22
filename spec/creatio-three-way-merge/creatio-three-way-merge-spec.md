# Creatio-aware three-way merge MCP specification

## Intent

Let an agent ask clio to semantically merge one Creatio package artifact from Git's base, ours, and
theirs contents without letting clio read or change the repository.

## Scope

Add one resident MCP tool named `merge-creatio-artifact`.

The tool is a pure preview operation:

- input is inline text plus a repository-relative artifact path;
- clio does not read, write, stage, commit, fetch, or push anything;
- the caller decides whether to write a fully resolved result;
- unsupported artifact families fail closed instead of falling back to textual or generic JSON merge.

There is no CLI command in this feature. There is no apply mode and no Git orchestration.

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

For `metadata.json`, the tool parses the supplied descriptor and all three metadata inputs. Every
metadata input must encode the descriptor's schema name and schema UId; when metadata also embeds a
manager name, that must match too. Missing or mismatched identity evidence returns `invalid-input`;
a caller-supplied descriptor is never trusted by itself.

For data bindings, the resolver receives the sibling descriptor through an additive in-memory API.
The existing filesystem-aware resolver API remains available for the Creatio app, but the clio MCP
adapter never invokes it and never reads `artifact-path`.

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
| `diagnostics` | Caller-safe explanations. |

The response does not use `success` or a top-level `error` field, because those are clio-run failure
classification signals. Diagnostics are composed only from fixed templates, the enumerated
`artifact-kind`, and bounded reason tokens; raw exception text and input content are never included.
Merge content is never redacted because rewriting valid JSON, XML, JavaScript, paths, or namespaces
would corrupt the artifact. Content and report strings remain untrusted branch data and callers must
not treat a conflict-free semantic result as author trust or deployment approval.

The response state invariants are:

- `resolved` requires resolver verification to pass, no conflict marker, and `content` present;
- any verification failure becomes `invalid-input`, with no content and
  no raw resolver diagnostic;
- `conflicts-remain` requires explicit marker content;
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
| `descriptor` | `descriptor.json` | semantic |
| `properties` | `properties.json` | semantic |
| `resource` | non-process resource XML | semantic |
| `data-binding` | `data.json` or localized `data.<culture>.json` | semantic |
| `process-schema-metadata` | `ProcessSchemaManager` metadata | `not-implemented` |
| `process-resource` | ProcessSchema resource XML | `not-implemented` |
| `csharp-source` | C# source | `not-implemented` |
| `sql-script` | SQL script | `not-implemented` |
| `unknown-schema-metadata` | unknown schema manager | `unsupported` |
| `unsupported-client-unit-source` | ClientUnit source without supported markers | `unsupported` |

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

Resident placement is an explicit context-budget decision. Implementation must measure the real
serialized default `tools/list` payload, document the added byte cost in `McpProfileGatingTests`, and
raise its ratchet only to the smallest rounded ceiling that accommodates the measured tool. The tool
is not feature-toggle gated.

The MCP class is a thin adapter over an injected `ICreatioArtifactMergeService`. It does not derive
from environment-oriented `BaseTool<T>` because it has no Creatio connection or command options.
Register the service/interface and tool dependencies in `clio/BindingsModule.cs`.

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

On 2026-08-22 the resolver owner explicitly authorized using and maintaining
`Creatio.ConflictResolver` inside clio. The implementation therefore uses source transfer rather
than an opaque DLL or private package dependency.

Before import, the issue must also record that the authorization comes from a rights holder and
explicitly covers public modification and redistribution under clio's MIT license. The imported
source is pinned to its source commit and tree, retains attribution, and carries package/license
metadata. Authorization to use the code is not silently treated as that public-license grant.

clio becomes the single source and semantic-test owner. The resolver remains one separate,
`netstandard2.0`-only project so both consumers execute the same target binary:

1. clio references the resolver project directly;
2. clio's release produces one versioned resolver package;
3. `crt-git-integration-app` pins that package and copies its `netstandard2.0` DLL into the existing
   Creatio assembly location;
4. after consumer verification, the resolver source copy in `crt-git-integration-app` is removed.

The source transfer includes the existing semantic fixture suite and a provenance notice. It does
not transfer the standalone resolver CLI, batch scripts, or a second solution. During the two-repo
migration, behavior changes are made only in clio so the temporary old app copy cannot become a
second maintained implementation.

The exact source commit, subtrees, baseline test result, and import verification procedure are
recorded in `creatio-three-way-merge-provenance.md`.

The resolver keeps its established assembly identity for the Creatio descriptor and uses an
independently controlled package/informational version for `resolver-version`. clio release version
properties must not flow into the resolver ProjectReference. The proven `System.Text.Json`
compatibility line remains centrally pinned until both supported Creatio runtime families pass the
app smoke test with a newer line.

## Non-goals

- Git index access or branch merge orchestration;
- repository path reads;
- writing or staging resolved content;
- committing or pushing;
- semantic business-process merge;
- textual fallback for unsupported Creatio artifacts;
- a second capabilities tool or a capabilities mode on the merge call.

## Definition of done

- the packaged clio MCP server advertises and executes the resident tool;
- contract, unit, real-process MCP E2E, and redaction-survival tests pass;
- the three-developer/two-Creatio validation in the companion test plan produces and resolves a real
  Git conflict and the merged package works in Creatio;
- ClioRing contract tests and Windows x64 NativeAOT publish pass;
- resolver provenance is documented and reproducible;
- the reusable lab runbook can reset and repeat the three-workspace/two-instance proof;
- no write/apply behavior has entered the preview tool.
