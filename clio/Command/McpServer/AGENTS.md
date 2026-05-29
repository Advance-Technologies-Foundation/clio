# MCP tool pattern

This directory contains the MCP surface for `clio`: tools, prompts, and related resources.

## Skill to use

For MCP implementation work in this directory, explicitly use the `create-mcp-tool` skill.

## Base rule

Prefer deriving MCP tools from `clio\Command\McpServer\Tools\BaseTool.cs`.

Use one of these two execution paths:

- `InternalExecute(options)`
  Use this when the tool should execute the injected command instance directly.
  This is correct for commands that are not bound to per-call environment settings.

- `InternalExecute<TCommand>(options)`
  Use this when `options` inherit from `EnvironmentOptions` and the command depends on environment-bound services such as `IApplicationClient`, `EnvironmentSettings`, or `IServiceUrlBuilder`.
  This resolves a fresh command instance for the environment carried by the current MCP call and avoids reusing the stale startup-time command.

## Environment-sensitive commands

If a tool accepts any of these:

- environment name
- URI/login/password
- OAuth client credentials

assume the tool is environment-sensitive unless proven otherwise.

In that case, do not execute the injected command directly. Use `InternalExecute<TCommand>(options)` so the command is resolved for the current request.

## Commands with custom setup

Some commands require command-specific setup before execution, for example attaching progress handlers.

In those cases use:

- `InternalExecute<TCommand>(options, configureCommand: ...)`

Example use cases:

- subscribe to `StatusChanged`
- attach temporary callbacks
- tweak command instance state before `Execute`

## Uniformity rules

- New MCP tools should inherit from `BaseTool<TOptions>` unless there is a strong reason not to.
- Do not duplicate local `InternalExecute` implementations in tools when `BaseTool` can handle the flow.
- Keep tool methods focused on:
  - MCP argument mapping
  - selecting the correct execution path
  - optional command-specific setup

## Related artifacts

When adding or updating an MCP tool, also review:

- prompts in `clio\Command\McpServer\Prompts`
- any related MCP resources in `clio\Command\McpServer\Resources`
- unit tests in `clio.tests\Command\McpServer`
- end-to-end tests in `clio.mcp.e2e`
- tool descriptions and safety flags (`ReadOnly`, `Destructive`, `Idempotent`, `OpenWorld`)

## Test requirement

- Every new or changed MCP tool must ship with updated `clio.mcp.e2e` coverage.
- Do not stop at unit mapping tests; MCP implementation work is incomplete until the real `clio mcp-server` path is exercised end to end.
- If an existing E2E harness cannot support the tool yet, extend the harness as part of the same task instead of deferring E2E coverage.

## MCP tool budget policy

clio's MCP tool registry shares the same `tools/list` slot with every other MCP server an agent host has open, and host platforms enforce a fixed cap on the total tool count an agent can see. To keep clio inside that envelope:

- **128 hard limit.** Anthropic and the MCP protocol cap a single host's tool count at 128. clio must never approach this number; every tool we ship competes with other servers the user has installed.
- **24 budget ratchet.** [`clio.tests/Command/McpServer/McpToolBudgetTests.cs`](../../../clio.tests/Command/McpServer/McpToolBudgetTests.cs) asserts the live count against `ToolBudget`. After every consolidation block, the ratchet must move down — never up — without explicit ticket approval.
- **The current 24 = 23 read-only flat + 1 `clio-run` meta.** The 23 flat read-only tools (`list-environments`, `get-schema`, `apps`, `sys-setting`, `dataforge-find`, `dataforge-context`, `dataforge-get-relations`, `dataforge-get-table-columns`, `dataforge-status`, `find-empty-iis-port`, `get-component-info`, `get-fsm-mode`, `get-guidance`, `get-schema-name-prefix`, `get-tool-contract`, `list-packages`, `list-page-templates`, `list-pages`, `list-schemas`, `show-passing-infrastructure`, `validate-page`, `assert-infrastructure`, `check-settings-health`) are kept flat so hosts can auto-approve them via `ReadOnly = true`. Every non-read-only operation is reached through `clio-run` — its `args.command` is a discriminator over a `[JsonPolymorphic]` hierarchy ([`ClioRunArgs.cs`](Tools/ClioRunArgs.cs)) and the per-command record carries the operation's fields.
- **Extend before add — for non-read-only.** A new destructive or mutation operation must extend `clio-run` with a new `[JsonDerivedType]` entry on `ClioRunArgs`, a matching `*RunArgs : ClioRunArgs` record, and a switch arm in [`ClioRunTool.Apply`](Tools/ClioRunTool.cs). Do not add a new top-level `[McpServerTool]`. CS8509-as-error in [`clio.csproj`](../../clio.csproj) catches a switch arm that's missing when a new derived type is added.
- **Extend before add — for read-only.** Prefer extending an existing read-only tool with a `mode` / `action` / `schema-type` / `kind` discriminator argument before registering a new flat `[McpServerTool]`. Only register a new flat top-level tool if `ReadOnly = true` is correct and no existing read-only surface covers the resource. Document the new entry in the table above.
- **Deprecation = remove, no aliases.** Do not preserve historical MCP tool names as aliases on new tool methods or as new `[JsonDerivedType]` discriminators. When a tool moves to a new contract, the old MCP `[McpServerTool]` registration goes away in the same commit. CLI verbs are unaffected because they live on `[Verb]`-decorated Options classes, not on the MCP wrapper.
- **Inner tool classes survive as adapters.** Per-resource tool classes (e.g., `RestartTool`, `SchemaCreateTool`, `AppSectionTool`) keep their `[McpServerToolType]` decoration and their public method signatures so `ClioRunTool.Apply` can dispatch into them. Strip only the per-method `[McpServerTool]` attribute when consolidating. Leave a brief `internal const string ToolName = "..."` constant on each adapter class so `ToolContractGetTool` and prompt helpers continue to resolve. Some adapter classes (e.g., `ColumnModificationArgsBase`) carry the `: ClioRunArgs` inheritance on a base record because the derived record already uses `: Base(args)` primary-constructor syntax — derived records inherit transitively.

When you cannot avoid raising the budget, ask in the ticket whether the new entry point should instead become a new `[JsonDerivedType]` on `ClioRunArgs` (for write paths) or a discriminator on an existing read-only tool.


## Component registry data source (`get-component-info`)

The curated Freedom UI component catalog consumed by `get-component-info`
no longer lives as a flat JSON file in the repo. Runtime resolution happens
through a two-layer fallback chain implemented in
`Tools/ComponentRegistryClient.cs`:

1. **File cache.** `~/.clio/cache/component-registry/{version}.json` with a
   5-minute TTL and a `{version}.meta.json` sidecar (ETag, Last-Modified, SHA-256).
   The TTL is aligned with the 5-minute academy mirror cadence so producer
   pushes reach AI within roughly 10 minutes worst-case. Cache hits return
   synchronously; stale entries return immediately while a single background
   refresh runs (stale-while-revalidate). AI requests never block on the network.
2. **CDN.** `https://academy.creatio.com/api/mcp/{version}/ComponentRegistry.json`
   (override via `CLIO_COMPONENT_REGISTRY_CDN_BASE_URL` env var). The academy
   edge is a 5-minute mirror of the `static-files-mcp` GitLab repository
   (`gitdigital.creatio.com/academy/static-files-mcp`) where per-version files
   and the `latest/ComponentRegistry.json` alias are maintained by the
   producer-side CI (Jenkins job in `creatio-ui` is planned; files are added
   manually until then). Versions that have not been published yet return 404
   and the chain falls through to the `latest` alias.

Above the registry chain sits the developer-only override: when
`CLIO_COMPONENT_REGISTRY_LOCAL_FILE` points at a JSON file on disk, that
file is served directly and the cache/CDN tiers are skipped. The override
is fail-fast (missing file throws) and never writes to the cache.

When the chain runs out of tiers (the file cache is empty AND the CDN is
unreachable AND no local override is set), `GetAsync` throws
`ComponentRegistryUnavailableException`. `ComponentInfoTool`'s catch-all
turns the exception into a graceful MCP response with `success: false`
and an `error` field that points the operator at
`CLIO_COMPONENT_REGISTRY_LOCAL_FILE` for offline development. There is no
in-DLL snapshot — the previous `Command/McpServer/Data/ComponentRegistry.seed.json`
seed file and the `ResolveCdnSnapshot` MSBuild target were retired once the
academy CDN went live (see PR clio#599 history for the migration).

The platform version is resolved per request by
`Tools/PlatformVersionResolver.cs` via cliogate `GET /rest/CreatioApiGateway/GetSysInfo`,
mapped to a 3-part SemVer (`SysInfo.CoreVersion` → `Major.Minor.Patch`), with
a 5-minute in-process cache. Any failure class (HTTP error, missing CoreVersion,
non-SemVer string, cliogate < `2.0.0.32`, no active environment) degrades softly
to `latest`, and the MCP response carries the `resolvedFrom` marker
(`"environment"` | `"latest-fallback"`) so AI can interpret the result correctly
(see `Resources/PageModificationGuidanceResource.cs` for the guidance text).

To force-refresh the local cache without waiting for the 5min TTL, use the
`clio component-registry-refresh` verb:

- no flags → refresh `latest/ComponentRegistry.json`
- `--version 8.2.1` → refresh that GA file
- `--all` → refresh every per-version file currently in the cache directory

Exit code is 0 only when every requested refresh got a 2xx from the CDN.

### Long-form documentation (`references.docs[]`)

A component entry may carry a `references.docs[]` array — a list of paths
(e.g. `docs/data-grid.component.md`) that live alongside the registry under
the same `/api/mcp/{version}/` prefix on the academy edge. When a detail
request hits an entry with `references.docs[]`, clio lazily fetches each file
through a sibling pipeline implemented in
`Tools/ComponentRegistryDocsClient.cs`:

- **Cache.** `~/.clio/cache/component-registry/{version}/{docPath}` (plus a
  `.meta.json` sidecar). Same 5-minute TTL + stale-while-revalidate as the
  registry payload; `~/.clio/cache/component-registry/` delete resets the
  whole chain in one go.
- **CDN.** `https://academy.creatio.com/api/mcp/{version}/{docPath}` — three
  attempts with exponential backoff on 5xx / network errors, immediate
  fall-through on 4xx.
- **No embedded tier for docs.** If the cache misses and the CDN cannot
  serve the file, the docs client returns `null` and the MCP tool **skips
  that file** — partial-failure mode by design. The other docs of the same
  component are still concatenated, the `documentation` field is omitted
  entirely only when every file fails, and the rest of the detail response
  (componentType, properties, example, …) is unaffected.

The raw doc paths come from a writable GitLab repository, so
`Tools/ComponentRegistryDocsPath.cs` validates every value against
`^docs/[A-Za-z0-9._-]+(/[A-Za-z0-9._-]+)*\.md$` and a `Path.GetFullPath`
containment check before any HTTP or filesystem touch. Both checks run in
the docs client AND in the docs cache store as defence in depth — never
add a new call site that bypasses them.

Detail responses receive a `documentation` field that is the concatenation
of every successfully-fetched file in registry order, separated by
`\n\n---\n\n`. List responses and mobile responses never carry the field
(mobile has no CDN tier; list mode does not load docs).

### Detail-response shape (`inputs`/`outputs` vs legacy `properties`)

The producer ships two schema generations under the same registry URL,
and `Tools/ComponentInfoTool.cs` surfaces both verbatim:

- **Wrapped (current).** Each component entry carries `inputs` and
  `outputs` dictionaries. Values are forward-compatible JSON blobs whose
  inner schema is owned by the producer (`type`, `default`, `description`,
  `values`, `keyType`, `valueType`, `items`, `deprecated`, …) — clio
  stores them as `JsonElement` so a producer-side schema addition does
  NOT require a coordinated clio release. Wrapped entries may also carry
  a nested `content` block with `typeDefinitions` — see below.
- **Legacy.** Older entries carry a single `properties` dictionary with a
  fixed clio-side POCO (`ComponentPropertyDefinition`). This shape still
  appears in the mobile catalog and in older per-version files.

### Global content (`root.references.baseInputs` + `root.references.typeDefinitions`)

The wrapped registry payload also carries a top-level `content` block —
metadata shared across every component:

- `root.references.baseInputs` — input keys every component inherits
  (`classes`, `id`, `loading`, `name`, `shape`, `styles`, `tabIndex`,
  `type`). Producer-side, these live once at root rather than being
  duplicated on every entry.
- `root.references.typeDefinitions` — global named-type schemas referenced
  by per-component `inputs`/`outputs` `type` strings (e.g.
  `RequestBindingConfig`, `CrtMenuItemViewElementConfig`,
  `ViewElementConfig`, `LocalizableStringModel`).

`ComponentInfoTool.CreateDetailResponse` folds both into the per-component
surface before serialising, but with two different rules:

- `response.inputs` = `root.references.baseInputs` ∪ `entry.inputs` —
  every component unconditionally inherits the base inputs (per-component
  override wins on a key collision). This is a flat union; AI never needs
  to look at `baseInputs` separately.
- `response.references.typeDefinitions` = **transitive closure** of every
  type identifier tokenised from `entry.inputs`/`entry.outputs`/per-component
  typedefs, looked up first in `entry.references.typeDefinitions` then in
  `root.references.typeDefinitions`. The producer's global bag carries
  ~190 types but any one component references only a handful; the closure
  filter (see `TypeReferenceClosure.cs`) drops every global a component does
  not reach, so a `crt.Button` detail response carries ~5 typedefs instead
  of ~190. Identifiers that resolve to neither bag are built-ins (`string`,
  `Record`, `Promise`, …) and are silently skipped.

So an AI consumer reads a single flat per-component view, with every
referenced type definition inlined and nothing irrelevant. The closure
filter is exercised by `Live_Snapshot_Detail_Should_Resolve_Referenced_References_Into_Inputs_And_TypeDefinitions`
in `ComponentRegistrySnapshotTests` (real producer payload) and by
`TypeReferenceClosureTests` (hermetic depth/edge cases).

### Mobile flavor (`schema-type=mobile`)

The mobile component catalog goes through the **same** infrastructure as the web
catalog — same `IComponentRegistryClient` implementation, same wrapped envelope
deserialisation, same `[JsonExtensionData] UnmappedExtensions` snapshot guard,
same async pipeline, same `CreateDetailResponse`, same response shape
(`inputs`/`outputs`/`references.typeDefinitions`/`documentation`/
`resolvedTargetVersion`/`resolvedFrom`). The two flavors are isolated by a
`RegistryFlavor` config carried on the client at construction time:

| Flavor | CDN file | Cache subdirectory | Local-override env var | Bundled fallback |
|---|---|---|---|---|
| Web (default) | `{base}/{version}/ComponentRegistry.json` | `~/.clio/cache/component-registry/` | `CLIO_COMPONENT_REGISTRY_LOCAL_FILE` | none (exhaustion → `ComponentRegistryUnavailableException`) |
| Mobile | `{base}/{version}/MobileComponentRegistry.json` | `~/.clio/cache/component-registry/mobile/` | `CLIO_MOBILE_COMPONENT_REGISTRY_LOCAL_FILE` | `Command/McpServer/Data/MobileComponentRegistry.json` (transitional, while producer rolls out) |

The mobile fallback is a deliberate, narrowly-scoped concession: the academy
mirror does not yet serve `MobileComponentRegistry.json` (the producer-side
Jenkins job for the mobile feed is in progress), so the bundled in-repo file
keeps `get-component-info schema-type=mobile` working in the interim. Once the
producer publishes, the bundled file becomes dead weight — the
`Command/McpServer/Data/**` csproj content glob will quietly stop having a
file to ship, and the entire `BundledFileRelativePath` slot on
`RegistryFlavor.Mobile` can be set to null in a follow-up commit. No mobile
asymmetry survives in code paths beyond this one fallback tier.

DI registration sits in `BindingsModule.cs`:

```csharp
services.AddSingleton<IComponentRegistryClient, ComponentRegistryClient>();      // web (default)
services.AddSingleton<IMobileComponentRegistryClient>(sp => new MobileComponentRegistryClient(
    sp.GetRequiredService<IHttpClientFactory>(),
    ComponentRegistryCacheStore.WithSubdirectory(..., RegistryFlavor.Mobile.CacheSubdirectoryName),
    ...,
    sp.GetRequiredService<IWorkingDirectoriesProvider>()));
```

`IMobileComponentRegistryClient` is a marker interface that adds no methods over
`IComponentRegistryClient` — it exists so the DI container can distinguish the
two singleton registrations at injection time. The implementation
(`MobileComponentRegistryClient`) inherits verbatim from the web type.

### Snapshot guard against silent data loss

Every POCO on the registry deserialisation path carries an
`[JsonExtensionData] UnmappedExtensions` dictionary: `ComponentRegistryEnvelope`,
`RegistryGlobalReferences`, `ComponentRegistryEntry`, `ComponentReferences`. The
guard test `ComponentRegistrySnapshotTests.Live_Registry_Snapshot_Should_Have_No_Unmapped_Fields`
deserialises a pinned copy of
`https://academy.creatio.com/api/mcp/latest/ComponentRegistry.json`
(`clio.tests/Command/McpServer/Fixtures/ComponentRegistry.live-snapshot.json`)
and asserts each `UnmappedExtensions` bucket is empty.

If the producer ships a new field, this test fails — and the fix is to
EITHER map the field onto a POCO property AND surface it through
`CreateDetailResponse`, OR document an explicit reason it can stay on
the bucket (and add an allowlist check in the test). Either way, the
silent-drop pattern that motivated this section in the first place
(`references.typeDefinitions`, `root.references.baseInputs`,
`root.references.typeDefinitions` all went unnoticed for weeks) cannot
recur unchecked.

To refresh the snapshot after a deliberate producer-side change, from
the repo root:

```sh
curl -s "https://academy.creatio.com/api/mcp/latest/ComponentRegistry.json" \
  > clio.tests/Command/McpServer/Fixtures/ComponentRegistry.live-snapshot.json
dotnet test --filter "FullyQualifiedName~ComponentRegistrySnapshot"
```

### Named type schemas (`references.typeDefinitions`)

The `inputs`/`outputs` `type` strings on the wrapped shape can reference
producer-defined type names (e.g. `"string | ButtonIcon | ButtonAnimatedIcon"`
on `crt.Button.inputs.icon`, or `"DataGridColumnDefinition"` as the item
type for `crt.DataGrid.inputs.columns`). Resolving these requires the
producer's named-type schemas, which travel under the entry's
`references.typeDefinitions` block:

```jsonc
"content": {
  "typeDefinitions": {
    "ButtonIcon": { "type": "string", "values": ["close-icon", "edit-icon", ...] },
    "ButtonAnimatedIcon": {
      "fields": {
        "animationData": { "type": "() => Promise<any>" },
        "loop":          { "type": "boolean | number" }
      }
    }
  }
}
```

`ComponentInfoTool` mirrors this 1:1 under `response.references.typeDefinitions`
(not flattened to root — the nested shape matches the producer). Each value
stays a `JsonElement` so the producer can add `required`, `default`, `items`,
deeper `fields` nests, or wholly new schema shapes without a coordinated clio
release. AI consumers should treat the dictionary as the authoritative
schema source for any non-primitive `type` token referenced by `inputs` /
`outputs`. The flat `documentation` field is derived from `references.docs[]`
(fetched + concatenated); the raw `docs[]` paths are intentionally NOT
surfaced on the response — only their resolved markdown.

`CreateDetailResponse` populates `inputs`/`outputs` only when the
underlying entry actually has them, and `properties` only when populated —
the catch is that `inputs`+`outputs` and `properties` describe the same
component surface but in different schema generations, so both can be
absent (mobile entries without bindings) and both can be empty (the
legacy `properties: {}` block on a wrapped-shape entry). AI must look at
both fields, not just `properties`, when generating `viewConfigDiff`
inserts or matching output events to handler `request` strings — see the
canonical guidance in `Resources/PageModificationGuidanceResource.cs`.

List-mode search (`ComponentInfoGrouping.Matches`) inspects the wrapped
shape too: it walks every `inputs`/`outputs` key and the well-known
`type` / `description` / `values` properties inside each binding value.
Without this branch the search filter would be useless on the new payload
because the legacy `category`/`description`/`properties` fields are empty
in the wrapped shape.

When changing the catalog data source, refer to:

- `research/architecture.md` — target architecture (creatio-ui CI → `static-files-mcp` GitLab → academy 5-minute mirror → clio).
- `research/clio-target-structure.md` — consumer-side design.
- `research/jenkins-pipeline-spec.md` — producer-side contract for the
  creatio-ui team (URL pattern, JSON shape, GA-tag trigger, git push into
  `static-files-mcp`, `latest/ComponentRegistry.json` semver gate).

## Workspace-scoped tools

For tools that operate on a local workspace:

- require `workspace-path` when the tool may be called outside the current shell working directory
- validate ownership against the local workspace before mutating the remote environment
- mark destructive tools as destructive
