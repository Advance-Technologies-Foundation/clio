# SPEC: Export Component Registry (MCP tool + CLI verb)

**Created**: 2026-08-21
**Size estimate**: S (1 story)
**Recommended next**: /bmad-spec is sufficient — proceed directly to implementation

---

## Why

`creatio-ai-app-development-toolkit`'s classic→Freedom migration engine (ENG-95543,
branch `feature/ENG-95543-registry-backed-mapping`) must validate every emitted `crt.*`
componentType and every `propMap` key against the target platform version's component
registry — including in CI, where no live stand exists. `get-component-info` answers one
component per call (~10 KB per call, measured on `crt.IconRadioButton`); the mapping table
references dozens of types, so per-type calls do not scale. `ComponentRegistryClient`
already downloads the FULL registry for a resolved version (410–648 KB depending on
version) and caches it at `<clio-home>/cache/component-registry/{version}.json` (TTL 5
min) — the data already exists in the client, only a way to hand it back as one file is
missing.

## Capabilities

| ID | Intent (WHAT) | Success Signal (HOW WE KNOW) |
|----|--------------|------------------------------|
| CAP-01 | Resolve the target platform version from `environment-name` (preferred) or explicit `version`, using the same tiered contract as `get-component-info` (`resolvedTargetVersion`, `resolvedFrom`, `resolvedFromReason`, `requiresVersionConfirmation`) | Given an environment with a known version, `resolvedFrom == "environment"`; given no environment/version, `resolvedFrom == "latest-fallback"` and `requiresVersionConfirmation == true` |
| CAP-02 | Write the resolved version's full component registry to a file, byte-identical to what `IComponentRegistryClient` fetched (no field loss from re-serializing through a typed model) | A `deprecated`/`deprecationReason` pair present in the source registry JSON is present, unchanged, in the written file |
| CAP-03 | Never fetch documentation bodies (`references.docs` paths are written as-is; the doc content itself is not fetched) | `IComponentRegistryDocsClient.GetDocAsync` is invoked zero times during the tool's execution |
| CAP-04 | Confine an explicit `output-file` to the workspace or OS temp root, resolving symlinks, and refuse to overwrite an existing file, before any write happens | A path containing `..`, an absolute system path, or a symlink that escapes the allowed zones is rejected with no file written; a path that already exists is rejected with no file written |
| CAP-05 | Fall back to a deterministic default path when `output-file` is omitted, and allow that specific tool-owned path to be overwritten on a repeat run | Two consecutive runs with no `output-file` both succeed and the second overwrites the first at `<workspace-root>/.clio-migration/component-registry/[mobile/]<version>.json` |
| CAP-06 | Return a response that carries the file path, version-resolution fields, and structural counters (components / composites / inputs) but never the registry content itself | The response body, serialized to JSON, contains no `componentType` occurrence |
| CAP-07 | Support both the `web` (default) and `mobile` `schema-type` registries via the existing flavor split, reusing the existing selection path rather than a new switch | `schema-type=mobile` produces a file sourced from `IMobileComponentRegistryClient`, `web` from `IComponentRegistryClient` |

## Constraints

- **C1**: Must be a new MCP tool (`export-component-registry`), not a new argument on
  `get-component-info` — that tool is `ReadOnly = true` (ComponentInfoTool.cs:89); adding a
  file-write argument would silently change its safety contract. New tool's attributes:
  `ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false` (only the
  default-path shape repeats safely; an explicit `output-file` is refused once it exists).
- **C2**: `environment-name` and `version` are mutually exclusive (mirrors
  `ComponentInfoTool`'s existing mutual-exclusivity check).
- **C3**: Must not duplicate logic already owned by `IComponentRegistryClient`,
  `ComponentRegistryCacheStore`, `ComponentInfoResolution`,
  `SensitiveErrorTextRedactor`, or `OutputPathConfinement` — reuse each verbatim
  (`BaseTool<TOptions>` excluded, see the revision note below). In
  particular, do not re-write `OutputPathConfinementTests.cs`-style unit tests for the
  guard itself; only add the thin command/tool-level integration tests.
  Shape follows `GetClassicPageSourcesCommand`/`GetClassicPageSourcesTool` (CLI
  `Command<TOptions>` + thin `BaseTool<TOptions>` MCP wrapper) rather than
  `ComponentInfoTool` (no CLI verb, no `BaseTool` base). REVISED during implementation: the
  shipped shape follows `ComponentInfoCommand`/`ComponentInfoTool` (flat classes) because
  `BaseTool.ResolveCommand` eagerly builds a per-environment container and would break the
  explicit-version-only and no-flags paths (ADR D4). The rationale below still holds in that
  this feature needs both a
  CLI verb and `output-file` confinement, which is the shape `GetClassicPageSourcesCommand`
  already solves, including bridging async registry-fetch work inside a synchronous
  `Execute`.
- **C4**: Registration is DI (`BindingsModule.cs`) + `[McpServerToolType]`/`[McpServerTool]`
  attributes only — `McpToolInvokerRegistry` discovers tools by reflection. Default to
  long-tail (do NOT add to `McpCoreToolProfile.CoreToolTypes` or the `ToolContractGetTool`
  curated catalog), matching `get-classic-page-sources`, because the primary consumer (CI
  with no live stand) reaches this through the CLI verb (`--version`), not through MCP.
- **C5**: All required companion artifacts per `AGENTS.md` MCP/doc-review policy:
  `clio/help/en/export-component-registry.txt`, `clio/docs/commands/export-component-registry.md`,
  `clio/Commands.md`, `clio/Wiki/WikiAnchors.txt`, `clio.mcp.e2e` coverage, PR body
  statements ("docs reviewed…", "MCP reviewed…", ClioRing compatibility line), and a
  `./.codex/workspace-diary.md` entry.

## Non-goals

- Will NOT fetch or embed documentation-file contents (`references.docs` bodies) — paths
  only.
- Will NOT add a file-writing argument to the existing `get-component-info` tool.
- Will NOT introduce a new registry data model — the file is the registry client's raw
  response body, not a re-serialized DTO.
- Will NOT make the tool MCP-resident (`CoreToolTypes`) without a demonstrated hot-path
  need; ships long-tail like its CLI-verb sibling.

## Success Signal

Running `clio export-component-registry --environment-name <env>` (or `--version X.Y.Z`)
writes the resolved version's full registry JSON, byte-faithful to the source, to
`<workspace-root>/.clio-migration/component-registry/[mobile/]<version>.json` (or the confined
`--output-file`), and prints a result containing the file path, resolved-version fields,
and component/composite/input counters — with zero calls to the docs client and no
registry content in the response body.

---

## Companion Notes

- Confirmed by inspecting `clio.tests/Command/McpServer/Fixtures/ComponentRegistry.live-snapshot.json`:
  the registry envelope's `inputs` entries carry `deprecated`/`deprecationReason` as raw
  JSON only — `ComponentPropertyDefinition`/`ComponentRegistryEntry` do not model those
  fields. This is why CAP-02 requires a byte passthrough rather than re-serializing through
  `ComponentCatalogState`.
- Counters (CAP-06) are computed off the same raw bytes written to disk, not from
  `IComponentInfoCatalog.LoadAsync`/`ComponentCatalogState` (revised during implementation):
  a counter derived from the typed model could disagree with the file's actual contents for
  any field that model does not map.
- The default-path contract (CAP-05, overwrite-on-rerun) and the explicit `output-file`
  contract (CAP-04, refuse-if-exists) are deliberately different, mirroring
  `GetClassicPageSourcesCommand.ResolveOutputPath` (default path bypasses
  `OutputPathConfinement.Resolve`'s existing-target refusal because it's the tool's own
  re-runnable output) vs. an explicit `output-file` (always goes through
  `OutputPathConfinement.Resolve` + `WriteAtomic`, refuses an existing target). Tests must
  not collapse these into one assertion.
- `schema-type=mobile` selection should reuse whatever existing `ComponentInfoTool`
  mechanism dispatches between `IComponentRegistryClient` and
  `IMobileComponentRegistryClient` (reported as `ComponentInfoResolution.RunWithSchemaTypeWarningAsync`
  or equivalent) rather than a new hand-written switch.
