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

## MCP contract

### Input

| Field | Required | Meaning |
|---|---:|---|
| `artifact-path` | yes | Repository-relative classification hint. It is never opened. Rooted paths and `..` segments are rejected. |
| `base-content` | yes | Git stage 1 content. |
| `ours-content` | yes | Git stage 2 content. |
| `theirs-content` | yes | Git stage 3 content. |
| `descriptor-content` | for `metadata.json` | Already-resolved, marker-free sibling descriptor used only for in-memory schema classification. |

The combined UTF-8 size of all content fields is limited to 4 MiB and is checked before resolver
parsing. The MCP transport necessarily materializes the JSON-RPC request before tool validation, so
this is a resolver-work budget, not a transport-memory guarantee. Rooted paths, `..` segments, and
unknown input properties are rejected.

For `metadata.json`, the tool parses the supplied descriptor and all three metadata inputs. Every
metadata input must encode the descriptor's schema name and schema UId; when metadata also embeds a
manager name, that must match too. Missing or mismatched identity evidence returns `invalid-input`;
a caller-supplied descriptor is never trusted by itself.

The tool always requests the resolver's conflict-marker output for supported logical conflicts. It
does not expose the resolver's internal file-type or merge-mode enums to the agent.

### Output

Every handled domain outcome is represented by `status`. An unexpected exception remains an MCP
invocation error.

| Field | Meaning |
|---|---|
| `status` | `resolved`, `conflicts-remain`, `manual-required`, `unsupported`, or `invalid-input`. |
| `artifact-kind` | The detected Creatio artifact family. |
| `support-level` | `semantic`, `manual`, or `none`. |
| `can-apply-automatically` | True only for verified, marker-free `resolved` content. |
| `resolver-version` | Version of the resolver assembly used for the result. |
| `merged-content` | Present only when `status` is `resolved`. |
| `conflict-content` | Present only when a supported semantic merge produced logical conflict markers. |
| `report` | Resolution type, winner policy, verification flag, additions, deletions, and true conflicts. |
| `diagnostics` | Caller-safe explanations. |

The response does not use `success` or a top-level `error` field, because those are clio-run failure
classification signals. Every diagnostic is scrubbed by `SensitiveErrorTextRedactor` at the adapter
boundary, and raw exception text is never returned. Merge content is not passed through the redactor,
because rewriting valid JSON, XML, JavaScript, paths, or namespaces would corrupt the artifact.

`can-apply-automatically` is intentionally explicit because it is an acceptance-level agent safety
signal. It is never inferred merely from the presence of content.

The response state invariants are:

- `resolved` requires resolver verification to pass, no conflict marker, `merged-content` present,
  and `can-apply-automatically=true`;
- any verification failure becomes `invalid-input`, with no content and
  `can-apply-automatically=false`;
- every other status has `can-apply-automatically=false`;
- `support-level=semantic` only for artifact kinds routed to a proven semantic strategy,
  `manual` only for the explicit manual families, and `none` otherwise.

Returned `merged-content` or `conflict-content` is limited to 4 MiB UTF-8. An oversized resolver
result is withheld and returned as `invalid-input` with a scrubbed diagnostic.

## Supported behavior

The first release exposes only behavior already proven by the resolver's fixture suite:

- `EntitySchemaManager`, `ClientUnitSchemaManager`, and `ServiceSchemaManager` metadata;
- `AddonSchemaManager` metadata for `AppearanceSettings`, `BusinessRule`, `RelatedPage`, and
  `TimelineEntity`;
- supported Freedom UI ClientUnit `SCHEMA_*` sections;
- descriptors, properties, resources, and data bindings.

The following outcomes remain fail closed:

- ProcessSchema metadata and resources: `manual-required`;
- C# source and SQL scripts: `manual-required`;
- unknown schema managers: `unsupported`;
- ClientUnit source without supported markers: `invalid-input` or `unsupported`;
- missing, conflicted, unknown, stale, or identity-mismatched descriptor evidence for
  `metadata.json`: `invalid-input` or `unsupported`.

No content is returned for `manual-required` or `unsupported` outcomes because the caller already
has all three inputs and clio has no semantic result to contribute.

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
- Add the tool to `McpCoreToolProfile` and `docs/McpCapabilityMap.md`.
- Publish agent workflow guidance from `clio-knowledge`; do not duplicate the article in server
  instructions or workspace templates.
- No MCP prompt or resource is required.

## Dependency and provenance gate

`Creatio.ConflictResolver` currently exists only as source in the private
`crt-git-integration-app` repository. It has no license file, package metadata, or public NuGet
release. clio is MIT and must not copy that source or ship an opaque DLL without an approved grant.

Implementation of the executable tool requires one approved resolver distribution:

1. preferred: publish a licensed, versioned `Creatio.ConflictResolver` NuGet package and centrally
   pin it in clio; or
2. explicitly approve relicensing and source transfer into clio.

The MCP contract and tests must not introduce a placeholder merge implementation while this gate is
open.

## Non-goals

- Git index access or branch merge orchestration;
- repository path reads;
- writing or staging resolved content;
- committing or pushing;
- semantic business-process merge;
- textual fallback for unsupported Creatio artifacts;
- a second capabilities tool or a capabilities mode on the merge call.

## Gate-open deliverable

While resolver provenance is unresolved, only this conventional specification, test plan, and a
visible blocked issue state may be delivered. Do not add a contract-only or placeholder MCP tool
that advertises functionality clio cannot execute.

## Definition of done after the gate closes

- the packaged clio MCP server advertises and executes the resident tool;
- contract, unit, real-process MCP E2E, and redaction-survival tests pass;
- the three-developer/two-Creatio validation in the companion test plan produces and resolves a real
  Git conflict and the merged package works in Creatio;
- ClioRing contract tests and Windows x64 NativeAOT publish pass;
- resolver provenance is documented and reproducible;
- no write/apply behavior has entered the preview tool.
