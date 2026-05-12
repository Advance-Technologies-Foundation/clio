# Implementation Plan — Part 1: NuGet distribution `Creatio.ComponentRegistry@0.1.0` and the clio loader migration

> **Status:** all open questions are closed (see the [§ Decisions log](#decisions-log) section). Ready for implementation.

## 0. Target state of this part

- New repo **[`creatio-component-registry-composer`](https://github.com/Advance-Technologies-Foundation/creatio-component-registry-composer)** (public, MIT) with .NET 8 composer logic.
- v0.1.0 input — a **bridge snapshot** from [`clio/Command/McpServer/Data/ComponentRegistry.json`](../clio/Command/McpServer/Data/ComponentRegistry.json), pinned via a sidecar `origin.json` with commit SHA and sha256. At v0.2.0+ (after the extractor in creatio-ui) the input becomes the npm package `@creatio/component-registry`.
- The NuGet package **`Creatio.ComponentRegistry@0.1.0`** is published to **public `api.nuget.org`** under the ATF owner (the same one that publishes `clio`).
- The package contains a DLL with two **embedded resources**: `ComponentRegistry.json` (data) + `metadata.json` (provenance).
- In clio:
  - `Directory.Packages.props` gains `<PackageVersion Include="Creatio.ComponentRegistry" Version="0.1.0" />`
  - `clio/clio.csproj` gains `<PackageReference Include="Creatio.ComponentRegistry" />` and loses `<Content Include="Command\McpServer\Data\**">`
  - `clio/Command/McpServer/Data/ComponentRegistry.json` **is removed** from the repo
  - `ComponentInfoCatalog.cs` reads the embedded resource via `Assembly.GetManifestResourceStream(...)` with a parameterless ctor + an `internal static LoadFromStream` factory for tests
- The behavior of the MCP tool `get-component-info` is **bit-for-bit identical** to before the migration (perfect drop-in)
- Invariants are preserved: `Lazy<>` initialization, fail-on-duplicate, `CategoryOrder` (temporarily hardcoded — data-driven categories is stage 6)

---

## 1. Steady-state architecture (for reference)

```
creatio-ui (Platform-UI team) ─── GA-tag `8.3.0`
    │
    └─ Jenkins extractor → npm publish @creatio/component-registry@8.3.0
        │
        ├── (branch-cut does NOT publish — only a baseline artifact for regressions)
        │
        ▼
composer-repo (AI/clio team) ─── webhook/cron pickup
    │
    └─ pull npm → diff → apply overrides.json → stamp metadata.json
        │
        ▼ dotnet pack + dotnet nuget push
public api.nuget.org ─── Creatio.ComponentRegistry@X.Y.Z (under ATF)
    │
    ▼ Renovate/Dependabot monitors
clio (clio team) ─── auto-PR with bumped <PackageVersion …/>
    │
    └─ human review + merge
```

**Ownership boundaries:**
- creatio-ui: decorators, JSDoc, extractor, npm publish — **own CI**, does not touch the composer
- composer-repo: `supported-versions.json`, `overrides.json`, merge logic, NuGet publish — **own CI in composer-repo (GitHub Actions)**, not in creatio-ui Jenkins
- clio: loader, MCP tools — bump via a bot-PR, **not a direct commit** from the composer pipeline

This is **5 separate CI runs**, not a single master pipeline. The split by ownership is deliberate (prevents cross-team coupling).

---

## 2. Repo layout for `creatio-component-registry-composer`

### 2.1 Stack: .NET 8 console app

| Rationale |
|---|
| `dotnet pack` — native step; composer and NuGet output in one toolchain |
| The AI/clio team writes C# (clio repo) — no context switch |
| The same `System.Text.Json.JsonSerializer` as in clio → zero serialization drift |
| Tests via NUnit/xUnit — the same pattern as clio.tests |
| ubuntu-latest GH runner has the .NET 8 SDK preinstalled |

### 2.2 Folder structure

```
creatio-component-registry-composer/
├── LICENSE                                   (MIT)
├── README.md
├── .gitignore                                (standard .NET)
├── .editorconfig
├── nuget.config                              (public nuget.org only)
├── Directory.Packages.props
├── global.json                               (pin SDK to 8.0.x)
├── composer.sln
│
├── src/
│   ├── Composer/                             ← console app
│   │   ├── Composer.csproj
│   │   ├── Program.cs                        (CLI: `composer build`)
│   │   ├── ComposerRunner.cs                 (orchestrator)
│   │   ├── Input/
│   │   │   ├── IInputSource.cs
│   │   │   ├── LocalJsonInputSource.cs       (v0.1.0 — reads data/input/<line>/)
│   │   │   └── NpmInputSource.cs             (stub, NotImplemented — for stage 7)
│   │   ├── Merge/
│   │   │   ├── RegistryMerger.cs             (v0.1.0: passthrough, single version)
│   │   │   └── OverridesApplier.cs           (apply aiHidden / aiOverlay)
│   │   ├── Output/
│   │   │   ├── BundleWriter.cs               (writes ComponentRegistry.json to obj/composed/)
│   │   │   └── MetadataStamper.cs            (provenance: composer SHA, origin.json copy)
│   │   └── Validation/
│   │       ├── DuplicateDetector.cs          (mirror of clio's fail-on-duplicate invariant)
│   │       └── OriginIntegrityChecker.cs     (sha256(snapshot) ≡ origin.json.sha256;
│   │                                          gh api commits/<sha> == 200)
│   │
│   └── Creatio.ComponentRegistry/            ← NuGet package project
│       ├── Creatio.ComponentRegistry.csproj
│       └── PackageMarker.cs                  (public marker class)
│
├── tests/
│   └── Composer.Tests/
│       ├── Composer.Tests.csproj
│       ├── RegistryMergerTests.cs
│       ├── OverridesApplierTests.cs
│       ├── MetadataStamperTests.cs
│       ├── OriginIntegrityCheckerTests.cs
│       └── DuplicateDetectorTests.cs
│
├── data/
│   ├── supported-versions.json               (["8.2.x"])
│   ├── overrides.json                        ({"aiHidden":[],"aiOverlay":{}})
│   └── input/
│       └── 8.2.x/
│           ├── ComponentRegistry.json        (snapshot from clio — 92 records)
│           └── origin.json                   (pinned commit + sha256)
│
├── .github/
│   └── workflows/
│       ├── ci.yml                            (PR build + tests + integrity check)
│       └── release.yml                       (manual trigger: pack + nuget push + tag)
│
└── docs/
    ├── how-to-update-snapshot.md             (the procedure for bumping data/input/8.2.x/)
    └── how-to-release.md                     (how to do a 0.1.x → 0.1.y bump)
```

### 2.3 Key files

**`global.json`:**
```json
{ "sdk": { "version": "8.0.0", "rollForward": "latestMinor" } }
```

**`nuget.config`** (public-only, **without Nexus**):
```xml
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
```

**`data/supported-versions.json`:**
```json
["8.2.x"]
```

**`data/overrides.json`:**
```json
{ "aiHidden": [], "aiOverlay": {} }
```

**`data/input/8.2.x/origin.json`** (pinned snapshot provenance — Q2 decision):
```json
{
  "kind": "local-snapshot",
  "originRepo": "Advance-Technologies-Foundation/clio",
  "originPath": "clio/Command/McpServer/Data/ComponentRegistry.json",
  "originCommit": "<exact clio SHA at time of copy>",
  "sha256": "<sha256 of data/input/8.2.x/ComponentRegistry.json>"
}
```

---

## 3. Composer logic for v0.1.0

### 3.1 Pipeline

```
data/supported-versions.json (["8.2.x"])
            │
            ▼
LocalJsonInputSource → reads data/input/8.2.x/ComponentRegistry.json (92 items)
            │
            ▼
OriginIntegrityChecker → assert sha256(snapshot) ≡ origin.json.sha256
                       → assert gh api commits/<origin.originCommit> == 200
            │ (fail on mismatch — protection against a stale snapshot)
            ▼
RegistryMerger v0.1.0 → passthrough (single version)
            │
            ▼
OverridesApplier → no-op at v0.1.0 (overrides are empty)
            │
            ▼
DuplicateDetector → throw on duplicate componentType
            │
            ▼
BundleWriter → obj/composed/ComponentRegistry.json (top-level array, identical to input)
MetadataStamper → obj/composed/metadata.json (composer SHA + copy of origin.json)
            │
            ▼
dotnet pack Creatio.ComponentRegistry → .nupkg → artifacts/
            │
            ▼
dotnet nuget push artifacts/*.nupkg → api.nuget.org
```

### 3.2 Output schema (v0.1.0 — **simplified**, drop-in compatible)

**`ComponentRegistry.json`** in the NuGet pkg — **the same top-level array of 92 records** that currently sits in clio. No wrappers like `{ "schemaVersion": ..., "components": [...] }` — that is stage 3 (the `Availability` model) and stage 6 (data-driven categories). `JsonSerializer.Deserialize<ComponentRegistryEntry[]>` in clio works without changes.

**`metadata.json`** (v0.1.0):
```json
{
  "registryVersion": "0.1.0",
  "buildTime": "2026-05-12T10:00:00Z",
  "supportedPlatformVersions": ["8.2.x"],
  "sources": {
    "composer": { "gitSha": "<composer-repo SHA>", "version": "0.1.0" },
    "input": {
      "8.2.x": {
        "kind": "local-snapshot",
        "originRepo": "Advance-Technologies-Foundation/clio",
        "originPath": "clio/Command/McpServer/Data/ComponentRegistry.json",
        "originCommit": "<clio SHA>",
        "sha256": "<sha256 of input>"
      }
    },
    "overrides": { "gitSha": "<composer SHA>", "appliedCount": 0 }
  }
}
```

`metadata.json` is effectively `composer.gitSha` + `overrides.appliedCount` + a full copy of `origin.json` from the composer-repo. No resolution logic at runtime — everything is determined by the content of `data/input/<line>/origin.json`.

### 3.3 Packing JSON as an embedded resource

**`src/Creatio.ComponentRegistry/Creatio.ComponentRegistry.csproj`:**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <PackageId>Creatio.ComponentRegistry</PackageId>
    <Version>0.1.0</Version>
    <Authors>ATF</Authors>
    <Company>Creatio</Company>
    <Description>Curated Freedom UI component catalog consumed by clio MCP server.</Description>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <RepositoryUrl>https://github.com/Advance-Technologies-Foundation/creatio-component-registry-composer</RepositoryUrl>
    <RepositoryType>git</RepositoryType>
    <PackageTags>creatio;clio;mcp;ai;freedom-ui</PackageTags>
    <GeneratePackageOnBuild>false</GeneratePackageOnBuild>
    <IncludeBuildOutput>true</IncludeBuildOutput>
    <RootNamespace>Creatio.ComponentRegistry</RootNamespace>
    <AssemblyName>Creatio.ComponentRegistry</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <!-- Files are generated by the composer-runner in obj/composed/ BEFORE dotnet pack -->
    <EmbeddedResource Include="$(MSBuildProjectDirectory)/../../obj/composed/ComponentRegistry.json">
      <LogicalName>Creatio.ComponentRegistry.ComponentRegistry.json</LogicalName>
    </EmbeddedResource>
    <EmbeddedResource Include="$(MSBuildProjectDirectory)/../../obj/composed/metadata.json">
      <LogicalName>Creatio.ComponentRegistry.metadata.json</LogicalName>
    </EmbeddedResource>
  </ItemGroup>
</Project>
```

**Critical:** `LogicalName` fixes the exact resource name. Without it MSBuild generates something like `Creatio.ComponentRegistry.._..obj.composed.ComponentRegistry.json` (because the files live outside the csproj folder).

**`PackageMarker.cs`:**
```csharp
namespace Creatio.ComponentRegistry;

/// <summary>
/// Anchor type used by consumers (clio) to locate the assembly that ships
/// the embedded component registry payload.
/// Usage: <c>typeof(PackageMarker).Assembly.GetManifestResourceStream(...)</c>
/// </summary>
public static class PackageMarker { }
```

---

## 4. NuGet package — structure and identification

### 4.1 Metadata

| Field | Value |
|---|---|
| `PackageId` | `Creatio.ComponentRegistry` |
| `Version` | `0.1.0` (3-part semver, without a `v` prefix in tags) |
| Owner on nuget.org | `ATF` (the same one that publishes clio) |
| `Authors` | `ATF` |
| `Company` | `Creatio` |
| `Description` | `Curated Freedom UI component catalog consumed by clio MCP server.` |
| `PackageLicenseExpression` | `MIT` |
| `RepositoryUrl` | `https://github.com/Advance-Technologies-Foundation/creatio-component-registry-composer` |
| `PackageTags` | `creatio;clio;mcp;ai;freedom-ui` |
| `TargetFramework` | `netstandard2.0` (max compatibility with clio `net8.0;net10.0`) |
| Destination feed | `https://api.nuget.org/v3/index.json` (public) |

### 4.2 `.nupkg` structure

```
Creatio.ComponentRegistry.0.1.0.nupkg
├── _rels/.../...
├── package/services/metadata/...
├── Creatio.ComponentRegistry.nuspec
└── lib/
    └── netstandard2.0/
        └── Creatio.ComponentRegistry.dll  ← contains 2 embedded resources
```

**Local verification after `dotnet pack`:**
```csharp
var asm = Assembly.LoadFrom("Creatio.ComponentRegistry.dll");
foreach (var n in asm.GetManifestResourceNames()) Console.WriteLine(n);
// Expected:
// Creatio.ComponentRegistry.ComponentRegistry.json
// Creatio.ComponentRegistry.metadata.json
```

### 4.3 Why a DLL with an embedded resource (and not `contentFiles/`)

| Approach | Pros | Cons |
|---|---|---|
| **embedded resource in DLL** (chosen) | `Assembly.GetManifestResourceStream` — standard; zero dependency on the filesystem; works with `PackAsTool=true` in clio | A single marker class is required |
| `contentFiles/any/any/*.json` | A plain file for inspection | `PackAsTool=true` unstably handles content files; breaks the offline invariant |

---

## 5. Changes in the clio repo

### 5.1 Files to change

| File | Action |
|---|---|
| `clio/Command/McpServer/Data/ComponentRegistry.json` | **Delete** |
| `Directory.Packages.props` | Add `<PackageVersion Include="Creatio.ComponentRegistry" Version="0.1.0" />` |
| `clio/clio.csproj` | Add `<PackageReference Include="Creatio.ComponentRegistry" />`; delete the `<Content Include="Command\McpServer\Data\**">` block |
| `clio/Command/McpServer/Tools/ComponentInfoCatalog.cs` | Rewrite: drop `IFileSystem`/`IWorkingDirectoriesProvider` deps; parameterless ctor + `internal static LoadFromStream(Stream)` factory |
| `clio/Program.cs` (DI registration) | Verify that `ComponentInfoCatalog` is registered without an `IFileSystem` dependency |
| `clio.tests/Command/McpServer/ComponentInfoToolTests.cs` | Hybrid (Variant C): 6 existing tests → `LoadFromStream(MemoryStream)` (hermetic), add 2-3 smoke tests for the real embedded resource |
| `clio.mcp.e2e/ComponentInfoToolE2ETests.cs` | No changes |
| `nuget.config` | **No changes** — the wildcard `*` → nuget.org will pick up `Creatio.ComponentRegistry` |
| `clio/Command/McpServer/AGENTS.md` | Update the section about the data source (now in NuGet, not in-repo) |

### 5.2 clio infrastructure checks

- `Directory.Packages.props` has `<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>` → `<PackageReference>` in `clio.csproj` without `Version=` — the invariant is **upheld**.
- `clio.csproj` has `<PackAsTool>true</PackAsTool>` — embedded resources in `Creatio.ComponentRegistry.dll` work in tool mode (the DLL is copied into `tools/<tfm>/any/`).
- `clio.csproj` already has `InternalsVisibleTo("clio.tests")` and `InternalsVisibleTo("clio.mcp.e2e")` — `internal static LoadFromStream(Stream)` is cheap, without additional csproj changes.

### 5.3 Sketch of the new `ComponentInfoCatalog.cs`

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Creatio.ComponentRegistry; // PackageMarker

namespace Clio.Command.McpServer.Tools;

public interface IComponentInfoCatalog {
    IReadOnlyList<ComponentRegistryEntry> GetAll();
    IReadOnlyList<ComponentRegistryEntry> Search(string? search);
    ComponentRegistryEntry? Find(string componentType);
}

public sealed class ComponentInfoCatalog : IComponentInfoCatalog {
    private const string RegistryResourceName = "Creatio.ComponentRegistry.ComponentRegistry.json";
    private static readonly string[] CategoryOrder = ["containers", "fields", "interactive", "display"];

    private readonly Lazy<ComponentCatalogState> _catalogState;

    // Public parameterless ctor — for DI (production path)
    public ComponentInfoCatalog()
        : this(() => LoadCatalogStateFromEmbeddedResource()) { }

    // Internal ctor — for tests via LoadFromStream
    internal ComponentInfoCatalog(Func<ComponentCatalogState> loader) {
        _catalogState = new Lazy<ComponentCatalogState>(loader, isThreadSafe: true);
    }

    public IReadOnlyList<ComponentRegistryEntry> GetAll() => _catalogState.Value.Entries;

    public IReadOnlyList<ComponentRegistryEntry> Search(string? search) {
        if (string.IsNullOrWhiteSpace(search)) return GetAll();
        string query = search.Trim();
        return _catalogState.Value.Entries.Where(e => Matches(e, query)).ToArray();
    }

    public ComponentRegistryEntry? Find(string componentType) {
        if (string.IsNullOrWhiteSpace(componentType)) return null;
        return _catalogState.Value.Lookup.TryGetValue(componentType.Trim(), out var entry) ? entry : null;
    }

    private static ComponentCatalogState LoadCatalogStateFromEmbeddedResource() {
        Assembly registryAssembly = typeof(PackageMarker).Assembly;
        using Stream? stream = registryAssembly.GetManifestResourceStream(RegistryResourceName);
        if (stream is null) {
            string available = string.Join(", ", registryAssembly.GetManifestResourceNames());
            throw new InvalidOperationException(
                $"Embedded resource '{RegistryResourceName}' was not found in {registryAssembly.FullName}. "
              + $"Available resources: [{available}]");
        }
        return LoadFromStream(stream);
    }

    // Internal for tests (accessible via InternalsVisibleTo)
    internal static ComponentCatalogState LoadFromStream(Stream stream) {
        ComponentRegistryEntry[]? rawEntries = JsonSerializer.Deserialize<ComponentRegistryEntry[]>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (rawEntries is null || rawEntries.Length == 0) {
            throw new InvalidOperationException("Component registry is empty or invalid.");
        }
        // duplicate detection + ordering + lookup dictionary (1:1 with the current logic)
        // ...
        return new ComponentCatalogState(orderedEntries, lookup);
    }

    // Matches / Contains / GetCategorySortKey — unchanged from the current code
}
```

### 5.4 Call-site check

Before the PR: `grep -rn "new ComponentInfoCatalog" /Users/a.kravchuk/Projects/clio/clio/ /Users/a.kravchuk/Projects/clio/clio.tests/ /Users/a.kravchuk/Projects/clio/clio.mcp.e2e/`.

DI registration (`services.AddSingleton<IComponentInfoCatalog, ComponentInfoCatalog>()`) stays unchanged — DI auto-resolves the parameterless ctor.

---

## 6. CI/CD for composer-repo — GitHub Actions ubuntu-latest

### 6.1 `.github/workflows/ci.yml` (PR builds)

```yaml
name: ci

on:
  pull_request:
  push:
    branches: [master]

jobs:
  build-and-test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'

      - name: Restore
        run: dotnet restore composer.sln

      - name: Build composer
        run: dotnet build src/Composer/Composer.csproj -c Release --no-restore

      - name: Origin integrity check
        env:
          GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}
        run: |
          dotnet run --project src/Composer/Composer.csproj -c Release --no-build -- check-origin \
            --input-root data/input

      - name: Composer tests
        run: dotnet test tests/Composer.Tests/Composer.Tests.csproj -c Release --no-build

      - name: Composer dry-run build (verify packing)
        run: |
          dotnet run --project src/Composer/Composer.csproj -c Release --no-build -- build \
            --supported-versions data/supported-versions.json \
            --overrides data/overrides.json \
            --input-root data/input \
            --output-dir obj/composed \
            --package-version 0.0.0-ci
          dotnet pack src/Creatio.ComponentRegistry/Creatio.ComponentRegistry.csproj \
            -c Release -o artifacts /p:PackageVersion=0.0.0-ci
          ls -la artifacts/
```

### 6.2 `.github/workflows/release.yml` (manual NuGet publish)

```yaml
name: release-to-nuget

on:
  workflow_dispatch:
    inputs:
      package_version:
        description: 'Semver version (e.g. 0.1.0)'
        required: true
        default: '0.1.0'

jobs:
  release:
    runs-on: ubuntu-latest
    env:
      PACKAGE_VERSION: ${{ inputs.package_version }}
    steps:
      - uses: actions/checkout@v4
        with: { fetch-depth: 0 }

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'

      - name: Validate version format (semver 3-part)
        run: |
          if [[ ! "${PACKAGE_VERSION}" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[A-Za-z0-9.]+)?$ ]]; then
            echo "Invalid version: ${PACKAGE_VERSION}. Expected semver MAJOR.MINOR.PATCH[-prerelease]"
            exit 1
          fi

      - name: Restore
        run: dotnet restore composer.sln

      - name: Build composer
        run: dotnet build src/Composer/Composer.csproj -c Release --no-restore

      - name: Origin integrity check
        env:
          GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}
        run: |
          dotnet run --project src/Composer/Composer.csproj -c Release --no-build -- check-origin \
            --input-root data/input

      - name: Composer tests
        run: dotnet test tests/Composer.Tests/Composer.Tests.csproj -c Release --no-build

      - name: Compose bundle
        run: |
          dotnet run --project src/Composer/Composer.csproj -c Release --no-build -- build \
            --supported-versions data/supported-versions.json \
            --overrides data/overrides.json \
            --input-root data/input \
            --output-dir obj/composed \
            --package-version "${PACKAGE_VERSION}"

      - name: Pack Creatio.ComponentRegistry
        run: |
          dotnet pack src/Creatio.ComponentRegistry/Creatio.ComponentRegistry.csproj \
            -c Release -o artifacts \
            /p:PackageVersion=${PACKAGE_VERSION}

      - name: Verify embedded resources
        run: |
          dotnet run --project tests/Composer.Tests/Composer.Tests.csproj -c Release --no-build \
            -- verify-resources "artifacts/Creatio.ComponentRegistry.${PACKAGE_VERSION}.nupkg"

      - uses: actions/upload-artifact@v4
        with:
          name: nupkg-${{ inputs.package_version }}
          path: artifacts/*.nupkg

      - name: Push to nuget.org
        run: |
          dotnet nuget push "artifacts/Creatio.ComponentRegistry.${PACKAGE_VERSION}.nupkg" \
            --api-key "${{ secrets.CLIO_NUGET_API_KEY }}" \
            --source https://api.nuget.org/v3/index.json
          # WITHOUT --skip-duplicate: explicit fail-on-duplicate

      - name: Create git tag
        run: |
          git tag "${PACKAGE_VERSION}"
          git push origin "${PACKAGE_VERSION}"
```

### 6.3 composer-repo secret setup

- `CLIO_NUGET_API_KEY` — the same API key (ATF on nuget.org) that publishes clio. Copy into GH Secrets of composer-repo.
- `GITHUB_TOKEN` — auto-provisioned, needed for `check-origin` (verifying clio commit existence via `gh api`).

---

## 7. Sequence of work (PR flow)

### 7.1 Cross-repo dependency

clio cannot merge a PR with `<PackageReference Include="Creatio.ComponentRegistry" />` until `0.1.0` is published to nuget.org.

### 7.2 Order

```
┌─────────────────────────────────────────────────────────────────────┐
│ COMPOSER-REPO BOOTSTRAP                                             │
│ 1. PR #1: layout (sln, csprojs, data/, .github/workflows/, docs/)   │
│    + snapshot ComponentRegistry.json + origin.json with pinned clio SHA│
│ 2. PR #2: composer runtime (Program.cs, Input, Merge, Output)        │
│ 3. PR #3: composer tests + DuplicateDetector + MetadataStamper +    │
│           OriginIntegrityChecker                                     │
└──────────────────────────┬──────────────────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────────────────┐
│ FIRST NuGet PUBLISH                                                 │
│ 4. Manual trigger of the `release-to-nuget` workflow with PACKAGE_VERSION=0.1.0│
│    - Origin integrity check passes                                  │
│    - Composer tests pass                                            │
│    - dotnet pack + push to api.nuget.org                            │
│    - git tag 0.1.0 created                                          │
│ 5. VERIFICATION: `dotnet nuget locals all --clear` locally →        │
│    `dotnet add package Creatio.ComponentRegistry --version 0.1.0` → │
│    `Assembly.GetManifestResourceNames()` → 2 resources              │
└──────────────────────────┬──────────────────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────────────────┐
│ CLIO MIGRATION PR                                                   │
│ 6. PR in the clio repo, single commit set:                          │
│    - Directory.Packages.props: + PackageVersion                     │
│    - clio.csproj: + PackageReference, - Content Include             │
│    - ComponentInfoCatalog.cs: rewrite the loader (Variant C)        │
│    - ComponentInfoToolTests.cs: hermetic LoadFromStream + smoke     │
│    - delete clio/Command/McpServer/Data/ComponentRegistry.json      │
│    - update AGENTS.md                                               │
│ 7. clio CI (build.yml) passes:                                      │
│    - dotnet restore pulls Creatio.ComponentRegistry@0.1.0 from n.org│
│    - unit tests green                                               │
│    - E2E (ComponentInfoToolE2ETests) green — drop-in proof          │
│ 8. Merge the clio PR                                                │
└─────────────────────────────────────────────────────────────────────┘
```

### 7.3 Gotchas

| # | Gotcha | Mitigation |
|---|---|---|
| G1 | clio CI fails because it does not yet see `Creatio.ComponentRegistry` | Push the NuGet BEFORE opening the clio PR (strict ordering in §7.2). |
| G2 | nuget.org does not allow overwriting a published version — `0.1.0` is immutable | If a bug is found — bump to `0.1.1`. The CI-first pass through the CI workflow (auto on PR) tests the pack logic without push. |
| G3 | `dotnet pack` sucks embedded resources from `obj/composed/`, the file is there via a runtime composer step. `obj/` is typically gitignored — checkout does not have these files | OK: the composer-runner is launched on the same checkout BEFORE `dotnet pack`. `.gitignore` ignores `obj/` as a category, but MSBuild creates the `obj/composed/` content after checkout. |
| G4 | `LogicalName` ≠ constant in the clio coordinator | The `RegistryResourceName` constant in `ComponentInfoCatalog.cs` + a test in Composer.Tests that builds the pack and inspects the assembly. |
| G5 | netstandard2.0 vs net8/net10 — NuGet warnings | `netstandard2.0` is universally consumable from net8+. No warnings are expected. |
| G6 | DI: the changed ctor `ComponentInfoCatalog()` breaks an explicit `new ComponentInfoCatalog(fs, wd)` | `grep -rn "new ComponentInfoCatalog"` before the PR. |
| G7 | Snapshot input in composer-repo and a file in clio until the clio-PR is merged — two sources | Mitigation: the composer bootstrap PR is done immediately; the clio-PR with the migration is planned as the next step. The window is short. |

---

## 8. Test strategy — Variant C (Hybrid)

### 8.1 Unit tests in clio.tests (hermetic)

Convert the 6 existing tests to `LoadFromStream` (via the `internal` factory):

```csharp
private static ComponentInfoTool CreateTool() {
    using var stream = new MemoryStream(Encoding.UTF8.GetBytes(TestRegistryJson));
    var state = ComponentInfoCatalog.LoadFromStream(stream);
    var catalog = new ComponentInfoCatalog(() => state);  // internal ctor
    return new ComponentInfoTool(catalog);
}
```

**What is preserved:**
- 100% of the current assertions (Count == 6, exact crt.Gallery bulkActions, etc.)
- `MockFileSystem` + `WorkingDirectoriesProvider` disappear
- Tests are deterministic + hermetic

### 8.2 Smoke tests (real embedded resource)

2-3 new tests in the same suite:

```csharp
[Test] public void Real_Registry_Should_Load_From_Embedded_Resource() {
    var catalog = new ComponentInfoCatalog();   // production ctor
    catalog.GetAll().Count.Should().BeGreaterThan(50);
}

[Test] public void Real_Registry_Should_Contain_Canonical_Components() {
    var catalog = new ComponentInfoCatalog();
    catalog.Find("crt.TabContainer").Should().NotBeNull();
    catalog.Find("crt.Button").Should().NotBeNull();
    catalog.Find("crt.MenuItem").Should().NotBeNull();
}

[Test] public void Real_Registry_Should_Expose_Expected_Resource_Names() {
    var asm = typeof(Creatio.ComponentRegistry.PackageMarker).Assembly;
    asm.GetManifestResourceNames().Should().Contain(
        "Creatio.ComponentRegistry.ComponentRegistry.json");
}
```

Smoke tests catastrophically catch a `LogicalName` mismatch (the loader would fail at startup).

### 8.3 E2E in clio.mcp.e2e

`ComponentInfoToolE2ETests.cs` remains unchanged — starts a real clio process, reads the real embedded resource. This is our **drop-in regression detector** (Variant D from Q13 — chosen as a replacement for a snapshot-baseline test).

### 8.4 Composer tests in composer-repo

| Test | Verifies |
|---|---|
| `LocalJsonInputSource_Should_Parse_Manual_Snapshot` | Parsing of 92 records |
| `OriginIntegrityChecker_Should_Throw_When_Hash_Mismatches` | Protection against a stale snapshot |
| `OriginIntegrityChecker_Should_Validate_Commit_Exists` | gh api commit lookup |
| `DuplicateDetector_Should_Throw_On_Duplicates` | Mirror of the clio invariant |
| `OverridesApplier_NoOp_When_Overrides_Empty` | v0.1.0 base case |
| `OverridesApplier_Hides_Components_Listed_In_AiHidden` | Readiness for 0.2.0 |
| `MetadataStamper_Should_Embed_Origin_Json_Verbatim` | Provenance |
| `BundleWriter_Should_Produce_Top_Level_Array_Compatible_With_Clio` | drop-in compatibility |
| `Packed_Assembly_Should_Expose_Two_Embedded_Resources` | Build pack locally, inspect |

### 8.5 Acceptance criteria

- [ ] `Creatio.ComponentRegistry@0.1.0` is published to api.nuget.org under ATF
- [ ] `assembly.GetManifestResourceNames()` returns exactly 2 names
- [ ] `JsonSerializer.Deserialize<ComponentRegistryEntry[]>(stream)` returns 92 entries
- [ ] clio build on CI is green with `<PackageReference Include="Creatio.ComponentRegistry" />`
- [ ] `clio/Command/McpServer/Data/ComponentRegistry.json` is removed from git
- [ ] `ComponentInfoToolTests` is green (hermetic Variant C + 3 smoke tests)
- [ ] `ComponentInfoToolE2ETests` is green (drop-in proof)
- [ ] git tag `0.1.0` in composer-repo is created
- [ ] `metadata.json` inside the package contains composer SHA + an exact copy of `origin.json`

---

## 9. Out-of-scope (explicit reminders)

Not part of this section:
- No changes in the `ComponentRegistryEntry`/`ComponentPropertyDefinition` model (the `Availability` fields, top-level wrapper — that is stages 3 and 6)
- No changes in `ComponentInfoArgs`/`ComponentInfoResponse` (new args — that is stage 5)
- No `IPlatformVersionResolver`, no cliogate integration (stage 4)
- No auto-bump, cron trigger in the composer (stage 9)
- No NPM `@creatio/component-registry` integration (stage 7)
- No changes in the creatio-ui repo
- Hardcoded `CategoryOrder` remains (data-driven — stage 6)

---

## Critical Files for Implementation

- [clio/Command/McpServer/Tools/ComponentInfoCatalog.cs](../clio/Command/McpServer/Tools/ComponentInfoCatalog.cs) — the rewritten loader
- [clio/Command/McpServer/Data/ComponentRegistry.json](../clio/Command/McpServer/Data/ComponentRegistry.json) — the snapshot source (then deleted)
- [Directory.Packages.props](../Directory.Packages.props) — add `<PackageVersion …/>`
- [clio/clio.csproj](../clio/clio.csproj) — add `<PackageReference …/>`, remove `<Content Include…>`
- [clio.tests/Command/McpServer/ComponentInfoToolTests.cs](../clio.tests/Command/McpServer/ComponentInfoToolTests.cs) — Variant C rewrite

---

## Decisions log

All open questions are closed in the Q&A with the plan owner. Summary:

| # | Question | Decision |
|---|---|---|
| 1 | Composer technology | **.NET 8 console app** (C#, NUnit). The same toolchain as clio → zero serialization drift. |
| 2 | `originCommit` provenance | **Sidecar `data/input/<line>/origin.json`** with pinned commit SHA + sha256. CI check `OriginIntegrityChecker`: sha256(snapshot) ≡ origin.json.sha256 and commit exists in the clio repo. |
| 3 | License | **MIT** for the entire composer-repo (consistency with clio). |
| 4 | composer-repo hosting | **Public GitHub** `Advance-Technologies-Foundation/creatio-component-registry-composer` (consistency with the clio ecosystem). |
| 5 | Test strategy for `ComponentInfoCatalog` | **Variant C (Hybrid):** 6 existing tests → hermetic `LoadFromStream`; +3 smoke tests against the real embedded resource. |
| 6 | NuGet API key | **Reuse `CLIO_NUGET_API_KEY`** (the same ATF account on nuget.org). Bus factor can be addressed later. |
| 7 | Who creates composer-repo | **Created** by the user self-service. Repo empty, ready for the first PR. |
| ~~8~~ | ~~Jenkins folder~~ | **N/A** — composer-repo public GitHub → GitHub Actions, not Jenkins. |
| 9 | CI runner | **`ubuntu-latest`** (GH-hosted, free for a public repo). nuget.org is reachable from the public internet (HTTP 403 = auth required, not a network block). |
| 10 | `--skip-duplicate` flag | **Without the flag** (consistency with clio release.yml). Fail-on-duplicate = explicit signal "a bump is needed". |
| 11.a | Version format | **3-part semver** `0.1.0` (matches the research MAJOR.MINOR.PATCH rules). |
| 11.b | Tag prefix | **Without `v`** (consistency with the clio convention `1.0.0.1`, `2.0.0.1`, …). |
| 12 | Constructor strategy | **Parameterless public ctor** for DI + **internal static `LoadFromStream(Stream)` factory** for hermetic tests. `InternalsVisibleTo("clio.tests")` is already configured. |
| 13 | Drop-in regression test | **Variant D** — without a snapshot baseline test; rely on the existing `ComponentInfoToolE2ETests` as the regression detector. |

### Architectural uplifts (emerged during Q&A):

- **The final NuGet destination is public `api.nuget.org`, not Terrasoft Nexus.** clio is published to nuget.org under the ATF owner; `Creatio.ComponentRegistry` follows the same pattern. A symmetric public ecosystem.
- **The steady-state trigger is the GA tag in creatio-ui, not branch-cut.** Branch-cut produces a baseline artifact for extractor regressions; the real publish is triggered by the GA tag (`8.3.0`, `8.3.1`, …).
- **The composer has its own CI in composer-repo (GitHub Actions),** rather than running from creatio-ui Jenkins. This is a conscious ownership split (Platform-UI ≠ AI/clio team).
- **clio bump via Renovate/Dependabot auto-PR**, not a direct commit from the composer pipeline. Review gating is preserved.
- **`overrides.json` and `supported-versions.json` live in composer-repo** (AI/clio team-owned), not in creatio-ui Jenkins.
