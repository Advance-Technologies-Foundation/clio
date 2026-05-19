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
through a three-layer fallback chain implemented in
`Tools/ComponentRegistryClient.cs`:

1. **CDN.** `https://academy.creatio.com/api/mcp/{version}/ComponentRegistry.json`
   (override via `CLIO_COMPONENT_REGISTRY_CDN_BASE_URL` env var). The academy
   edge is a 5-minute mirror of the `static-files-mcp` GitLab repository
   (`gitdigital.creatio.com/academy/static-files-mcp`) where per-version files
   and the `latest/ComponentRegistry.json` alias are maintained by the
   producer-side CI (Jenkins job in `creatio-ui` is planned; files are added
   manually until then). Versions that have not been published yet return 404
   and the chain falls through to the cache, the `latest` alias, and finally
   the embedded snapshot — this is expected.
2. **File cache.** `~/.clio/cache/component-registry/{version}.json` with a
   5-minute TTL and a `{version}.meta.json` sidecar (ETag, Last-Modified, SHA-256).
   The TTL is aligned with the 5-minute academy mirror cadence so producer
   pushes reach AI within roughly 10 minutes worst-case. Cache hits return
   synchronously; stale entries return immediately while a single background
   refresh runs (stale-while-revalidate). AI requests never block on the network.
3. **Embedded snapshot in `clio.dll`.** Manifest resources
   `Clio.ComponentRegistry.ComponentRegistry.json` and
   `Clio.ComponentRegistry.embedded-metadata.json` are produced at clio build
   time by the `ResolveCdnSnapshot` MSBuild target — that target GETs the CDN
   `latest/ComponentRegistry.json` and on failure copies
   `Command/McpServer/Data/ComponentRegistry.seed.json` instead. So even an
   offline `dotnet build` succeeds and ships a valid catalog inside the assembly.

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
