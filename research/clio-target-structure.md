# Target structure for supporting multiple platform versions

As Creatio evolves, the set of components, properties, and behaviors changes. The AI must receive a catalog filtered by the platform version it is working against.

## Key finding: a platform-version probe is AVAILABLE

`/rest/CreatioApiGateway/GetSysInfo` returns `SysInfo.CoreVersion` (for example `8.1.5.xxxx`) — implemented in [CreatioApiGateway.cs:705](../cliogate/Files/cs/CreatioApiGateway.cs#L705). This is a cliogate-package endpoint (minimum version `2.0.0.32` — see [GetCreatioInfoCommand.cs:38](../clio/Command/GetCreatioInfoCommand.cs#L38)). That is, the version can be automatically resolved by `environment-name` with a soft fallback when cliogate is not installed.

**Conclusion:** `target-version` becomes **optional**, not a required parameter.

## Current structure (baseline)

| Aspect | State |
|---|---|
| Storage | one file [ComponentRegistry.json](../clio/Command/McpServer/Data/ComponentRegistry.json) in the clio repo (92 records) |
| Loader | [ComponentInfoCatalog.cs](../clio/Command/McpServer/Tools/ComponentInfoCatalog.cs) — `Lazy<>`, in-memory, fail-on-duplicate, reads from the file system |
| Model | `ComponentRegistryEntry` (`componentType`, `category`, `description`, `container`, `parentTypes`, `properties`, `typicalChildren`, `example`) — **no version field** |
| Property | `ComponentPropertyDefinition` (`type`, `description`, `required`, `default`, `values`) — **no version field** |
| MCP tool args | `component-type`, `search` — no `environment-name`, no `target-version` |
| Categories | hardcoded in two places |
| Version probe | exists (`GetSysInfo.CoreVersion`), but **not used in the catalog** |

## Target structure

| Aspect | State |
|---|---|
| Storage | NuGet package `Creatio.ComponentRegistry` with embedded JSON, **not in the clio repo** |
| Loader | `Assembly.GetManifestResourceStream` — reads from the NuGet-bundled resource |
| Model | `ComponentRegistryEntry.Availability` + per-property `Availability` |
| Top-level | `latestKnownVersion`, `categories` (data-driven), `$schema`, `schemaVersion` |
| MCP tool args | + `environment-name`, + `target-version` |
| Versioning | Independent NuGet semver (MAJOR/MINOR/PATCH by semantic rules — see [architecture.md](architecture.md#versioning-creatiocomponentregistry-nuget)) |
| Distribution | NPM → composer-repo → NuGet (see [architecture.md](architecture.md)) |

## What the target structure must support

1. **Component-level versioning** — a new component in v8.2 is invisible under v8.1.
2. **Property-level versioning** — a new property on an existing component appears in a specific version.
3. **Deprecation** — a property/component removed in a specific version.
4. **Backwards-compatibility** — records without version metadata = "always applicable", zero-break migration.
5. **Visibility in the response** — the AI must see which version the filtering was done against (otherwise debugging is impossible).
6. **Categories — data-driven** (new categories arrive with the platform).

## Target record shape

Per-entry availability metadata in a single file — the best trade-off for the scale of ~100 × ~5–10 versions: a single source of truth, diff-friendly in Git, cheap filter-on-read, zero duplication.

```json
{
  "componentType": "crt.Button",
  "category": "interactive",
  "availability": { "since": "8.0.0" },
  "description": "Clickable action element.",
  "properties": {
    "type":     { "type": "string", "description": "Must be crt.Button." },
    "caption":  { "type": "string", "description": "Button caption." },
    "iconPosition": {
      "type": "string",
      "values": ["left", "right"],
      "availability": { "since": "8.1.0" },
      "description": "Icon placement (added in 8.1)."
    },
    "legacyStyleMode": {
      "type": "string",
      "availability": { "since": "8.0.0", "until": "8.2.0" },
      "description": "Pre-8.2 style override. Removed in 8.2; use crt.ButtonToggleGroup."
    }
  }
}
```

Semantics of `availability`:
- **absent** → always applicable (back-compat — we do not rewrite the existing 92 records)
- **`since`** — inclusive (from this version onward)
- **`until`** — exclusive (up to this version, not including)
- a record is filtered out when `target < since` OR `target >= until`

## Resolution stack for `target-version`

Priority (from high to low):

1. **Explicit `target-version`** in the tool args — used as is, overrides everything.
2. **`environment-name`** → probe `GetSysInfo` → cache per environment. In the response `resolvedFrom: "environment"`.
3. **Any fallback** (cliogate unavailable, probe failed, env not set, version string does not parse) → **`latest known`** with marker `resolvedFrom: "latest-fallback"`.

Always return in the response:

```json
{
  "resolvedTargetVersion": "8.3.2",
  "resolvedFrom": "latest-fallback"
}
```

Permitted values of `resolvedFrom`: `"explicit"` | `"environment"` | `"latest-fallback"`.

This closes diagnostics: the AI sees why a component is absent (filtered by version vs. truly does not exist).

## Fallback policy (locked in)

**Rule:** when the version is not determined from an explicit parameter and is not resolved from the environment, the catalog treats it as **last known (`latest known`)**. No other modes — `unrestricted` (return everything including deprecated), `error` (refuse to execute the request), or "oldest" — are NOT implemented.

**What "last known" means.** It is **the maximum `since` among all records** of the current `ComponentRegistry.json`, computed by the composer phase in clio at build time. It is stored in the catalog manifest as a top-level field:

```json
{
  "latestKnownVersion": "8.3.2",
  "components": [ … ],
  "categories": [ … ]
}
```

`latestKnownVersion` is updated automatically on every composer run. Hardcoded constants in C# code are forbidden (otherwise on adding a version in `creatio-ui` someone will forget to update the constant).

**When the fallback triggers (exhaustive list):**

| Trigger | Scenario | `resolvedFrom` |
|---|---|---|
| No `environment-name` and no `target-version` | `get-component-info` is called "dry" (a new workspace, an exploratory request) | `latest-fallback` |
| `environment-name` is set, but cliogate < `2.0.0.32` | An old stand without the `GetSysInfo` endpoint | `latest-fallback` |
| `environment-name` is set, but `GetSysInfo` returned an HTTP error | The stand is unavailable, auth did not pass, timeout | `latest-fallback` |
| `GetSysInfo` returned a `CoreVersion` that does not parse as semver | A custom build, a dev stand with a non-standard string | `latest-fallback` |

**What is NOT a fallback (important):**

- If `target-version` is passed as an explicit parameter — it is used **as is**, even when the version is older than `latestKnownVersion - 2` or newer than `latestKnownVersion`. The AI/user is responsible for correctness. `resolvedFrom = "explicit"`.
- If `target-version` is explicitly higher than `latestKnownVersion` — the response still goes out, but with an additional field `warning: "target-version <X> exceeds latestKnownVersion <Y>; catalog may be incomplete for this version"`. Not an error — an informational sign.

**How the AI should react to `latest-fallback`:**

Guidance ([PageModificationGuidanceResource.cs](../clio/Command/McpServer/Resources/PageModificationGuidanceResource.cs)) must explain:
- `resolvedFrom: "latest-fallback"` means that the catalog shows **everything the platform knows in the most recent version**, including components/properties that may be absent on the target stand.
- If a subsequent operation (`update-page`) failed on a non-existent property — this may be a legitimate signal that `environment-name` is worth passing in the next call to `get-component-info` for correct filtering.
- Do not suggest that the user remember `target-version` as a substitute for `environment-name` — that delegates the responsibility from the OS to the AI.

**Why `latest known`, not the alternatives:**

- **vs `error`**: breaks UX for exploratory queries ("show the list of available components" without any env). This is a common first-contact flow with MCP.
- **vs `unrestricted` (return everything, including deprecated)**: gives the AI components that the platform removed long ago. Save will fail, but the AI gets a contradictory signal — the catalog said property `legacyStyleMode` exists, and the API rejected it. A debug nightmare loop.
- **vs `oldest known`**: safer than `latest` (the AI does not suggest anything new), but creates the inverse problem — on an 8.3 stand the AI will not know about properties added in 8.2. Negates the whole point of the feature.

`latest known` with a clear marker is a compromise between "give the AI a full list" and "do not lie to the AI about a property definitely being there". The `latest-fallback` marker signals to the AI: the data can be a superset of the real stand.

## High-level architectural changes

### 1. Extend the model

- `ComponentRegistryEntry.Availability` → `AvailabilityRange? { Since, Until }` (nullable record).
- `ComponentPropertyDefinition.Availability` → the same type.
- `ComponentRegistry` becomes a wrapper manifest: `{ "$schema": ..., "schemaVersion": ..., "latestKnownVersion": ..., "categories": [...], "components": [...] }`.

### 2. Version resolver

- A new service `IPlatformVersionResolver` with a cache per `(environment-name, ttl)`.
- Implementation via `IApplicationClient` → `/rest/CreatioApiGateway/GetSysInfo` → `SysInfo.CoreVersion`.
- Cliogate-version guard — soft-fail into fallback.

### 3. Filter pipeline in the catalog

- `IComponentInfoCatalog.Search(search, targetVersion)` — an additional parameter.
- `IComponentInfoCatalog.Find(componentType, targetVersion)` — filters properties to those visible for the version.
- The cache is keyed by `(targetVersion, search)` on top of the existing `Lazy<>` — cheap.

### 4. Extend the MCP contract

```csharp
public sealed record ComponentInfoArgs(
    string? ComponentType = null,
    string? Search = null,
    string? EnvironmentName = null,   // → resolver
    string? TargetVersion = null      // override
);
```

`ComponentInfoResponse` adds `resolvedTargetVersion`, `resolvedFrom`.

### 5. Categories — data-driven

Move `CategoryOrder` out of [ComponentInfoCatalog.cs:40](../clio/Command/McpServer/Tools/ComponentInfoCatalog.cs#L40) and [ComponentInfoTool.cs:17](../clio/Command/McpServer/Tools/ComponentInfoTool.cs#L17) into the catalog. Categories are part of the top-level manifest, shipped in the NuGet pkg.

### 6. Loader → embedded resource from the NuGet pkg

Replacement of the current implementation [ComponentInfoCatalog.cs:74](../clio/Command/McpServer/Tools/ComponentInfoCatalog.cs#L74):

```csharp
// Current (reading from the clio repo):
// string registryPath = fileSystem.Path.Combine(
//     workingDirectoriesProvider.ExecutingDirectory,
//     "Command", "McpServer", "Data", "ComponentRegistry.json");

// Target (reading from the NuGet-bundled resource):
using var stream = typeof(ComponentRegistryEntry).Assembly
    .GetManifestResourceStream("Creatio.ComponentRegistry.ComponentRegistry.json")
    ?? throw new InvalidOperationException("Embedded registry resource not found.");
```

The file `clio/Command/McpServer/Data/ComponentRegistry.json` **is removed from the clio repo** at the same time as `<PackageReference Include="Creatio.ComponentRegistry" />` is added. The data lives only in the NuGet feed, the version is pinned via `Directory.Packages.props`.

### 7. Guidance updates

[PageModificationGuidanceResource.cs](../clio/Command/McpServer/Resources/PageModificationGuidanceResource.cs) must explain to the AI:
- to pass `environment-name` to `get-component-info` for correct filtering;
- to read `resolvedTargetVersion` so as not to suggest components/properties unavailable on the target version.

## Open questions for coordination with the team

1. **Source of truth long-term.** Hand-curated forever vs. auto-extraction from the platform. **A separate research** — see [architecture.md](architecture.md).
2. ~~**Fallback semantics when the version is unknown.**~~ **Closed:** `latest known` with the marker `resolvedFrom: "latest-fallback"`. Details and edge cases in the section [Fallback policy](#fallback-policy-locked-in).
3. **Version granularity.** `Major.Minor.Build` or a semver style? Is a `pre-release` (`8.2.0-rc1`) needed?
4. **Migration plan for the existing 92 records.** The base position is to leave them alone (all without `availability` = "always"). The question is whether to set an explicit `since: "8.0.0"` in the next release for clarity.
5. **`example` payload per component** may also depend on the version (new props in the example). Whether to do multi-`example` with `availability`, or to keep one canonical example for "latest known".
6. **Checks in CI.** Verify that no `since` exceeds the largest registered version (protection against typos).

## What is preserved (zero-break migration)

- `Lazy<>` loading wrapper — remains; a secondary filter cache per `(version, search)` is added.
- Fail-on-duplicate ([ComponentInfoCatalog.cs:101](../clio/Command/McpServer/Tools/ComponentInfoCatalog.cs#L101)) remains — critical.
- Existing callers without `target-version` preserve the current behavior (`latest-fallback`).

## What changes and what we no longer do

- **We do not store** registry data in the clio repo. This file moves into the `Creatio.ComponentRegistry` NuGet pkg.
- **We do not split** into per-version snapshot files in clio — diff operations live inside the composer-repo. clio sees a single unified bundle.
- **We do not use** file-system path lookup in the loader — only `Assembly.GetManifestResourceStream`.

## Mandatory follow-up for implementation

Per [clio/Command/McpServer/AGENTS.md](../clio/Command/McpServer/AGENTS.md):
- Changes to `get-component-info` require unit + E2E coverage in [clio.mcp.e2e](../clio.mcp.e2e).
- The version resolver — a separate unit suite (fake `IApplicationClient`).
- Guidance resources are updated in the same PR.
- A loader change requires testing with a real pinned NuGet (mock-NuGet or a test feed).

## Delivery stages (target architecture)

No intermediate pilot stage. The order of work is dictated by dependencies, not by validation.

1. **Composer-repo bootstrap** — create `creatio-component-registry-composer` with a single `supported-versions.json` (for example `["8.2.x"]`) and `overrides.json: {}`. Implement merge logic on top of the manual JSON from the current clio repo (as temporary input data, until the extractor in creatio-ui is ready). Output — NuGet pkg `Creatio.ComponentRegistry@0.1.0`.
2. **clio loader** — `Assembly.GetManifestResourceStream`, deletion of `Data/ComponentRegistry.json`, addition of `<PackageReference>`. Release of clio with `Creatio.ComponentRegistry 0.1.0` as a perfect drop-in replacement for the current in-repo JSON.
3. **The `Availability` model** + filter pipeline + nullable-by-default semantics (zero-break).
4. **`IPlatformVersionResolver`** + cliogate guard + cache + `GetSysInfo` integration.
5. **MCP contract** — `ComponentInfoArgs` + `ComponentInfoResponse` extension, fallback policy, guidance updates.
6. **Data-driven categories** — top-level `categories` field in the manifest.
7. **Extractor in creatio-ui** — `tools/component-registry-extractor` with AST walk, JSDoc parsing, output to NPM `@creatio/component-registry`. Switching the composer from manual input to the NPM feed.
8. **Jenkins in creatio-ui** — `Jenkinsfile.Registry` with branch-cut / push / GA-tag triggers.
9. **Composer CI** — cron + auto-bump, dependabot flow on the clio side.

Each stage finishes with a release of the corresponding NuGet/NPM package to the production feed. No mocked stage feeds.
