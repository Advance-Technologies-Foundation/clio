# ADR: Export Component Registry as a byte-passthrough file, via a new tool

**Status**: Accepted
**Date**: 2026-08-21
**Related**: [spec-export-component-registry](../prd/spec-export-component-registry.md), ENG-95543

## Context

`creatio-ai-app-development-toolkit`'s classic→Freedom migration engine needs to validate
dozens of `crt.*` componentTypes and `propMap` keys per run, including in CI with no live
Creatio stand. `get-component-info` (`ComponentInfoTool.cs`) answers one component per
call and is `ReadOnly = true`; `IComponentRegistryClient` already fetches and caches the
full per-version registry (`ComponentRegistryClient.cs`, `ComponentRegistryCacheStore.cs`),
but no MCP tool or CLI verb exposes that full payload as a file.

## Decision

### D1 — New tool, not a new argument
Add `export-component-registry` as its own MCP tool + CLI verb. Do not add a file-output
argument to `get-component-info`: that tool is declared `ReadOnly = true`
(`ComponentInfoTool.cs:89`), and a write capability would silently change its declared
safety contract. New tool declares `ReadOnly = false, Destructive = false, Idempotent =
true, OpenWorld = false`.

### D2 — Byte passthrough, not re-serialization
The file is written from the raw bytes `IComponentRegistryClient.GetAsync` returns, not
from a re-serialization of `ComponentCatalogState`/`ComponentRegistryEntry`. Verified
against `clio.tests/Command/McpServer/Fixtures/ComponentRegistry.live-snapshot.json`: an
`inputs` entry's `deprecated`/`deprecationReason` pair exists only as raw JSON — the typed
model (`ComponentPropertyDefinition`) has no field for it. Re-serializing through the typed
model would silently drop exactly the fields the ENG-95543 consumer needs. For the same
reason the response counters (component/composite/input counts) are computed off the same
raw bytes that are written to disk (`ExportComponentRegistryCommand.CountEntries`, a
`JsonDocument` walk over the fetched JSON), not through `IComponentInfoCatalog.LoadAsync`/
`ComponentCatalogState`: a counter derived from the typed model could disagree with what
the file actually contains for any field that model does not map.

### D3 — No docs bodies
`ComponentDocumentationLoader.LoadAsync` / `IComponentRegistryDocsClient.GetDocAsync` are
never called by this tool. `references.docs` path strings are already present in the raw
registry JSON and pass through with the rest of the payload; fetching ~150-190 doc bodies
per run is out of scope (1.2–1.5 MB, ~190 HTTP round-trips) and not needed for
componentType/propMap validation.

### D4 — Shape: plain CLI command class + plain MCP tool class (ComponentInfoCommand/ComponentInfoTool shape), NOT `BaseTool<TOptions>`
Revised during implementation after checking a concrete correctness constraint. Originally
this ADR called for the `GetClassicPageSourcesCommand`/`GetClassicPageSourcesTool` shape
(`Command<TOptions>` + `BaseTool<TOptions>` wrapper), reasoning that it already solves
`output-file` confinement and CLI-verb-with-async-bridging. That reasoning undersold a
constraint the version-resolution flow actually has: `BaseTool<TOptions>.ResolveCommand<TCommand>`
(and the CLI `Resolve<TCommand>(opts)` path) EAGERLY builds a per-environment container and
would eagerly resolve an `EnvironmentSettings`-typed constructor dependency even when the
caller passed only `--version` (no `environment-name`/`uri` at all) — which fails or forces an
unwanted default-environment binding on the exact call shape (`--version` only, or neither
flag → `latest-fallback`) this tool must support cleanly. `ComponentInfoCommand`/`ComponentInfoTool`
already solve this correctly: they resolve `EnvironmentSettings` LAZILY, only inside the
`hasEnvironment` branch (`ISettingsRepository.GetEnvironment` for the CLI verb,
`IToolCommandResolver.Resolve<EnvironmentSettings>(options)` — the ENG-93208
credential-passthrough-aware seam — for the MCP tool), and skip it entirely for the
explicit-`version` and no-flags branches. `export-component-registry` has the identical
three-way branch (explicit version / environment probe / no-active-environment fallback), so
it follows `ComponentInfoCommand`/`ComponentInfoTool`'s shape instead: a plain CLI command
class (`Execute` → `ExecuteAsync(...).GetAwaiter().GetResult()`, no `Command<TOptions>` base —
`ComponentInfoCommand` and `ComponentRegistryRefreshCommand` are both precedent for a plain
class dispatched via `Resolve<TCommand>().Execute(opts)` in `Program.cs`) and a plain MCP tool
class (no `BaseTool<TOptions>` base — same justification `ComponentInfoTool` already
establishes: "a strong reason not to" per the MCP `AGENTS.md` uniformity rule). `output-file`
confinement is unaffected by this: `OutputPathConfinement`/`OutputPathConfinement.WriteAtomic`
are `internal static` members of the `Clio.Command` namespace, callable from anywhere in the
same assembly (internal is assembly-scoped, not namespace-scoped) — the MCP tool class calls
them directly regardless of which base class it derives from.

### D5 — Two different output-path contracts
- Explicit `--output-file`: goes through `OutputPathConfinement.Resolve` +
  `OutputPathConfinement.WriteAtomic` — symlink resolution, workspace/temp-only anchor,
  refuses `..`-escape and refuses an already-existing target, all before any write.
- No `--output-file` (default): `<workspace-root>/.clio-migration/component-registry/[mobile/]<version>.json`,
  computed the same way `GetClassicPageSourcesCommand.ResolveOutputPath` computes its
  default — tool-owned, bypasses the existing-target refusal, and is overwritten on every
  rerun.
These are deliberately different contracts; do not collapse them into one test or one code
path.

### D6 — Reuse, no duplication
Reuse verbatim: `IComponentRegistryClient`/`ComponentRegistryClient` (including
`RegistryFlavor.Web`/`Mobile` and `IMobileComponentRegistryClient` for `schema-type`),
`ComponentRegistryCacheStore`, `ComponentInfoResolution` (version-tier contract:
`resolvedTargetVersion`, `resolvedFrom`, `resolvedFromReason`, `requiresVersionConfirmation`),
`SensitiveErrorTextRedactor`, `OutputPathConfinement`. `BaseTool<TOptions>` is deliberately
NOT on this list — see D4 for the eager environment-resolution constraint that rules it
out. Do not duplicate `OutputPathConfinementTests.cs`'s guard-level unit tests — only add thin
command/tool-level integration tests that prove the guard is wired in.

### D7 — Registration: long-tail by default
DI registration in `BindingsModule.cs` plus `[McpServerToolType]`/`[McpServerTool]`
attributes is sufficient for `McpToolInvokerRegistry` (reflection-based discovery). Do not
add the new tool to `McpCoreToolProfile.CoreToolTypes` or to `ToolContractGetTool`'s
curated catalog — mirrors `get-classic-page-sources`, which is also long-tail. Rationale:
the primary consumer (CI, no live stand) calls the CLI verb directly with `--version`; an
MCP agent that needs it reaches it through `clio-run`/discovery like any other long-tail
tool. Revisit only if a concrete hot-path MCP use case emerges.

## Consequences

- Positive: one file replaces dozens of `get-component-info` round-trips for the migration
  engine's bulk validation use case; zero data loss vs. the source registry; no new HTTP
  load from docs fetching.
- Positive: reuses the entire existing version-resolution and caching stack — no new
  version-tier semantics to test or explain.
- Negative: the file's schema is whatever the upstream registry CDN emits, unvalidated
  against a typed contract at write time — acceptable because the consumer (ENG-95543's
  migration engine) already parses the registry independently and the alternative (typed
  re-serialization) is strictly lossy.
- Negative/tracked: long-tail MCP registration means an MCP-only agent must discover the
  tool via `clio-run`/`get-tool-contract` rather than seeing it in the default tool list;
  acceptable per D7's stated primary-consumer rationale.

## Companion artifacts required (per AGENTS.md, not optional)

`clio/help/en/export-component-registry.txt`, `clio/docs/commands/export-component-registry.md`,
`clio/Commands.md`, `clio/Wiki/WikiAnchors.txt`, `clio.mcp.e2e` coverage, PR body
statements ("docs reviewed, no update required" is NOT applicable here — docs must be
added; "MCP reviewed…"; ClioRing compatibility line), and a `docs/knowledge/McpServer/`
record for the D4 constraint (the former `./.codex/workspace-diary.md` is archived
read-only per AGENTS.md — do not append to it).
