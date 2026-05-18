# Target structure in clio: CDN client + cache + embedded fallback

This document defines the clio-side target state. The architecture context is in [architecture.md](architecture.md); the CDN producer contract is in [jenkins-pipeline-spec.md](jenkins-pipeline-spec.md).

## Key finding: a platform-version probe is AVAILABLE

`/rest/CreatioApiGateway/GetSysInfo` returns `SysInfo.CoreVersion` (for example `8.1.5.xxxx`) — implemented in [CreatioApiGateway.cs:705](../cliogate/Files/cs/CreatioApiGateway.cs#L705). This is a cliogate-package endpoint (minimum version `2.0.0.32` — see [GetCreatioInfoCommand.cs:38](../clio/Command/GetCreatioInfoCommand.cs#L38)). That is, the version of the target environment can be automatically resolved by `environment-name` with a soft fallback when cliogate is not installed.

The version returned by `GetSysInfo` is mapped to a CDN file name (e.g. `8.1.5.xxxx` → request `8.1.5.json` first, fall back to `latest.json` if not present).

## Current structure (baseline before this work)

| Aspect | State |
|---|---|
| Storage | one file [ComponentRegistry.json](../clio/Command/McpServer/Data/ComponentRegistry.json) in the clio repo (92 records) |
| Loader | [ComponentInfoCatalog.cs](../clio/Command/McpServer/Tools/ComponentInfoCatalog.cs) — `Lazy<>`, in-memory, fail-on-duplicate, reads from the file system via `IFileSystem` |
| Model | `ComponentRegistryEntry` (`componentType`, `category`, `description`, `container`, `parentTypes`, `properties`, `typicalChildren`, `example`) — no version field |
| Property | `ComponentPropertyDefinition` (`type`, `description`, `required`, `default`, `values`) — no version field |
| MCP tool args | `component-type`, `search` — no `environment-name`, no `target-version` |
| Categories | hardcoded in two places |
| Version probe | exists (`GetSysInfo.CoreVersion`), but **not used in the catalog** |

## Target structure (v1)

| Aspect | State |
|---|---|
| Primary source | `https://academy.creatio.com/api/mcp/{version}/ComponentRegistry.json` (per-version file on academy CDN) |
| Latest alias | `https://academy.creatio.com/api/mcp/latest/ComponentRegistry.json` (CI-maintained pointer to the freshest GA) |
| Runtime cache | `~/.clio/cache/component-registry/{version}.json` + `{version}.meta.json` (Last-Modified, ETag, expiresAt, sourceUrl) |
| Embedded fallback | `Clio.ComponentRegistry.ComponentRegistry.json` as an embedded resource in `clio.dll`. Populated at `dotnet pack` time via MSBuild `ResolveCdnSnapshot` target. Committed seed-snapshot serves as the bootstrap source and as the fallback when the MSBuild fetch fails. |
| Loader | `Stream` from CDN/cache/embedded, parsed by the same `JsonSerializer.Deserialize<ComponentRegistryEntry[]>` |
| Model | **Unchanged** — `ComponentRegistryEntry` and `ComponentPropertyDefinition` keep current fields. No `availability`. Schema is drop-in compatible. |
| MCP tool Args | **Unchanged** — `component-type`, `search` |
| MCP tool Response | + `resolvedTargetVersion` + `resolvedFrom` (`"environment"` \| `"latest-fallback"`). `"explicit"` reserved for a future tool-surface bump |
| Version resolution | Internal stack: explicit > GetSysInfo > latest. v1 tool surface activates only the lower two rungs |
| Categories | Hardcoded `CategoryOrder` array — **unchanged** in v1. Data-driven categories deferred to a later schema bump |

## Component-level design

```
                  MCP get-component-info
                          │
                          ▼
                  ComponentInfoTool
                          │
            ┌─────────────┴───────────────┐
            ▼                             ▼
   ComponentInfoCatalog          IPlatformVersionResolver
            │                             │
            ▼                             ▼
   IComponentRegistryClient       cliogate /GetSysInfo
            │
   ┌────────┴────────┐
   ▼        ▼         ▼
 CDN     FileCache   EmbeddedSnapshot
(Http)  (~/.clio/   (Assembly.
         cache/...)   GetManifest
                      ResourceStream)
```

### Layers in detail

#### 1. `IPlatformVersionResolver` (new)

```csharp
public interface IPlatformVersionResolver
{
    Task<PlatformVersionResolution> ResolveAsync(
        string? environmentName,
        CancellationToken cancellationToken = default);
}

public sealed record PlatformVersionResolution(
    string ResolvedVersion,          // e.g. "8.1.5"
    VersionResolutionSource Source); // Environment | LatestFallback

public enum VersionResolutionSource { Environment, LatestFallback }
```

Implementation:
- If `environmentName` is null/empty → return `(latest, LatestFallback)`.
- Resolve `IApplicationClient` for `environmentName` from `IApplicationClientFactory`.
- Call `/rest/CreatioApiGateway/GetSysInfo`.
- Parse `SysInfo.CoreVersion` as `System.Version`. On success → `(coreVersion.ToString(3), Environment)` (drop the build/revision components — the CDN uses 3-part `Major.Minor.Patch` filenames matching GA tags; see [jenkins-pipeline-spec.md § Filenames](jenkins-pipeline-spec.md)).
- On any failure (cliogate < 2.0.0.32, HTTP error, parse failure, unknown shape) → `(latest, LatestFallback)`.
- **Caching:** results cached per `environmentName` with a 5-minute TTL in-process. This is short-TTL — `GetSysInfo` is cheap and environment URLs are pinned per session; the cache prevents probing for every `get-component-info` call within a workflow.

`v1` does NOT accept an explicit `target-version`. The internal data type carries `VersionResolutionSource.Explicit` for the future, but the resolver method never returns it because `ComponentInfoArgs` has no field to feed it.

#### 2. `IComponentRegistryClient` (new)

The orchestrator of the fallback chain. Single entry point for the catalog loader.

```csharp
public interface IComponentRegistryClient
{
    Task<ComponentRegistryFetchResult> GetAsync(
        string requestedVersion,
        CancellationToken cancellationToken = default);
}

public sealed record ComponentRegistryFetchResult(
    Stream Content,                       // disposable; holds raw JSON
    string ResolvedVersion,               // the version of the file actually loaded — may differ from requested if fallback to latest happened
    ComponentRegistrySource Source);      // Cdn | FileCache | Embedded

public enum ComponentRegistrySource { Cdn, FileCache, Embedded }
```

Algorithm (per call, with stale-while-revalidate):

```
1. Try cache(requestedVersion):
   - If fresh (within TTL):
       → return (cache, requestedVersion, FileCache)
   - If stale OR missing:
       a. Spawn fire-and-forget background task: tryRefreshFromCdn(requestedVersion)
       b. If stale exists:
            → return (stale, requestedVersion, FileCache)
       c. If no cache and CDN reachable (sync attempt with short timeout):
            → fetch CDN sync, cache, return (cdn, requestedVersion, Cdn)
       d. If CDN sync fails:
            → fall through to step 2

2. Try latest.json from cache or CDN (so AI gets *something* on cold-start with no network):
   - If cache(latest) fresh:
       → return (cacheLatest, latestVersion, FileCache)
   - If CDN reachable for latest:
       → fetch, cache, return (cdn, latestVersion, Cdn)
   - Else fall through

3. Return embedded snapshot:
       → (embedded, embeddedVersion, Embedded)
   embeddedVersion is taken from a sidecar metadata field baked at build time
   (see § MSBuild fetch target below).
```

**Stale-while-revalidate detail.** The background refresh task:
- Runs at most once per (version, 5-minute window) — guarded by an in-process semaphore.
- Writes to `~/.clio/cache/component-registry/{version}.json.tmp`, atomic-renames on success.
- Failures are logged at `Information` level, not surfaced to the caller.

**Resilience defaults**:
- HTTP connect timeout: 5 seconds.
- HTTP read timeout: 30 seconds.
- Retries: 3 attempts with exponential backoff (1s, 2s, 4s) on 5xx and network errors. No retry on 4xx.
- Total per-call budget (sync path): 60 seconds. Stale-while-revalidate background tasks ignore this budget.

#### 3. File cache

Location: `~/.clio/cache/component-registry/`.
- Per OS: `Environment.SpecialFolder.UserProfile` + `.clio/cache/component-registry/`.
- File: `{version}.json` — the raw JSON as fetched.
- Metadata sidecar: `{version}.meta.json`:

  ```json
  {
    "fetchedAt": "2026-05-13T10:00:00Z",
    "expiresAt": "2026-05-14T10:00:00Z",
    "sourceUrl": "https://academy.creatio.com/api/mcp/8.2.1/ComponentRegistry.json",
    "etag": "W/\"abc123\"",
    "lastModified": "Wed, 13 May 2026 09:00:00 GMT",
    "contentSha256": "..."
  }
  ```

- TTL: `expiresAt = fetchedAt + 24h`.
- Cache invalidation: by `expiresAt` only (no checksum check on read). Corrupted parse triggers cache deletion and falls through.
- Size: each file ~190 KB at current registry size; per-version caching means the directory could hold ~5 files long-term (~1 MB). Unbounded growth not a concern for foreseeable future; no eviction policy in v1.

#### 4. Embedded snapshot

Built into `clio.dll` as a manifest resource. Marker class:

```csharp
namespace Clio.ComponentRegistry;

internal static class EmbeddedRegistryMarker { }
```

Accessed as:

```csharp
typeof(EmbeddedRegistryMarker).Assembly
    .GetManifestResourceStream("Clio.ComponentRegistry.ComponentRegistry.json");
```

Sidecar metadata embedded the same way (`Clio.ComponentRegistry.embedded-metadata.json`) — holds `embeddedVersion` (the version that was the CDN `latest` at clio build time, or the seed-snapshot version for a build that failed CDN fetch).

#### 5. `ComponentInfoCatalog` rewrite

Existing public surface (`IComponentInfoCatalog.GetAll`, `Search`, `Find`) is preserved. Internally:
- Constructor takes `IComponentRegistryClient` + `IPlatformVersionResolver`.
- `Lazy<>` initialization is replaced by **per-call resolution** keyed by `(environmentName, requestedVersion)`. A short-lived in-memory cache (5 min) holds the parsed `Dictionary<string, ComponentRegistryEntry>` per `resolvedVersion`.
- Public parameterless ctor stays for DI; an `internal` ctor takes the client + resolver for tests.
- `LoadFromStream(Stream)` becomes a public static method on the type (no longer internal-only) — usable by tests AND by the CDN client orchestrator. Same fail-on-duplicate invariant, same `CategoryOrder` array.

#### 6. MCP `ComponentInfoTool` / `ComponentInfoArgs` / `ComponentInfoResponse`

`ComponentInfoArgs` — **no schema change in v1**:

```csharp
public sealed record ComponentInfoArgs(
    string? ComponentType = null,
    string? Search = null);
```

`ComponentInfoResponse` — **add two fields**:

```csharp
public sealed record ComponentInfoResponse(
    IReadOnlyList<ComponentRegistryEntry> Components,
    string ResolvedTargetVersion,        // NEW: e.g. "8.1.5" or "latest"
    string ResolvedFrom);                // NEW: "environment" | "latest-fallback"
```

`environmentName` is read from the active `EnvironmentSettings` (already in DI). The tool does not require AI to pass it in args — clio knows which environment is active for the current command session.

The MCP Response schema bump is **additive** — existing AI clients ignore the new fields. No coordinated change required.

#### 7. `clio component-registry refresh` (new CLI command)

Force-refresh of the cache:

```
clio component-registry refresh [--version <semver>] [--all]
```

- Without `--version` / `--all`: refresh `latest.json` only.
- `--version 8.2.1`: refresh that single file.
- `--all`: refresh all files currently present in the local cache.

Operationally useful when a user wants to pick up a newly published GA without waiting for the 24h TTL. Trivial wrapper over `IComponentRegistryClient.RefreshAsync(version)`.

## Resolution stack for `target-version` (internal)

Internal resolver supports three rungs. In v1, only the lower two are reachable from the MCP tool surface.

| Priority | Rung | Activated in v1? | `resolvedFrom` |
|---|---|---|---|
| 1 (highest) | Explicit `target-version` in Args | **No** (Args unchanged in v1) | `"explicit"` |
| 2 | `environmentName` from active env → `GetSysInfo` | Yes | `"environment"` |
| 3 (lowest) | Latest fallback (`latest.json` on CDN, or embedded `latest`) | Yes | `"latest-fallback"` |

When the resolver returns `"latest-fallback"` — the MCP Response carries the marker and AI must treat the catalog as a superset (more components/properties than the target environment may support).

## Fallback policy (v1)

Two orthogonal fallbacks:

**1. Version-selection fallback** — when `GetSysInfo` cannot determine a usable version:

| Trigger | Scenario | `resolvedFrom` |
|---|---|---|
| No active environment for the clio session | No environment configured | `latest-fallback` |
| cliogate < `2.0.0.32` on the env | Old stand without `GetSysInfo` | `latest-fallback` |
| `GetSysInfo` HTTP failure | Stand unreachable, auth invalid, timeout | `latest-fallback` |
| `GetSysInfo` returned non-parseable version | Custom build, dev stand with odd string | `latest-fallback` |

In all of the above the resolver returns `latest` and the client maps that to the `latest.json` CDN endpoint or the embedded `latest` marker.

**2. Transport-layer fallback** — when the data for a chosen version is unavailable:

| Tier | Trigger | Next action |
|---|---|---|
| CDN | HTTP 5xx, network error, timeout | Fall through to file cache (incl. stale) |
| CDN | HTTP 404 for the specific version | Try `latest.json` first; if also missing → fall through |
| CDN | Malformed JSON | Fall through to file cache; do not write the bad payload |
| File cache | File missing | Fall through to embedded |
| File cache | Parse failure | Delete bad file; fall through to embedded |
| Embedded | Resource missing | Fail hard at startup (broken clio build) |

Logging at each tier (`source=cdn`, `source=cache`, `source=embedded`, with version + latency).

## MSBuild fetch target (build-time embedded refresh)

Goal: at `dotnet pack` time of clio, fetch `latest/ComponentRegistry.json` from the CDN and embed it as `Clio.ComponentRegistry.ComponentRegistry.json` resource in `clio.dll`. On failure, fall back to a committed seed-snapshot in the repo.

Sketch:

```xml
<!-- in clio.csproj -->
<Target Name="ResolveCdnSnapshot" BeforeTargets="GenerateAssemblyInfo;CoreCompile;PrepareResources">
    <PropertyGroup>
        <CdnSnapshotUrl>https://academy.creatio.com/api/mcp/latest/ComponentRegistry.json</CdnSnapshotUrl>
        <CdnSnapshotPath>$(IntermediateOutputPath)ComponentRegistry.json</CdnSnapshotPath>
        <CdnSnapshotMetadataPath>$(IntermediateOutputPath)embedded-metadata.json</CdnSnapshotMetadataPath>
        <SeedSnapshotPath>$(MSBuildProjectDirectory)/Command/McpServer/Data/ComponentRegistry.seed.json</SeedSnapshotPath>
    </PropertyGroup>

    <DownloadFile SourceUrl="$(CdnSnapshotUrl)"
                  DestinationFolder="$(IntermediateOutputPath)"
                  DestinationFileName="ComponentRegistry.json"
                  Retries="3"
                  RetryDelayMilliseconds="1000"
                  ContinueOnError="WarnAndContinue">
        <Output TaskParameter="DownloadedFile" PropertyName="DownloadedSnapshot" />
    </DownloadFile>

    <!-- Fallback to seed when DownloadFile didn't produce the file -->
    <Copy SourceFiles="$(SeedSnapshotPath)"
          DestinationFiles="$(CdnSnapshotPath)"
          Condition="!Exists('$(CdnSnapshotPath)')" />

    <WriteLinesToFile File="$(CdnSnapshotMetadataPath)"
                      Lines='{ "embeddedVersion": "latest", "embeddedAt": "$([System.DateTime]::UtcNow.ToString(o))", "fallbackToSeed": $([System.String]::Equals(...)) }'
                      Overwrite="true" />
</Target>

<ItemGroup>
    <EmbeddedResource Include="$(IntermediateOutputPath)ComponentRegistry.json">
        <LogicalName>Clio.ComponentRegistry.ComponentRegistry.json</LogicalName>
    </EmbeddedResource>
    <EmbeddedResource Include="$(IntermediateOutputPath)embedded-metadata.json">
        <LogicalName>Clio.ComponentRegistry.embedded-metadata.json</LogicalName>
    </EmbeddedResource>
</ItemGroup>
```

Notes:
- `<DownloadFile>` is the built-in MSBuild task (since SDK 5.0). No external NuGet packages required.
- `ContinueOnError="WarnAndContinue"` lets the seed-fallback `<Copy>` run if the download fails.
- The seed-snapshot path `Command/McpServer/Data/ComponentRegistry.seed.json` is checked in. It is the same content as today's `ComponentRegistry.json` (renamed during the migration; the original is then deleted from the repo).
- `embeddedVersion` is set to `"latest"` because the CDN's `latest.json` does not encode its actual semver in the filename. A v1.1 improvement could embed the actual semver by reading a sidecar header — deferred.
- Concrete final pseudo-code above is intentionally schematic; the implementation plan ([implementation-plan-part1-cdn.md](implementation-plan-part1-cdn.md)) carries the exact `Condition=` expressions.

## What is preserved (zero-break migration)

- `Lazy<>` semantics in the catalog become per-version lazy caches inside `ComponentInfoCatalog`.
- Fail-on-duplicate ([ComponentInfoCatalog.cs:101](../clio/Command/McpServer/Tools/ComponentInfoCatalog.cs#L101)) remains.
- Existing callers that pass no environment context get `resolvedFrom: "latest-fallback"`.
- `ComponentRegistryEntry` and `ComponentPropertyDefinition` field shapes are unchanged.
- The current 92-record content is preserved as the seed-snapshot, and as the CDN's `latest.json` until the creatio-ui Jenkins job updates it.
- `CategoryOrder` array stays hardcoded.

## What changes

- `IFileSystem`-based path lookup in the loader is removed. The loader is wired through `IComponentRegistryClient`.
- `[Content Include="Command\McpServer\Data\**">` from clio.csproj is removed. The `Data` folder hosts only the seed-snapshot (`ComponentRegistry.seed.json`), which is processed by the MSBuild target — not packed as a clio content file.
- DI graph adds `IComponentRegistryClient`, `IPlatformVersionResolver`, an `HttpClient` (via `IHttpClientFactory`), `IFileSystem` for cache writes (already in DI).
- `ComponentInfoResponse` gets two new fields.
- `clio component-registry refresh` CLI command appears.

## What we no longer do

- We do not read the registry from the clio repo at runtime.
- We do not depend on a NuGet package for catalog content (the prior NuGet approach is dropped).
- We do not maintain a separate composer repo.
- We do not apply AI-side overrides (`aiHidden` / `aiOverlay`) in v1.
- We do not filter records by version inside a single file (the file IS the version).

## Mandatory follow-up for implementation

Per [clio/Command/McpServer/AGENTS.md](../clio/Command/McpServer/AGENTS.md):
- Changes to `get-component-info` require unit + E2E coverage in [clio.mcp.e2e](../clio.mcp.e2e).
- The CDN client — unit tests with `HttpMessageHandler` mock; cover all fallback transitions.
- The version resolver — separate unit suite with fake `IApplicationClient`.
- The cache layer — temp-directory tests for write/read/stale.
- Guidance resources (`PageModificationGuidanceResource.cs`) updated in the same PR to advise AI to interpret `resolvedFrom: "latest-fallback"` correctly.
- MSBuild target — integration check in CI that exercises the seed-fallback path (block the test network and confirm the build still succeeds with the seed embedded).

## Delivery stages (target architecture, this branch)

Single sequence — no intermediate pilot. Each step is its own commit set in this branch's PR.

1. **CDN client + cache + fallback chain.** Implement `IComponentRegistryClient`, the `~/.clio/cache/component-registry/` layout, the stale-while-revalidate path. Tested in isolation with `HttpMessageHandler` mock.
2. **Embedded snapshot via MSBuild.** Add `Command/McpServer/Data/ComponentRegistry.seed.json` (renamed copy of today's file). Wire the MSBuild target. Wire the `EmbeddedRegistryMarker`. Verify with `Assembly.GetManifestResourceNames()`.
3. **Version resolver.** Implement `IPlatformVersionResolver` over cliogate `GetSysInfo`. 5-min in-proc cache. Soft-fallback to `latest` on every error class.
4. **Catalog rewrite.** Wire `ComponentInfoCatalog` to the client + resolver. Per-`(env, version)` in-mem cache of parsed entries. Public `LoadFromStream` static. Drop `IFileSystem` constructor dependency.
5. **MCP contract extension.** Add `ResolvedTargetVersion` + `ResolvedFrom` to `ComponentInfoResponse`. Update `ComponentInfoTool` to populate them.
6. **Guidance update + tests.** `PageModificationGuidanceResource` mentions the new Response fields and the `latest-fallback` semantics. Test plan from § Mandatory follow-up.
7. **CLI command.** `clio component-registry refresh` thin wrapper over `RefreshAsync`.
8. **Remove legacy.** Delete the old in-repo `ComponentRegistry.json`; delete the `<Content Include="Command\McpServer\Data\**">` block (replaced by the MSBuild flow + seed file).

Stages 1–4 are loader internals; stages 5–6 are the AI-visible bump. The PR can land as a single squash, or be reviewed in 2–3 logical chunks.
