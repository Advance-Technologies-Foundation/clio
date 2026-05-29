# Implementation Plan — Part 1: CDN-driven component registry + clio loader migration

> **Status:** all open questions are closed (see [§ Decisions log](#decisions-log)). Ready for implementation. This supersedes [implementation-plan-part1-nuget.md] (now removed) from the abandoned NuGet-based PR [clio#595](https://github.com/Advance-Technologies-Foundation/clio/pull/595).

## 0. Target state of this part

- clio reads the Freedom UI component catalog from **academy.creatio.com CDN over HTTPS**, with a runtime file cache at `~/.clio/cache/component-registry/`. When both layers miss, the client throws `ComponentRegistryUnavailableException`; `ComponentInfoTool` returns a graceful MCP error response pointing operators at `CLIO_COMPONENT_REGISTRY_LOCAL_FILE`.
- There is **no in-DLL embedded snapshot and no build-time MSBuild fetch**. The `Command/McpServer/Data/ComponentRegistry.seed.json` seed file, the `ResolveCdnSnapshot` MSBuild target, and the `IEmbeddedRegistryReader` machinery described in §3.3/§3.4 of this plan were prototyped, then retired once the academy CDN went live. The text in those sections is retained as historical context — do not implement it.
- A new `IPlatformVersionResolver` probes `GetSysInfo` via cliogate to resolve the active environment's `CoreVersion`, then maps that to a CDN filename.
- The MCP tool `get-component-info` keeps its existing `ComponentInfoArgs`. The `ComponentInfoResponse` gains two new fields: `resolvedTargetVersion` and `resolvedFrom` (`"environment"` | `"latest-fallback"`).
- The legacy NuGet integration from PR #595 is **not** introduced. The composer-repo and the `Creatio.ComponentRegistry@0.1.0` package are slated for deletion (see § Legacy decommissioning).
- Behavior of `get-component-info` is **backward compatible**: existing AI consumers ignore the two new Response fields and see the same component shapes.

---

## 1. Steady-state architecture (for reference)

```
creatio-ui (Platform-UI team) ─── GA-tag `8.3.0` (parallel cross-repo PR; Jenkins job is planned)
    │
    └─ Jenkins extractor → git push to
        │                  gitdigital.creatio.com/academy/static-files-mcp
        │                  └─ 8.3.0/ComponentRegistry.json
        │                  └─ latest/ComponentRegistry.json (if 8.3.0 > current latest)
        ▼
static-files-mcp GitLab repo (academy team)
    │
    └─ academy mirror copies the repo to the public CDN tree every 5 minutes
        ▼
academy.creatio.com (public HTTPS CDN)
    │
    └─ /api/mcp/8.3.0/ComponentRegistry.json
        └─ /api/mcp/latest/ComponentRegistry.json
            └─ Cache-Control: public, max-age=300; ETag
                ▼ HTTPS GET (5min TTL, stale-while-revalidate)
clio runtime
    │
    └─ IComponentRegistryClient
         │   1. cache(version): if fresh → return; if stale → return stale + bg refresh
         │   2. CDN(version) → cache → return
         │   3. cache(latest) → return
         │   4. CDN(latest) → cache → return
         │   5. (exhausted) throw ComponentRegistryUnavailableException
         ▼
       ComponentInfoCatalog → MCP get-component-info
                              + ResolvedTargetVersion + ResolvedFrom in Response
```

Two CI pipelines total: creatio-ui Jenkins (git push to `static-files-mcp` on GA-tag — planned), clio CI (build + tests, including MSBuild fetch target). The academy mirror is academy-owned infrastructure, not a separate CI pipeline. No third party.

---

## 2. Files to change in clio

| File | Action |
|---|---|
| `clio/Command/McpServer/Data/ComponentRegistry.json` | **Removed.** The web registry now lives only on the academy CDN; the `Data` folder retains only the mobile catalog. (A `ComponentRegistry.seed.json` rename was prototyped here for an in-DLL fallback and then retired — see §3.3 below.) |
| `clio/clio.csproj` | Remove `<Content Include="Command\McpServer\Data\**">`. The `ResolveCdnSnapshot` MSBuild target + `<EmbeddedResource>` items that were prototyped here have been retired — the assembly carries no registry payload. |
| `clio/Command/McpServer/Tools/ComponentInfoCatalog.cs` | Rewrite: drop `IFileSystem` + `IWorkingDirectoriesProvider` deps; wire `IComponentRegistryClient` + `IPlatformVersionResolver`; per-`(env, version)` lazy cache; public `LoadFromStream(Stream)` static factory. Same fail-on-duplicate invariant. |
| `clio/Command/McpServer/Tools/ComponentInfoTool.cs` | Plug `IPlatformVersionResolver` + `environmentName` from active env. Populate `ResolvedTargetVersion` + `ResolvedFrom` on every Response. |
| `clio/Command/McpServer/Tools/ComponentRegistryClient.cs` | **New.** Orchestrator of the cache → CDN fallback chain. Throws `ComponentRegistryUnavailableException` when both tiers miss. |
| `clio/Command/McpServer/Tools/ComponentRegistryCacheStore.cs` | **New.** File-cache I/O for `~/.clio/cache/component-registry/`. |
| `clio/Command/McpServer/Tools/PlatformVersionResolver.cs` | **New.** `IPlatformVersionResolver` impl over cliogate `GetSysInfo`. |
| `clio/Command/McpServer/Resources/PageModificationGuidanceResource.cs` | Update wording: AI should read `resolvedTargetVersion` / `resolvedFrom`; explain `"latest-fallback"` semantics. |
| `clio/Command/ComponentRegistryRefreshCommand.cs` | **New.** `clio component-registry refresh [--version <semver>] [--all]` CLI. |
| `clio/Program.cs` (DI registration + verb routing) | Register `IComponentRegistryClient`, `IPlatformVersionResolver`, `IHttpClientFactory` (if not already there), the new command. Update `ComponentInfoCatalog` registration to use the new ctor. |
| `clio.tests/Command/McpServer/ComponentInfoToolTests.cs` | Hybrid: existing tests → use `LoadFromStream(MemoryStream)` for hermetic loading. Add per-fallback-tier tests. |
| `clio.tests/Command/McpServer/ComponentRegistryClientTests.cs` | **New.** `HttpMessageHandler` mocks covering each fallback transition. |
| `clio.tests/Command/McpServer/ComponentRegistryCacheStoreTests.cs` | **New.** Temp-dir cache layout, TTL, atomic rename. |
| `clio.tests/Command/McpServer/PlatformVersionResolverTests.cs` | **New.** Fake `IApplicationClient`; all failure-class branches. |
| `clio.mcp.e2e/ComponentInfoToolE2ETests.cs` | Verify the Response now includes `resolvedTargetVersion` + `resolvedFrom`. |
| `clio/Command/McpServer/AGENTS.md` | Document the new data source (CDN), the cache, and the fallback chain. |

---

## 3. Component-by-component

### 3.1 `IComponentRegistryClient` + fetch pipeline

```csharp
namespace Clio.Command.McpServer.Tools;

public interface IComponentRegistryClient {
    Task<ComponentRegistryFetchResult> GetAsync(string requestedVersion, CancellationToken ct = default);
    Task RefreshAsync(string version, CancellationToken ct = default);
}

public sealed record ComponentRegistryFetchResult(
    Stream Content,
    string ResolvedVersion,
    ComponentRegistrySource Source);

public enum ComponentRegistrySource { Cdn, FileCache, Embedded }

public sealed class ComponentRegistryClient(
    IHttpClientFactory httpClientFactory,
    IComponentRegistryCacheStore cacheStore,
    IEmbeddedRegistryReader embeddedReader,
    ILogger<ComponentRegistryClient> logger)
    : IComponentRegistryClient {

    private const string CdnBaseUrl = "https://academy.creatio.com/api/mcp/";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
    private static readonly SemaphoreSlim BackgroundRefreshGate = new(initialCount: 1);

    public async Task<ComponentRegistryFetchResult> GetAsync(string requestedVersion, CancellationToken ct = default) {
        var cached = await cacheStore.TryReadAsync(requestedVersion, ct);
        if (cached is { IsFresh: true }) {
            logger.LogInformation("registry source=cache version={Version} stale=false", requestedVersion);
            return new(cached.Content, requestedVersion, ComponentRegistrySource.FileCache);
        }

        if (cached is { IsFresh: false }) {
            _ = TryBackgroundRefreshAsync(requestedVersion);
            logger.LogInformation("registry source=cache version={Version} stale=true bgRefresh=scheduled", requestedVersion);
            return new(cached.Content, requestedVersion, ComponentRegistrySource.FileCache);
        }

        var fromCdn = await TryFetchFromCdnAsync(requestedVersion, ct);
        if (fromCdn is not null) {
            return new(fromCdn, requestedVersion, ComponentRegistrySource.Cdn);
        }

        // Fall back to latest
        var cachedLatest = await cacheStore.TryReadAsync("latest", ct);
        if (cachedLatest is not null) {
            logger.LogInformation("registry source=cache version=latest fallback-from={Requested}", requestedVersion);
            return new(cachedLatest.Content, "latest", ComponentRegistrySource.FileCache);
        }

        var fromCdnLatest = await TryFetchFromCdnAsync("latest", ct);
        if (fromCdnLatest is not null) {
            return new(fromCdnLatest, "latest", ComponentRegistrySource.Cdn);
        }

        logger.LogWarning("registry source=embedded fallback-from={Requested}", requestedVersion);
        return new(embeddedReader.OpenRegistryStream(), embeddedReader.EmbeddedVersion, ComponentRegistrySource.Embedded);
    }

    private async Task<Stream?> TryFetchFromCdnAsync(string version, CancellationToken ct) {
        var http = httpClientFactory.CreateClient("component-registry");
        http.Timeout = TimeSpan.FromSeconds(30);
        try {
            using var resp = await http.GetAsync($"{CdnBaseUrl}{version}/ComponentRegistry.json", HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode) {
                logger.LogInformation("registry cdn-get version={Version} status={Status}", version, (int)resp.StatusCode);
                return null;
            }
            var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
            await cacheStore.WriteAsync(version, bytes, resp.Headers.ETag?.Tag, resp.Content.Headers.LastModified, ct);
            return new MemoryStream(bytes, writable: false);
        } catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) {
            logger.LogInformation(ex, "registry cdn-get version={Version} failed", version);
            return null;
        }
    }

    private async Task TryBackgroundRefreshAsync(string version) {
        if (!await BackgroundRefreshGate.WaitAsync(0)) return;
        try {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            await TryFetchFromCdnAsync(version, cts.Token);
        } finally { BackgroundRefreshGate.Release(); }
    }

    public Task RefreshAsync(string version, CancellationToken ct = default)
        => TryFetchFromCdnAsync(version, ct).ContinueWith(_ => { }, ct);
}
```

HTTP retries are configured via `IHttpClientFactory`'s `HttpClientHandler` + Polly policy (3 attempts, exp backoff 1s/2s/4s, only on 5xx + network errors). Registered in `Program.cs`:

```csharp
services.AddHttpClient("component-registry")
    .AddTransientHttpErrorPolicy(p => p.WaitAndRetryAsync(3, n => TimeSpan.FromSeconds(Math.Pow(2, n - 1))));
```

### 3.2 File cache layer

```csharp
public interface IComponentRegistryCacheStore {
    Task<CacheReadResult?> TryReadAsync(string version, CancellationToken ct);
    Task WriteAsync(string version, byte[] payload, string? etag, DateTimeOffset? lastModified, CancellationToken ct);
}

public sealed record CacheReadResult(Stream Content, bool IsFresh);

public sealed class ComponentRegistryCacheStore(IFileSystem fileSystem, TimeProvider clock)
    : IComponentRegistryCacheStore {

    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);
    private readonly string _root = fileSystem.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".clio", "cache", "component-registry");

    public async Task<CacheReadResult?> TryReadAsync(string version, CancellationToken ct) {
        var jsonPath = fileSystem.Path.Combine(_root, $"{version}.json");
        var metaPath = fileSystem.Path.Combine(_root, $"{version}.meta.json");
        if (!fileSystem.File.Exists(jsonPath) || !fileSystem.File.Exists(metaPath)) return null;

        try {
            using var metaStream = fileSystem.File.OpenRead(metaPath);
            var meta = JsonSerializer.Deserialize<CacheMetadata>(metaStream)
                ?? throw new InvalidDataException();
            var bytes = await fileSystem.File.ReadAllBytesAsync(jsonPath, ct);
            var isFresh = meta.ExpiresAt > clock.GetUtcNow();
            return new(new MemoryStream(bytes, writable: false), isFresh);
        } catch {
            // bad cache → delete
            try { fileSystem.File.Delete(jsonPath); fileSystem.File.Delete(metaPath); } catch { }
            return null;
        }
    }

    public async Task WriteAsync(string version, byte[] payload, string? etag, DateTimeOffset? lastModified, CancellationToken ct) {
        fileSystem.Directory.CreateDirectory(_root);
        var tmp = fileSystem.Path.Combine(_root, $"{version}.json.tmp");
        var final = fileSystem.Path.Combine(_root, $"{version}.json");
        await fileSystem.File.WriteAllBytesAsync(tmp, payload, ct);
        fileSystem.File.Move(tmp, final, overwrite: true);

        var meta = new CacheMetadata(
            FetchedAt: clock.GetUtcNow(),
            ExpiresAt: clock.GetUtcNow() + Ttl,
            SourceUrl: $"https://academy.creatio.com/api/mcp/{version}/ComponentRegistry.json",
            Etag: etag,
            LastModified: lastModified,
            ContentSha256: Convert.ToHexString(SHA256.HashData(payload)));
        await fileSystem.File.WriteAllTextAsync(
            fileSystem.Path.Combine(_root, $"{version}.meta.json"),
            JsonSerializer.Serialize(meta),
            ct);
    }

    private sealed record CacheMetadata(
        DateTimeOffset FetchedAt,
        DateTimeOffset ExpiresAt,
        string SourceUrl,
        string? Etag,
        DateTimeOffset? LastModified,
        string ContentSha256);
}
```

### 3.3 Embedded resource + marker — **RETIRED**

> **Note:** the snippet below describes the prototype embedded-snapshot tier that was retired together with the seed file and the MSBuild target after the academy CDN went live. Kept for historical reference only — do not implement.


```csharp
namespace Clio.Command.McpServer.Tools;

internal static class EmbeddedRegistryMarker { }

public interface IEmbeddedRegistryReader {
    Stream OpenRegistryStream();
    string EmbeddedVersion { get; }
}

public sealed class EmbeddedRegistryReader : IEmbeddedRegistryReader {
    private const string RegistryResource = "Clio.ComponentRegistry.ComponentRegistry.json";
    private const string MetadataResource = "Clio.ComponentRegistry.embedded-metadata.json";
    private readonly Assembly _asm = typeof(EmbeddedRegistryMarker).Assembly;
    private readonly Lazy<string> _version;

    public EmbeddedRegistryReader() {
        _version = new(() => ReadEmbeddedVersion(), isThreadSafe: true);
    }

    public Stream OpenRegistryStream() =>
        _asm.GetManifestResourceStream(RegistryResource)
        ?? throw new InvalidOperationException(
            $"Embedded resource '{RegistryResource}' not found. Available: [{string.Join(", ", _asm.GetManifestResourceNames())}]");

    public string EmbeddedVersion => _version.Value;

    private string ReadEmbeddedVersion() {
        using var s = _asm.GetManifestResourceStream(MetadataResource);
        if (s is null) return "latest";
        var meta = JsonSerializer.Deserialize<JsonElement>(s);
        return meta.TryGetProperty("embeddedVersion", out var v) ? v.GetString() ?? "latest" : "latest";
    }
}
```

### 3.4 MSBuild `ResolveCdnSnapshot` target — **RETIRED**

> **Note:** the MSBuild target sketch below describes the prototype build-time CDN-fetch flow that was retired with the seed file. Kept for historical reference only — do not implement.


In `clio/clio.csproj`:

```xml
<PropertyGroup>
    <CdnSnapshotUrl Condition="'$(CdnSnapshotUrl)' == ''">https://academy.creatio.com/api/mcp/latest/ComponentRegistry.json</CdnSnapshotUrl>
    <CdnSnapshotStaging>$(IntermediateOutputPath)component-registry/</CdnSnapshotStaging>
    <CdnSnapshotPath>$(CdnSnapshotStaging)ComponentRegistry.json</CdnSnapshotPath>
    <CdnSnapshotMetadataPath>$(CdnSnapshotStaging)embedded-metadata.json</CdnSnapshotMetadataPath>
    <SeedSnapshotPath>$(MSBuildProjectDirectory)/Command/McpServer/Data/ComponentRegistry.seed.json</SeedSnapshotPath>
</PropertyGroup>

<Target Name="ResolveCdnSnapshot"
        BeforeTargets="PrepareResources;CoreCompile"
        Inputs="$(SeedSnapshotPath)"
        Outputs="$(CdnSnapshotPath);$(CdnSnapshotMetadataPath)">

    <MakeDir Directories="$(CdnSnapshotStaging)" />

    <DownloadFile SourceUrl="$(CdnSnapshotUrl)"
                  DestinationFolder="$(CdnSnapshotStaging)"
                  DestinationFileName="ComponentRegistry.json"
                  Retries="3"
                  RetryDelayMilliseconds="1000"
                  ContinueOnError="WarnAndContinue">
        <Output TaskParameter="DownloadedFile" PropertyName="_DownloadedFile" />
    </DownloadFile>

    <PropertyGroup>
        <_FallbackToSeed Condition="!Exists('$(CdnSnapshotPath)')">true</_FallbackToSeed>
        <_FallbackToSeed Condition="'$(_FallbackToSeed)' == ''">false</_FallbackToSeed>
    </PropertyGroup>

    <Copy SourceFiles="$(SeedSnapshotPath)"
          DestinationFiles="$(CdnSnapshotPath)"
          Condition="'$(_FallbackToSeed)' == 'true'" />

    <WriteLinesToFile File="$(CdnSnapshotMetadataPath)"
                      Overwrite="true"
                      Lines='{ "embeddedVersion": "latest", "embeddedAt": "$([System.DateTime]::UtcNow.ToString("o"))", "fallbackToSeed": $(_FallbackToSeed) }' />

    <Message Importance="High"
             Text="ResolveCdnSnapshot: fallbackToSeed=$(_FallbackToSeed) snapshot=$(CdnSnapshotPath)" />
</Target>

<ItemGroup>
    <EmbeddedResource Include="$(CdnSnapshotPath)">
        <LogicalName>Clio.ComponentRegistry.ComponentRegistry.json</LogicalName>
        <Visible>false</Visible>
    </EmbeddedResource>
    <EmbeddedResource Include="$(CdnSnapshotMetadataPath)">
        <LogicalName>Clio.ComponentRegistry.embedded-metadata.json</LogicalName>
        <Visible>false</Visible>
    </EmbeddedResource>
</ItemGroup>
```

The `BeforeTargets` runs the target early enough that the file exists when `EmbeddedResource` is collected. `Inputs`/`Outputs` are stale-marked so incremental builds skip the fetch when the seed is unchanged.

**Bootstrap behavior**: until the producer pipeline is fully built up, `latest/ComponentRegistry.json` may be missing from the CDN (or stale, with manual priming done from `static-files-mcp`) and `_FallbackToSeed` becomes `true`. Build succeeds with the seed file embedded. Once the academy mirror is serving fresh payloads, builds pick up the latest GA content automatically.

### 3.5 `IPlatformVersionResolver`

```csharp
public interface IPlatformVersionResolver {
    Task<PlatformVersionResolution> ResolveAsync(string? environmentName, CancellationToken ct = default);
}

public sealed record PlatformVersionResolution(string ResolvedVersion, VersionResolutionSource Source);
public enum VersionResolutionSource { Environment, LatestFallback }

public sealed class PlatformVersionResolver(
    IApplicationClientFactory clientFactory,
    TimeProvider clock,
    ILogger<PlatformVersionResolver> logger)
    : IPlatformVersionResolver {

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<string, (PlatformVersionResolution Result, DateTimeOffset ExpiresAt)> _cache = new();

    public async Task<PlatformVersionResolution> ResolveAsync(string? environmentName, CancellationToken ct = default) {
        if (string.IsNullOrEmpty(environmentName)) return new("latest", VersionResolutionSource.LatestFallback);
        if (_cache.TryGetValue(environmentName, out var cached) && cached.ExpiresAt > clock.GetUtcNow()) return cached.Result;

        var resolution = await TryProbeAsync(environmentName, ct);
        _cache[environmentName] = (resolution, clock.GetUtcNow() + CacheTtl);
        return resolution;
    }

    private async Task<PlatformVersionResolution> TryProbeAsync(string environmentName, CancellationToken ct) {
        try {
            var client = clientFactory.CreateClient(environmentName);
            var url = $"{client.GetEnvironmentSettings().Uri}/rest/CreatioApiGateway/GetSysInfo";
            var responseBody = client.ExecuteGetRequest(url);  // existing API in clio
            var parsed = JsonSerializer.Deserialize<JsonElement>(responseBody);
            var coreVersionStr = parsed.GetProperty("SysInfo").GetProperty("CoreVersion").GetString();
            if (string.IsNullOrEmpty(coreVersionStr)) return new("latest", VersionResolutionSource.LatestFallback);
            if (!Version.TryParse(coreVersionStr, out var v)) return new("latest", VersionResolutionSource.LatestFallback);

            var threePart = $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}";
            return new(threePart, VersionResolutionSource.Environment);
        } catch (Exception ex) {
            logger.LogInformation(ex, "version-resolver fallback env={Env}", environmentName);
            return new("latest", VersionResolutionSource.LatestFallback);
        }
    }
}
```

Uses existing clio infrastructure (`IApplicationClient`, `IApplicationClientFactory`) — same probe pattern as in [GetCreatioInfoCommand.cs](../clio/Command/GetCreatioInfoCommand.cs).

### 3.6 `ComponentInfoCatalog` rewrite (sketch)

```csharp
public sealed class ComponentInfoCatalog : IComponentInfoCatalog {
    private static readonly string[] CategoryOrder = ["containers", "fields", "interactive", "display"];
    private readonly IComponentRegistryClient _client;
    private readonly ConcurrentDictionary<string, ComponentCatalogState> _byVersion = new();

    public ComponentInfoCatalog(IComponentRegistryClient client) { _client = client; }

    public async Task<ComponentCatalogState> LoadAsync(string requestedVersion, CancellationToken ct = default) {
        if (_byVersion.TryGetValue(requestedVersion, out var cached)) return cached;
        var fetch = await _client.GetAsync(requestedVersion, ct);
        using (fetch.Content) {
            var state = LoadFromStream(fetch.Content, fetch.ResolvedVersion, fetch.Source);
            _byVersion[requestedVersion] = state;
            return state;
        }
    }

    public static ComponentCatalogState LoadFromStream(Stream stream, string resolvedVersion, ComponentRegistrySource source) {
        var raw = JsonSerializer.Deserialize<ComponentRegistryEntry[]>(stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (raw is null || raw.Length == 0) throw new InvalidOperationException("Component registry is empty or invalid.");

        var duplicates = raw
            .Where(e => !string.IsNullOrWhiteSpace(e.ComponentType))
            .GroupBy(e => e.ComponentType, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key).ToArray();
        if (duplicates.Length > 0)
            throw new InvalidOperationException(
                $"Component registry contains duplicate component types: {string.Join(", ", duplicates)}.");

        var ordered = raw
            .Where(e => !string.IsNullOrWhiteSpace(e.ComponentType))
            .OrderBy(e => GetCategorySortKey(e.Category))
            .ThenBy(e => e.ComponentType, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var lookup = ordered.ToDictionary(e => e.ComponentType, StringComparer.OrdinalIgnoreCase);
        return new ComponentCatalogState(ordered, lookup, resolvedVersion, source);
    }

    // Search / Find / Matches / etc — kept identical to current impl, just take the state argument
}

public sealed record ComponentCatalogState(
    IReadOnlyList<ComponentRegistryEntry> Entries,
    IReadOnlyDictionary<string, ComponentRegistryEntry> Lookup,
    string ResolvedVersion,
    ComponentRegistrySource Source);
```

The interface `IComponentInfoCatalog` is extended:

```csharp
public interface IComponentInfoCatalog {
    Task<ComponentCatalogState> LoadAsync(string requestedVersion, CancellationToken ct = default);
    // Existing GetAll / Search / Find become "latest" convenience wrappers:
    Task<IReadOnlyList<ComponentRegistryEntry>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ComponentRegistryEntry>> SearchAsync(string? search, CancellationToken ct = default);
    Task<ComponentRegistryEntry?> FindAsync(string componentType, CancellationToken ct = default);
}
```

The default-version convenience overloads internally call `LoadAsync("latest", ct)`.

### 3.7 `ComponentInfoTool` + Response extension

In `ComponentInfoTool.ExecuteAsync` (sketch):

```csharp
public async Task<ComponentInfoResponse> ExecuteAsync(ComponentInfoArgs args, CancellationToken ct = default) {
    var activeEnv = _environmentSettings.GetActiveEnvironmentName();
    var resolution = await _versionResolver.ResolveAsync(activeEnv, ct);
    var state = await _catalog.LoadAsync(resolution.ResolvedVersion, ct);

    // existing logic but operating on `state`
    var response = BuildResponse(state, args);
    return response with {
        ResolvedTargetVersion = state.ResolvedVersion,
        ResolvedFrom = resolution.Source switch {
            VersionResolutionSource.Environment => "environment",
            VersionResolutionSource.LatestFallback => "latest-fallback",
            _ => "latest-fallback"
        }
    };
}
```

Add to `ComponentInfoResponse`:

```csharp
[JsonPropertyName("resolvedTargetVersion")]
public string? ResolvedTargetVersion { get; init; }

[JsonPropertyName("resolvedFrom")]
public string? ResolvedFrom { get; init; }
```

Both are nullable on the class definition but always populated by the tool in the success path. Existing tests that build a `ComponentInfoResponse` directly continue to compile.

### 3.8 `PageModificationGuidanceResource` update

Add a paragraph:

> The response includes `resolvedTargetVersion` (the platform version the catalog was filtered for) and `resolvedFrom`:
> - `"environment"` — the target environment's `CoreVersion` was probed via cliogate.
> - `"latest-fallback"` — no version could be resolved; the catalog represents the most recent platform GA. AI should treat this as a superset and be prepared for the target environment to reject components/properties added after its own version.

### 3.9 `clio component-registry refresh` CLI

```csharp
[Verb("component-registry", HelpText = "Manage the local component-registry cache.")]
public sealed class ComponentRegistryRefreshOptions {
    [Option("version", Required = false, HelpText = "Refresh a specific version, e.g. 8.2.1.")]
    public string? Version { get; set; }

    [Option("all", Required = false, HelpText = "Refresh all versions currently in local cache.")]
    public bool All { get; set; }
}
```

Handler:
- Default: `RefreshAsync("latest")`.
- `--version X.Y.Z`: `RefreshAsync(version)`.
- `--all`: enumerate cache dir, `RefreshAsync` each.
- Always prints `source=cdn | source=cache | source=embedded` + duration per item.

---

## 4. Test strategy

### 4.1 Unit (clio.tests)

| Test | Verifies |
|---|---|
| `ComponentInfoCatalog_LoadFromStream_Returns_Correct_Count` | 92+ records loaded from a stream — hermetic |
| `ComponentInfoCatalog_LoadFromStream_FailsOnDuplicate` | Invariant preserved |
| `ComponentInfoCatalog_LoadFromStream_OrdersByCategoryAndName` | Sort stable |
| `ComponentRegistryClient_Returns_FromCache_When_Fresh` | TTL respected |
| `ComponentRegistryClient_Returns_Stale_And_Schedules_BgRefresh` | Stale-while-revalidate |
| `ComponentRegistryClient_Falls_To_LatestJson_When_Version_404` | 404 → latest |
| `ComponentRegistryClient_Falls_To_Cache_When_Cdn_5xx` | 5xx → cache |
| `ComponentRegistryClient_Falls_To_Embedded_When_All_Else_Fails` | Final tier |
| `ComponentRegistryClient_Does_Not_Cache_BadJson` | Invariant |
| `ComponentRegistryCacheStore_AtomicRename_On_Write` | Crash-safe writes |
| `ComponentRegistryCacheStore_Deletes_Corrupted_Meta` | Self-healing |
| `PlatformVersionResolver_Returns_Environment_For_Valid_CoreVersion` | Happy path |
| `PlatformVersionResolver_Falls_To_Latest_When_Cliogate_Old` | Soft fail |
| `PlatformVersionResolver_Falls_To_Latest_When_Http_Fails` | Soft fail |
| `PlatformVersionResolver_Falls_To_Latest_When_CoreVersion_Unparseable` | Soft fail |
| `PlatformVersionResolver_Caches_Result_5min` | TTL |
| `EmbeddedRegistryReader_Returns_Stream` | Manifest resource present |
| `EmbeddedRegistryReader_Throws_When_Missing` | Clear error message |

`HttpMessageHandler` mocks are used for all CDN tests (no real network).

### 4.2 E2E (clio.mcp.e2e)

`ComponentInfoToolE2ETests` runs against a real clio process started in a temp dir with no `~/.clio/cache/`. Asserts:
- First call → embedded snapshot (since CDN is presumably reachable in CI but cache is empty, behavior depends on test env config). Soft assertion: `resolvedFrom` is one of the valid values.
- `resolvedTargetVersion` is non-null.
- The Response is parseable as `ComponentInfoResponse` JSON.

### 4.3 Build integration test

A CI matrix entry that:
1. Blocks outbound network (`iptables` or `unshare`-style isolation).
2. Runs `dotnet build clio.csproj`.
3. Asserts the build succeeds.
4. Inspects `bin/Debug/...../clio.dll` with `Assembly.GetManifestResourceNames()` and asserts the embedded resource is present and parses as a valid registry.

This proves the seed-fallback path of the MSBuild target works.

### 4.4 Acceptance criteria

- [ ] `dotnet build` succeeds without any CDN download or `ResolveCdnSnapshot` log line.
- [ ] `dotnet test` passes 100% of new + adapted tests, including the new `GetAsync_Throws_When_Cache_Empty_And_Cdn_Down` exhaustion test.
- [ ] `clio component-registry refresh` runs end-to-end against a live CDN (manual smoke).
- [ ] `clio.mcp.e2e/ComponentInfoToolE2ETests` is green; Response includes `resolvedTargetVersion` + `resolvedFrom`.
- [ ] The `Command/McpServer/Data/` folder contains only the mobile registry (no `ComponentRegistry.json`, no `ComponentRegistry.seed.json`).
- [ ] The csproj `<Content Include="Command\McpServer\Data\**">` block is removed, together with the retired `ResolveCdnSnapshot` target and its `<EmbeddedResource>` items.
- [ ] `PageModificationGuidanceResource` mentions `resolvedFrom` semantics.
- [ ] `Program.cs` registers the new services.

---

## 5. Legacy decommissioning

External to this PR:

| Item | Action | Owner | Tracking |
|---|---|---|---|
| Branch `research/mcp-components-versioning` (PR #595) | Close without merge. Add a comment: "Superseded by `research/mcp-components-cdn` — architectural pivot to CDN model per architects' review." Link to the new PR. | clio team (us) | Issue / PR comment |
| Composer repo `Advance-Technologies-Foundation/creatio-component-registry-composer` | Delete (GitHub repo delete). README should first note the deletion intent for any external watchers. | clio team (us) — repo admin | Separate ops task |
| NuGet `Creatio.ComponentRegistry@0.1.0` on nuget.org | Unlist first (immediate effect, removes from search). Then file a support request with NuGet support to fully delete the package version. | clio team (us) | Separate ops task |

These are scheduled to happen **after** this PR merges and the new architecture is live; doing them before would leave a window where neither model has a working release.

---

## 6. Sequence of work

```
┌─── COMMIT 1: Research documents ────────────────────────────────────┐
│ - research/README.md                                                │
│ - research/architecture.md                                          │
│ - research/clio-target-structure.md                                 │
│ - research/jenkins-pipeline-spec.md                                 │
│ - research/extractor-analysis.md                                    │
│ - research/implementation-plan-part1-cdn.md (this file)             │
│ Open PR as DRAFT for architectural review.                          │
└──────────────────────────┬──────────────────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────────────────┐
│ COMMIT 2: Foundation — embedded snapshot + seed file                │
│ - Rename Data/ComponentRegistry.json → Data/ComponentRegistry.seed.json│
│ - Add EmbeddedRegistryMarker + EmbeddedRegistryReader              │
│ - Add ResolveCdnSnapshot MSBuild target to clio.csproj             │
│ - Remove <Content Include="Command\McpServer\Data\**">             │
│ - Verify Assembly.GetManifestResourceNames() finds both resources  │
│ - Test: offline build succeeds with seed-fallback                  │
└──────────────────────────┬──────────────────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────────────────┐
│ COMMIT 3: CDN client + file cache                                   │
│ - ComponentRegistryClient + IComponentRegistryClient               │
│ - ComponentRegistryCacheStore + IComponentRegistryCacheStore       │
│ - Polly retry policy in DI                                          │
│ - Unit tests for every fallback transition                          │
└──────────────────────────┬──────────────────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────────────────┐
│ COMMIT 4: Platform version resolver                                 │
│ - PlatformVersionResolver + IPlatformVersionResolver               │
│ - 5-min in-proc cache                                               │
│ - Unit tests for all failure classes                                │
└──────────────────────────┬──────────────────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────────────────┐
│ COMMIT 5: Catalog + MCP tool rewrite                                │
│ - ComponentInfoCatalog rewrite (IComponentRegistryClient ctor dep) │
│ - ComponentInfoTool reads active env, calls resolver, populates    │
│   ResolvedTargetVersion + ResolvedFrom on Response                  │
│ - ComponentInfoResponse: new fields                                 │
│ - PageModificationGuidanceResource: new paragraph                  │
│ - Update existing ComponentInfoToolTests to use LoadFromStream    │
│ - Update DI in Program.cs                                           │
└──────────────────────────┬──────────────────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────────────────┐
│ COMMIT 6: CLI command                                               │
│ - clio component-registry refresh [--version <semver>] [--all]    │
│ - Wire verb routing                                                 │
└──────────────────────────┬──────────────────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────────────────┐
│ COMMIT 7: AGENTS.md + final polish                                  │
│ - clio/Command/McpServer/AGENTS.md updated                          │
│ - clio.mcp.e2e adjustments                                          │
│ - PR moved out of Draft                                             │
└─────────────────────────────────────────────────────────────────────┘
```

Commits 2–7 are sequential because each builds on the previous. Commit 1 (research only) is independent and lands first to surface the architecture for review.

---

## 7. Gotchas

| # | Gotcha | Mitigation |
|---|---|---|
| G1 | `DownloadFile` MSBuild task may not exist in older .NET SDKs | clio is on net8/net10. The SDK includes it since 5.0. Safe. |
| G2 | `ContinueOnError="WarnAndContinue"` doesn't prevent build success when SourceUrl is unreachable | Verified: with `WarnAndContinue` MSBuild emits a warning and the target proceeds; `<Copy>` runs because of the `_FallbackToSeed` condition. |
| G3 | `Inputs/Outputs` on the target may skip the fetch when the seed didn't change but the CDN did | This is intentional — incremental builds prefer the previously-downloaded snapshot. Force a clean rebuild (`dotnet clean`) to re-fetch from CDN. Documented in AGENTS.md. |
| G4 | `MemoryStream` reused between threads | The client wraps fresh `MemoryStream(bytes)` on each call — safe. |
| G5 | DI lifetime mismatch (`ComponentInfoCatalog` singleton + per-request HTTP) | Use `IHttpClientFactory.CreateClient(...)` (transient per call, pooled handlers). |
| G6 | `IFileSystem` and `TimeProvider` available in the existing clio DI graph | `IFileSystem` already registered (System.IO.Abstractions). `TimeProvider` requires .NET 8+ — clio targets net8/net10. |
| G7 | First run on a fresh user profile has no cache and an empty CDN — embedded path is the only working one | Verified: that path returns the seed-fallback content. AI sees `resolvedFrom: "latest-fallback"`. |
| G8 | Cache directory `~/.clio/cache/` doesn't exist | `cacheStore.WriteAsync` creates it (`Directory.CreateDirectory`). |
| G9 | `Polly` is not yet a clio dependency | Add `Microsoft.Extensions.Http.Polly` PackageVersion to `Directory.Packages.props` if it isn't already pinned. |
| G10 | `IApplicationClient.ExecuteGetRequest` signature for `GetSysInfo` URL | Existing pattern in `GetCreatioInfoCommand.cs` — copy verbatim. |
| G11 | `latest.json` doesn't encode its actual semver in the filename | Embedded metadata records `"embeddedVersion": "latest"`. A v1.1 improvement could parse a sidecar header (`X-Registry-Version`) from the CDN response. Deferred. |
| G12 | E2E test environment without internet → CDN unreachable → falls to seed | Acceptable. Tests assert Response shape; `resolvedFrom` value is loose (must be one of the valid enum values). |

---

## 8. Out of scope (explicit reminders)

- Per-entry `Availability` ranges in `ComponentRegistryEntry`/`ComponentPropertyDefinition`. The file IS the version.
- Explicit `target-version` field in `ComponentInfoArgs`. (Internal resolver supports it.)
- Data-driven categories (top-level `categories` block). Hardcoded array stays.
- AI-side overrides (`aiHidden` / `aiOverlay`).
- Pre-release platform tags (`8.3.0-rc1`). Not published to CDN; clio falls back to `latest`.
- Integration of `latest.json` semver gate logic in clio. clio just GETs `latest.json` — semver promotion is the producer's job.
- A bot that auto-refreshes the seed-snapshot when CDN content changes. Manual or scheduled task in a future PR.
- Implementation of the creatio-ui Jenkins job (the git push side) and any tweaks the academy team may need on the 5-minute mirror. Cross-repo / cross-team tracks.
- composer-repo deletion + NuGet 0.1.0 unlist — § Legacy decommissioning.

---

## 9. Decisions log

All open questions are closed by the Q&A session that produced this branch. Summary:

| # | Question | Decision |
|---|---|---|
| 1 | Branch strategy | New branch `research/mcp-components-cdn` from `master`. PR #595 closed without merge. |
| 2 | Distribution channel | Public HTTPS CDN at `https://academy.creatio.com/api/mcp/{version}/ComponentRegistry.json`, mirrored every 5 minutes from the `static-files-mcp` GitLab repository. No auth on the CDN. |
| 3 | Versioning model | Per-version + `latest/ComponentRegistry.json` alias. SemVer-gated `latest/` promotion in the same git commit. |
| 4 | CDN access | Public, no authentication. GitLab write access restricted to the academy team and the Jenkins service account. |
| 5 | CI in creatio-ui | Jenkins (planned), trigger on GA-tag, git push to `static-files-mcp`. Branch-cut runs baseline mode (artifact only). |
| 6 | JSON schema | Drop-in compatible — top-level array of `ComponentRegistryEntry`. No per-entry `availability`, no wrappers. |
| 7 | AI-side overrides | Removed in v1. |
| 8 | Composer repo + NuGet 0.1.0 | Deleted (composer-repo via GitHub delete; NuGet via unlist + nuget.org support). |
| 9 | Fallback chain in clio | 3-layer: CDN → file cache (`~/.clio/cache/component-registry/`) → embedded snapshot in `clio.dll`. |
| 10 | Cache policy | TTL 5min, stale-while-revalidate (background refresh; AI never blocks on network). Aligned with the 5-minute academy mirror cadence so producer pushes reach AI within roughly 10 minutes worst-case. |
| 11 | Embedded snapshot refresh | Build-time fetch via MSBuild `ResolveCdnSnapshot`; seed-snapshot in repo for bootstrap + offline build. |
| 12 | Version resolver stack | Internal: `explicit > GetSysInfo > latest`. v1 tool surface activates `GetSysInfo > latest` only. |
| 13 | MCP tool surface | `ComponentInfoArgs` unchanged. `ComponentInfoResponse` adds `resolvedTargetVersion` + `resolvedFrom`. |
| 14 | Scope of this PR | Research docs + clio code + jenkins-pipeline-spec.md (contract for creatio-ui). Implementation of the Jenkins job — separate cross-repo PR. |

---

## Critical files for implementation

- [clio/Command/McpServer/Tools/ComponentInfoCatalog.cs](../clio/Command/McpServer/Tools/ComponentInfoCatalog.cs) — rewritten loader
- [clio/Command/McpServer/Tools/ComponentInfoTool.cs](../clio/Command/McpServer/Tools/ComponentInfoTool.cs) — Response shape + resolver wiring
- [clio/Command/McpServer/Data/ComponentRegistry.json](../clio/Command/McpServer/Data/ComponentRegistry.json) — renamed to `.seed.json`
- [clio/clio.csproj](../clio/clio.csproj) — MSBuild target + EmbeddedResource items
- [clio/Command/McpServer/Resources/PageModificationGuidanceResource.cs](../clio/Command/McpServer/Resources/PageModificationGuidanceResource.cs) — guidance update
- [clio.tests/Command/McpServer/ComponentInfoToolTests.cs](../clio.tests/Command/McpServer/ComponentInfoToolTests.cs) — hermetic + new tier tests
- New files under `clio/Command/McpServer/Tools/` — `ComponentRegistryClient.cs`, `ComponentRegistryCacheStore.cs`, `EmbeddedRegistryMarker.cs`, `EmbeddedRegistryReader.cs`, `PlatformVersionResolver.cs`
- New file `clio/Command/ComponentRegistryRefreshCommand.cs`
