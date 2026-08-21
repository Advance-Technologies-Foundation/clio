# Story: export-component-registry-1 — MCP tool + CLI verb for bulk component registry export

**Feature**: export-component-registry
**Jira**: ENG-95543
**PRD**: [spec-export-component-registry](../prd/spec-export-component-registry.md)
**ADR**: [adr-export-component-registry](../adr/adr-export-component-registry.md)
**Size**: M

## Description

As the `creatio-ai-app-development-toolkit` migration engine (ENG-95543), I need a single
clio command that writes the FULL Freedom UI component registry for a resolved platform
version to a file, so I can validate every `crt.*` componentType and `propMap` key against
the target version — including in CI where no live stand exists — without hundreds of
per-component `get-component-info` calls.

## Acceptance criteria

1. New CLI verb `export-component-registry` (`ExportComponentRegistryCommand`, a plain class
   dispatched via `Resolve<ExportComponentRegistryCommand>().Execute(opts)` — mirrors
   `ComponentInfoCommand`/`ComponentRegistryRefreshCommand`, NOT `Command<TOptions>`) and a
   plain MCP tool class (`ExportComponentRegistryTool`, mirrors `ComponentInfoTool` — NOT
   `BaseTool<TOptions>`; see ADR D4 for why: eager per-environment container resolution would
   break the explicit-`version`-only and no-flags call shapes), registered in
   `BindingsModule.cs`, discoverable via `McpToolInvokerRegistry` reflection (no addition to
   `McpCoreToolProfile.CoreToolTypes` or `ToolContractGetTool`'s curated catalog — long-tail,
   per ADR D7).
2. MCP tool attributes: `ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false`.
   `Idempotent = false` because only the default-path shape repeats safely — an explicit `output-file`
   is refused once it exists, so a blind retry turns a completed export into an "already exists" failure.
3. Options (kebab-case): `--environment-name` (preferred) XOR `--version` (3-part semver,
   mutually exclusive — reject both/neither the way `ComponentInfoTool` does); `--schema-type`
   (`web` default | `mobile`); `--output-file` (optional); `--uri`/`--login`/`--password`
   fallback via `ConnectionArgsBase`.
4. Version resolution reuses `ComponentInfoResolution` verbatim: response carries
   `resolvedTargetVersion`, `resolvedFrom` (`environment` | `environment-superset` |
   `latest-fallback`), `resolvedFromReason`, `requiresVersionConfirmation` (`true` only on
   `latest-fallback`).
5. `schema-type=mobile` resolves through `IMobileComponentRegistryClient`; `web` (default)
   through `IComponentRegistryClient` — reusing the existing `ComponentInfoTool`
   web/mobile dispatch path, not a new switch.
6. File content is the raw registry bytes as fetched by the registry client — no
   re-serialization through `ComponentCatalogState`/`ComponentRegistryEntry`. A `deprecated`
   + `deprecationReason` pair present in the source is present, byte-identical, in the
   output file.
7. The tool never calls `IComponentRegistryDocsClient.GetDocAsync` (verified by a test
   asserting zero invocations on that mock across a full run).
8. Explicit `--output-file`: goes through `OutputPathConfinement.Resolve` +
   `OutputPathConfinement.WriteAtomic`. Rejected before any write for: `..`-traversal,
   absolute system path outside workspace/temp, symlink escape, and an already-existing
   target (`Destructive = false`).
9. Omitted `--output-file`: default path
   `<workspace-root>/.clio-migration/component-registry/<version>.json`
   (mirrors `GetClassicPageSourcesCommand.ResolveOutputPath`); a second run at the default
   path succeeds and overwrites — this is a DIFFERENT contract from AC-08 and must be
   tested separately.
10. Response DTO contains no registry content (`componentType` does not appear anywhere in
    the serialized response) — only: absolute output path, `resolvedTargetVersion`,
    `resolvedFrom`, `resolvedFromReason`, `requiresVersionConfirmation`, `versionWarning`,
    and counters (component count, composite count, total input count). Revised during
    implementation: the counters are computed off the SAME raw bytes written to disk
    (`ExportComponentRegistryCommand.CountEntries`), not via
    `IComponentInfoCatalog.LoadAsync`/`ComponentCatalogState` — a counter derived from the
    typed model could disagree with what the file actually contains, and a payload carrying
    no `components` array fails the export instead of reporting zero counters.
11. Errors routed through `SensitiveErrorTextRedactor` before returning to the caller.
12. Companion artifacts, all present in the same PR:
    - `clio/help/en/export-component-registry.txt`
    - `clio/docs/commands/export-component-registry.md`
    - `clio/Commands.md` (index entry)
    - `clio/Wiki/WikiAnchors.txt` (anchor mapping)
    - `clio.mcp.e2e` coverage for the new MCP tool
    - PR body: `MCP reviewed…` statement, docs-review statement, ClioRing compatibility
      line (expected: "ClioRing compatibility reviewed, no Ring-consumed contract changed"
      + inspected paths, since Ring does not consume this tool)
    - `./.codex/workspace-diary.md` entry after implementation

## Test plan (unit, `clio.tests`)

Command-level (`ExportComponentRegistryCommandTests`, `BaseCommandTests<TOptions>` pattern,
AAA + `[Description]` + FluentAssertions `.because`):
- Resolves version via `environment-name`; via explicit `version`; falls back to `latest`
  with `requiresVersionConfirmation = true` when neither is resolvable.
- Rejects mutually-exclusive `environment-name` + `version`.
- Explicit `output-file` outside allowed anchors is rejected before any write: `..`
  traversal, absolute system path, symlink escape (thin integration test — do not
  re-implement `OutputPathConfinementTests.cs`'s guard-level coverage).
- Explicit `output-file` pointing at an existing file is rejected, no write occurs.
- Default path (no `output-file`): first run creates the file; second run overwrites and
  succeeds (distinct test from the explicit-output-file existing-target rejection above).
- Written file byte-content matches the source registry exactly for a fixture containing a
  `deprecated`/`deprecationReason` pair (regression guard against re-serialization).
- `schema-type=mobile` sources from `IMobileComponentRegistryClient`; default/`web` from
  `IComponentRegistryClient`.
- Docs client mock (`IComponentRegistryDocsClient`) receives zero calls across a full run.
- Response JSON contains no `componentType` occurrence; counters match the fixture's known
  component/composite/input counts.

MCP tool-level (`ExportComponentRegistryToolTests`, direct-construction pattern like
`GetClassicPageSourcesToolTests`):
- Args (`environment-name`/`version`/`schema-type`/`output-file`/connection fallback) map
  correctly to `ExportComponentRegistryOptions`.
- Error messages are redacted via `SensitiveErrorTextRedactor` before being returned.

## Definition of Done

- All acceptance criteria above satisfied.
- `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=McpServer"`
  (and `Module=Command` if the command lives outside `McpServer/`) green.
- `clio.mcp.e2e` coverage added and green.
- Companion docs/help/Commands.md/WikiAnchors.txt updated.
- Diary entry appended.
- PR opened with Jira link line, MCP/docs/ClioRing review statements, assignee + reviewers set.
