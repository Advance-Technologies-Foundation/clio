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

## Read-response deadline (retry-safe tools)

Retry-safe tools (read-only, or the `get-page` local-write read; never idempotent server writes) are bounded by a wall-clock response
deadline so a stalled Creatio round-trip can never hang the call indefinitely (ENG-93373). This is
NOT wired per tool — it lives at the call-tool pipeline layer, so it is shape-agnostic and covers
every retry-safe tool regardless of its return type:

- `McpReadDeadlineGate.IsRetrySafe(toolName, readOnly, destructive)` is the single authority:
  `!destructive && !isProgressStreamingRead && (readOnly || isGetPage)`. `get-page` is admitted by NAME
  (ReadOnly=false because it writes local `.clio-pages` files, but it reads from Creatio and a retry
  re-reads + overwrites). `get-app-info` is EXCLUDED by name (`isProgressStreamingRead`): it is
  ReadOnly=true but streams `notifications/progress` under the write-path heartbeat and its contract is
  "await completion, do not retry" — so the read deadline must never bound it. The
  `Idempotent` hint is deliberately NOT in the predicate: an idempotent SERVER write (`install-gate`,
  `generate-source-code`, `add-package-dependency`, …) is safe only for sequential re-runs, not for a
  retry issued while an abandoned first call is still mutating the server — so the deadline covers reads
  only, never server writes.
- `McpReadResponseDeadline.RunAsync(...)` races the work against the deadline
  (default 120s, override `CLIO_MCP_READ_DEADLINE_SECONDS`; separate from the write path's
  `CLIO_MCP_RESPONSE_DEADLINE_SECONDS`). On expiry it returns a structured `CallToolResult` with
  `error-class: creatio-timeout` + `read-response-timed-out: true` + `retry-guidance`, and abandons
  the read (safe — the caller simply retries).
- Wiring: `McpToolErrorFilter.HandleCallToolErrors` applies it to MATCHED (advertised) retry-safe
  tools (annotations read from `MatchedPrimitive`); `McpDurableCallToolHandler` applies it to
  UNMATCHED long-tail retry-safe tools via `IMcpToolInvokerRegistry.IsRetrySafe`.

Do NOT add a per-tool `error-class`/`retry-guidance` field to a read response for timeout purposes —
the pipeline mechanism already covers it. Destructive tools own their own timeout contract (e.g.
`create-app-section`'s `section-created: in-progress`); never route a destructive tool through the
read deadline. See `spec/adr/adr-read-only-mcp-response-deadline.md`.

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
(`"environment"` | `"environment-superset"` | `"latest-fallback"`) so AI can interpret the result correctly
(see `Resources/PageModificationGuidanceResource.cs` for the guidance text).

The soft degrade keeps the tool from erroring, but it does NOT license the agent
to proceed blindly. On `latest-fallback` the server instructions
(`McpServerInstructions.cs`), the tool `[Description]`, the `versionWarning`
(`ComponentInfoResolution.LatestFallbackWarning`), and the page-modification guidance
all direct the agent to tell the user the platform version could not be determined
and request explicit confirmation before generating an implementation plan — it must
not silently assume a component set. The same surfaces direct the agent to list the
full catalog proactively (list mode, `component-type` omitted) at the start of page
work, so non-obvious components such as `crt.Gallery` are discovered without an
explicit user prompt.

To force-refresh the local cache without waiting for the 5min TTL, use the
`clio component-registry-refresh` verb:

- no flags → refresh `latest/ComponentRegistry.json`
- `--version 8.2.1` → refresh that GA file
- `--all` → refresh every per-version file currently in the cache directory

Exit code is 0 only when every requested refresh got a 2xx from the CDN, across all three
flavors (web, mobile, requests).

### Long-form documentation (`references.docs[]`)

A component entry may carry a `references.docs[]` array — a list of paths
(e.g. `docs/data-grid.component.md`) that live alongside the registry under
the same `/api/mcp/{version}/` prefix on the academy edge. When a detail
request hits an entry with `references.docs[]`, clio lazily fetches each file
through a sibling pipeline implemented in
`Tools/ComponentRegistryDocsClient.cs`:

- **Cache.** `~/.clio/cache/component-registry/{version}/{docPath}` (plus a
  `.meta.json` sidecar). Same 5-minute TTL as the registry payload;
  `~/.clio/cache/component-registry/` delete resets the whole chain in one go.
  **Unlike the registry payload, docs do NOT use stale-while-revalidate.** A
  fresh entry returns immediately; a *stale* entry is revalidated against the
  CDN **synchronously**, capped by `ComponentRegistryDocsClient.StaleRevalidateBudget`
  (5 s), and the stale bytes are served only as a fallback when the CDN cannot
  return a fresh copy in time (stale-if-error). This deliberately trades a few
  seconds of latency for documentation freshness — an outdated guide silently
  steers the agent into wrong page schemas (ENG-91135). The registry payload
  keeps stale-while-revalidate; only the docs tier changed.
- **CDN.** `https://academy.creatio.com/api/mcp/{version}/{docPath}` — three
  attempts with exponential backoff on 5xx / network errors, immediate
  fall-through on 4xx. On a stale-cache revalidation the whole retry sequence is
  bounded by the 5 s budget above; on a cold-cache miss it runs unbounded
  (there is no stale copy to fall back to).
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

### Selection-metadata (`whenToUse` / `whenNotToUse` / `synonyms` / `useCases` / `appliesToCustomEntities` / `entityCouplingNote`)

Solution A (ENG-91571) component-selection metadata, generated by the
producer from `@whenToUse` / `@whenNotToUse` / `@synonym` / `@useCase` /
`@appliesToCustomEntities` / `@entityCouplingNote` JSDoc tags on the
`creatio-ui` component class. `ComponentRegistryEntry` maps all six and
`CreateDetailResponse` surfaces them on the detail response (each omitted when
the producer published none). The POSITIVE signals — `synonyms` / `useCases` /
`whenToUse` — are additionally folded into `ComponentInfoGrouping.Matches`, so a
list-mode keyword search matches an informal term (e.g. `table` →
`crt.DataGrid`). `whenNotToUse` is deliberately NOT searched: it is
anti-guidance, and matching it would surface the very component the metadata
steers away from (searching `image` must not return `crt.DataGrid` just because
its `whenNotToUse` names image collections). These are the fields the agent uses to pick between visually
similar components (`crt.Gallery` vs `crt.DataGrid` vs `crt.List`) — the core
ENG-91134 lever. Today ~8 components carry them; everything else deserialises
to null/empty. Because all six are mapped, they round-trip through the
`[JsonExtensionData]` snapshot guard rather than landing on the unmapped
bucket — if the producer renames a tag, the guard test fails (see below).

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
| Requests (`get-request-info`) | `{base}/{version}/RequestRegistry.json` | `~/.clio/cache/component-registry/requests/` | `CLIO_REQUEST_REGISTRY_LOCAL_FILE` | none (exhaustion → `ComponentRegistryUnavailableException` naming the requests env var) |

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

### Request registry data source (`get-request-info`)

The Freedom UI request catalog (`crt.*Request` types wired through a view element's
request bindings, e.g. a button's `clicked`; OOTB button-action requests initiative,
ENG-93187) is the third `RegistryFlavor`
(`RegistryFlavor.Requests`, see the flavor table above) served by the same
transport chain: local-override env var → file cache → CDN → `latest` fallback →
`ComponentRegistryUnavailableException`. Only the byte transport is shared —
the envelope differs from components, so parsing lives in its own
`Tools/RequestInfoCatalog.cs` (`{ "requests": [...], "references": { "baseParameters",
"typeDefinitions" } }`; there is no legacy top-level-array generation and the
`requests` array is mandatory).

Consumer rules that differ from the component catalog — do not "unify" them away:

- **`baseParameters` are NOT merged into `parameters`.** The component catalog
  merges `baseInputs` into `inputs` because base inputs are authorable. The request
  catalog's base fields (`$context`, `scopes`, `type`) are platform-injected at
  dispatch time; merging them would teach an AI consumer to pass them through the
  binding's `params` block. `RequestInfoTool.CreateDetailResponse` surfaces them as
  a SEPARATE `baseParameters` response field instead.
- **An empty `parameters` map is meaningful and stays on the wire.** It says "this
  request accepts NO parameters" (e.g. `crt.ClosePageRequest`); absence of the field
  would read as "unknown".
- **The detail response always seeds the type-definition closure with
  `RequestBindingConfig`** — the wiring contract of every request — so a
  parameterless request still returns a self-contained wiring schema.
- **Request docs live under the `request-docs/` namespace**
  (`request-docs/<basename>.request.md`, flat URL next to the registry). The shared
  `Tools/ComponentRegistryDocsPath.cs` validator accepts exactly the `docs/` and
  `request-docs/` prefixes; the docs pipeline (`ComponentRegistryDocsClient` +
  `ComponentDocumentationLoader`) is reused verbatim.
- **The surface ships enabled on every install.** `RequestInfoTool` (`get-request-info`) is a
  resident core tool in `McpCoreToolProfile.CoreToolTypes`; the `ListPrintablesTool` probe is
  non-resident and dispatched through `clio-run`; the `WhenToUseRequestsGuidanceResource` guide is a
  plain `GuidanceCatalog` entry, and the routing map plus the three always-on page guides
  (`PageModificationGuidanceResource` with its run-process GATE row, `MobilePageGuidanceResource`
  with its request-catalog pointer, `PageSchemaHandlersGuidanceResource` with its "Standard handler
  parameter catalog" pointers) reference the request surface as static article content.
  `ToolContractGetTool` carries the curated `BuildRequestInfo` contract that names `get-request-info`
  as the authoritative contract. The `when-to-use-requests` guide owns the request-selection decision
  rules and the catalog discipline; handler mechanics stay in `page-schema-handlers` (never duplicate).
  There is deliberately NO CLI twin verb; `component-registry-refresh` covers the requests cache
  flavor alongside web and mobile. Offline iteration goes through `CLIO_REQUEST_REGISTRY_LOCAL_FILE`.
- **Snapshot guard is symmetric**: `RequestRegistrySnapshotTests` pins
  `clio.tests/Command/McpServer/Fixtures/RequestRegistry.live-snapshot.json`
  (the live academy CDN payload — the producer now publishes it; refresh whenever a new
  one ships via `curl -s https://academy.creatio.com/api/mcp/latest/RequestRegistry.json > <that fixture>`)
  and fails on any non-empty `UnmappedExtensions` bucket.
- **Environment-dependent parameter values come from PROBE tools, never from the
  catalog.** A registry parameter whose value lives in the target environment carries a
  `valueSource` annotation (`{ "kind": "environment", "tool": "<probe>" }`) inside its
  parameter blob — e.g. `crt.PrintablesRequest.templateId` -> `list-printables`,
  `crt.RunBusinessProcessRequest.processName` -> `get-process-signature`. Probes are
  dedicated read-only, per-call environment-scoped tools (the `get-process-signature`
  pattern): one probe per RESOURCE CLASS (process signatures, printables, …), never one
  per request and never a generic env-reader. New probes derive their response from
  `Tools/EnvironmentProbeResponse.cs` (`success` / `resolutionFailed` hard-vs-transient
  lever / `error` with candidates) and are deliberately NOT resident in `tools/list` —
  the per-request docs and the `when-to-use-requests` guide route agents to them. The
  agent-facing hard rule (carried by the guide and every probe description): fill such
  values ONLY from the probe result; never invent them; on empty/ambiguous results ask
  the user. NOTE the two probes differ on provenance, NOT on what they read (both are read-only
  built-in-DataService reads): `list-printables` was born with ENG-93187 as an MCP-only probe (no
  registered CLI verb, no `help`/`docs`, no purpose outside crt.PrintablesRequest wiring), while
  `get-process-signature` is a pre-existing GA command the request catalog merely REUSES: a
  registered, documented standalone CLI verb (`Program.cs` verb table + dispatch, aliases `gps`,
  its own `help`/`docs`).

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
