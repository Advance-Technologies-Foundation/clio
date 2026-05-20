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

### Long-form documentation (`content.docs[]`)

A component entry may carry a `content.docs[]` array — a list of paths
(e.g. `docs/data-grid.component.md`) that live alongside the registry under
the same `/api/mcp/{version}/` prefix on the academy edge. When a detail
request hits an entry with `content.docs[]`, clio lazily fetches each file
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

### Named type schemas (`content.typeDefinitions`)

The `inputs`/`outputs` `type` strings on the wrapped shape can reference
producer-defined type names (e.g. `"string | ButtonIcon | ButtonAnimatedIcon"`
on `crt.Button.inputs.icon`, or `"DataGridColumnDefinition"` as the item
type for `crt.DataGrid.inputs.columns`). Resolving these requires the
producer's named-type schemas, which travel under the entry's
`content.typeDefinitions` block:

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

`ComponentInfoTool` mirrors this 1:1 under `response.content.typeDefinitions`
(not flattened to root — the nested shape matches the producer). Each value
stays a `JsonElement` so the producer can add `required`, `default`, `items`,
deeper `fields` nests, or wholly new schema shapes without a coordinated clio
release. AI consumers should treat the dictionary as the authoritative
schema source for any non-primitive `type` token referenced by `inputs` /
`outputs`. The flat `documentation` field is derived from `content.docs[]`
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
