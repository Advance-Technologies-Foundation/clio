# Implementation Plan — Частина 1: NuGet-distribution `Creatio.ComponentRegistry@0.1.0` і clio loader міграція

> **Статус:** усі open questions закриті (див. секцію [§ Decisions log](#decisions-log)). Готово до implementation.

## 0. Цільовий стан цієї частини

- Новий repo **[`creatio-component-registry-composer`](https://github.com/Advance-Technologies-Foundation/creatio-component-registry-composer)** (public, MIT) із .NET 8 composer-логікою.
- v0.1.0 input — **bridge-snapshot** з [`clio/Command/McpServer/Data/ComponentRegistry.json`](../clio/Command/McpServer/Data/ComponentRegistry.json), pinned через sidecar `origin.json` із commit SHA і sha256. На v0.2.0+ (після extractor у creatio-ui) input стане npm пакетом `@creatio/component-registry`.
- NuGet-пакет **`Creatio.ComponentRegistry@0.1.0`** опублікований на **public `api.nuget.org`** під ATF owner (той самий що публікує `clio`).
- Пакет містить DLL з двома **embedded resources**: `ComponentRegistry.json` (data) + `metadata.json` (provenance).
- У clio:
  - `Directory.Packages.props` отримує `<PackageVersion Include="Creatio.ComponentRegistry" Version="0.1.0" />`
  - `clio/clio.csproj` отримує `<PackageReference Include="Creatio.ComponentRegistry" />` і втрачає `<Content Include="Command\McpServer\Data\**">`
  - `clio/Command/McpServer/Data/ComponentRegistry.json` **видалено** з repo
  - `ComponentInfoCatalog.cs` читає embedded resource через `Assembly.GetManifestResourceStream(...)` із parameterless ctor + `internal static LoadFromStream` factory для тестів
- Поведінка MCP-tool `get-component-info` **бітово ідентична** до міграції (perfect drop-in)
- Інваріанти збережені: `Lazy<>` ініціалізація, fail-on-duplicate, `CategoryOrder` (тимчасово hardcoded — data-driven categories — це етап 6)

---

## 1. Steady-state architecture (для довідки)

```
creatio-ui (Platform-UI team) ─── GA-tag `8.3.0`
    │
    └─ Jenkins extractor → npm publish @creatio/component-registry@8.3.0
        │
        ├── (branch-cut НЕ публікує — лише baseline artifact для регресій)
        │
        ▼
composer-repo (AI/clio team) ─── webhook/cron pickup
    │
    └─ pull npm → diff → apply overrides.json → stamp metadata.json
        │
        ▼ dotnet pack + dotnet nuget push
public api.nuget.org ─── Creatio.ComponentRegistry@X.Y.Z (under ATF)
    │
    ▼ Renovate/Dependabot моніторить
clio (clio team) ─── auto-PR з bumped <PackageVersion …/>
    │
    └─ human review + merge
```

**Ownership boundaries:**
- creatio-ui: декоратори, JSDoc, extractor, npm publish — **own CI**, не торкається composer
- composer-repo: `supported-versions.json`, `overrides.json`, merge-логіка, NuGet publish — **own CI у composer-repo (GitHub Actions)**, не у creatio-ui Jenkins
- clio: loader, MCP tools — bump через bot-PR, **не direct commit** від composer pipeline

Це **5 окремих CI runs**, не один master pipeline. Розрив за ownership — навмисний (запобігає cross-team coupling).

---

## 2. Repo layout для `creatio-component-registry-composer`

### 2.1 Стек: .NET 8 console app

| Обґрунтування |
|---|
| `dotnet pack` — native крок; composer і NuGet output в одному toolchain |
| Команда AI/clio пише C# (clio repo) — без context-switch |
| Той самий `System.Text.Json.JsonSerializer` що в clio → нульовий serialization-drift |
| Tests через NUnit/xUnit — той самий патерн що clio.tests |
| ubuntu-latest GH runner має .NET 8 SDK preinstalled |

### 2.2 Структура папок

```
creatio-component-registry-composer/
├── LICENSE                                   (MIT)
├── README.md
├── .gitignore                                (стандартний .NET)
├── .editorconfig
├── nuget.config                              (public nuget.org only)
├── Directory.Packages.props
├── global.json                               (pin SDK на 8.0.x)
├── composer.sln
│
├── src/
│   ├── Composer/                             ← console app
│   │   ├── Composer.csproj
│   │   ├── Program.cs                        (CLI: `composer build`)
│   │   ├── ComposerRunner.cs                 (orchestrator)
│   │   ├── Input/
│   │   │   ├── IInputSource.cs
│   │   │   ├── LocalJsonInputSource.cs       (v0.1.0 — читає data/input/<line>/)
│   │   │   └── NpmInputSource.cs             (stub, NotImplemented — для етапу 7)
│   │   ├── Merge/
│   │   │   ├── RegistryMerger.cs             (v0.1.0: passthrough, одна версія)
│   │   │   └── OverridesApplier.cs           (apply aiHidden / aiOverlay)
│   │   ├── Output/
│   │   │   ├── BundleWriter.cs               (пише ComponentRegistry.json у obj/composed/)
│   │   │   └── MetadataStamper.cs            (provenance: composer SHA, origin.json copy)
│   │   └── Validation/
│   │       ├── DuplicateDetector.cs          (mirror fail-on-duplicate інваріанту clio)
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
│           ├── ComponentRegistry.json        (snapshot із clio — 92 records)
│           └── origin.json                   (pinned commit + sha256)
│
├── .github/
│   └── workflows/
│       ├── ci.yml                            (PR build + tests + integrity check)
│       └── release.yml                       (manual trigger: pack + nuget push + tag)
│
└── docs/
    ├── how-to-update-snapshot.md             (процедура bump-у data/input/8.2.x/)
    └── how-to-release.md                     (як зробити bump 0.1.x → 0.1.y)
```

### 2.3 Ключові файли

**`global.json`:**
```json
{ "sdk": { "version": "8.0.0", "rollForward": "latestMinor" } }
```

**`nuget.config`** (public-only, **без Nexus**):
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

## 3. Composer-логіка для v0.1.0

### 3.1 Pipeline

```
data/supported-versions.json (["8.2.x"])
            │
            ▼
LocalJsonInputSource → читає data/input/8.2.x/ComponentRegistry.json (92 items)
            │
            ▼
OriginIntegrityChecker → assert sha256(snapshot) ≡ origin.json.sha256
                       → assert gh api commits/<origin.originCommit> == 200
            │ (fail на mismatch — захист від stale snapshot)
            ▼
RegistryMerger v0.1.0 → passthrough (одна версія)
            │
            ▼
OverridesApplier → no-op на v0.1.0 (overrides порожні)
            │
            ▼
DuplicateDetector → throw при дублях componentType
            │
            ▼
BundleWriter → obj/composed/ComponentRegistry.json (top-level масив, ідентичний input)
MetadataStamper → obj/composed/metadata.json (composer SHA + copy origin.json)
            │
            ▼
dotnet pack Creatio.ComponentRegistry → .nupkg → artifacts/
            │
            ▼
dotnet nuget push artifacts/*.nupkg → api.nuget.org
```

### 3.2 Output schema (v0.1.0 — **спрощений**, drop-in compatible)

**`ComponentRegistry.json`** у NuGet pkg — **той самий top-level масив 92 записів**, що зараз лежить у clio. Жодних обгорток типу `{ "schemaVersion": ..., "components": [...] }` — це етап 3 (модель `Availability`) і етап 6 (data-driven categories). `JsonSerializer.Deserialize<ComponentRegistryEntry[]>` у clio працює без змін.

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

`metadata.json` — це фактично `composer.gitSha` + `overrides.appliedCount` + повний copy `origin.json` з composer-repo. Жодної логіки розв'язання на runtime — все determined by content of `data/input/<line>/origin.json`.

### 3.3 Packing JSON як embedded resource

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
    <!-- Файли генеруються composer-runner-ом у obj/composed/ ПЕРЕД dotnet pack -->
    <EmbeddedResource Include="$(MSBuildProjectDirectory)/../../obj/composed/ComponentRegistry.json">
      <LogicalName>Creatio.ComponentRegistry.ComponentRegistry.json</LogicalName>
    </EmbeddedResource>
    <EmbeddedResource Include="$(MSBuildProjectDirectory)/../../obj/composed/metadata.json">
      <LogicalName>Creatio.ComponentRegistry.metadata.json</LogicalName>
    </EmbeddedResource>
  </ItemGroup>
</Project>
```

**Критично:** `LogicalName` фіксує exact resource name. Без нього MSBuild згенерує щось на кшталт `Creatio.ComponentRegistry.._..obj.composed.ComponentRegistry.json` (бо файли поза csproj-папкою).

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

## 4. NuGet-пакет — структура і identification

### 4.1 Метадані

| Поле | Значення |
|---|---|
| `PackageId` | `Creatio.ComponentRegistry` |
| `Version` | `0.1.0` (semver 3-part, без `v` prefix у тегах) |
| Owner на nuget.org | `ATF` (той самий що публікує clio) |
| `Authors` | `ATF` |
| `Company` | `Creatio` |
| `Description` | `Curated Freedom UI component catalog consumed by clio MCP server.` |
| `PackageLicenseExpression` | `MIT` |
| `RepositoryUrl` | `https://github.com/Advance-Technologies-Foundation/creatio-component-registry-composer` |
| `PackageTags` | `creatio;clio;mcp;ai;freedom-ui` |
| `TargetFramework` | `netstandard2.0` (max сумісність з clio `net8.0;net10.0`) |
| Destination feed | `https://api.nuget.org/v3/index.json` (public) |

### 4.2 Структура `.nupkg`

```
Creatio.ComponentRegistry.0.1.0.nupkg
├── _rels/.../...
├── package/services/metadata/...
├── Creatio.ComponentRegistry.nuspec
└── lib/
    └── netstandard2.0/
        └── Creatio.ComponentRegistry.dll  ← містить 2 embedded resources
```

**Local verification після `dotnet pack`:**
```csharp
var asm = Assembly.LoadFrom("Creatio.ComponentRegistry.dll");
foreach (var n in asm.GetManifestResourceNames()) Console.WriteLine(n);
// Очікувано:
// Creatio.ComponentRegistry.ComponentRegistry.json
// Creatio.ComponentRegistry.metadata.json
```

### 4.3 Чому DLL з embedded resource (а не `contentFiles/`)

| Підхід | Pros | Cons |
|---|---|---|
| **embedded resource у DLL** (вибрано) | `Assembly.GetManifestResourceStream` — стандарт; нульова залежність від filesystem; works з `PackAsTool=true` у clio | Потрібен один marker class |
| `contentFiles/any/any/*.json` | Plain file для інспекції | `PackAsTool=true` нестабільно обробляє content-файли; ламає offline-інваріант |

---

## 5. Зміни у clio repo

### 5.1 Файли під зміну

| Файл | Дія |
|---|---|
| `clio/Command/McpServer/Data/ComponentRegistry.json` | **Видалити** |
| `Directory.Packages.props` | Додати `<PackageVersion Include="Creatio.ComponentRegistry" Version="0.1.0" />` |
| `clio/clio.csproj` | Додати `<PackageReference Include="Creatio.ComponentRegistry" />`; видалити `<Content Include="Command\McpServer\Data\**">` block |
| `clio/Command/McpServer/Tools/ComponentInfoCatalog.cs` | Переписати: drop `IFileSystem`/`IWorkingDirectoriesProvider` deps; parameterless ctor + `internal static LoadFromStream(Stream)` factory |
| `clio/Program.cs` (DI registration) | Перевірити, що `ComponentInfoCatalog` реєструється без `IFileSystem` залежності |
| `clio.tests/Command/McpServer/ComponentInfoToolTests.cs` | Hybrid (Variant C): 6 існуючих тестів → `LoadFromStream(MemoryStream)` (hermetic), додати 2-3 smoke-тести для real embedded resource |
| `clio.mcp.e2e/ComponentInfoToolE2ETests.cs` | Жодних змін |
| `nuget.config` | **Жодних змін** — wildcard `*` → nuget.org підхопить `Creatio.ComponentRegistry` |
| `clio/Command/McpServer/AGENTS.md` | Оновити секцію про джерело даних (тепер у NuGet, не in-repo) |

### 5.2 Перевірки інфраструктури clio

- `Directory.Packages.props` має `<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>` → `<PackageReference>` у `clio.csproj` без `Version=` — інваріант **дотримано**.
- `clio.csproj` має `<PackAsTool>true</PackAsTool>` — embedded resources у `Creatio.ComponentRegistry.dll` працюють у tool-режимі (DLL копіюється в `tools/<tfm>/any/`).
- `clio.csproj` уже має `InternalsVisibleTo("clio.tests")` і `InternalsVisibleTo("clio.mcp.e2e")` — `internal static LoadFromStream(Stream)` дешевий, без додаткових змін csproj.

### 5.3 Ескіз нового `ComponentInfoCatalog.cs`

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

    // Public parameterless ctor — для DI (виробничий шлях)
    public ComponentInfoCatalog()
        : this(() => LoadCatalogStateFromEmbeddedResource()) { }

    // Internal ctor — для тестів через LoadFromStream
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

    // Internal для тестів (доступно через InternalsVisibleTo)
    internal static ComponentCatalogState LoadFromStream(Stream stream) {
        ComponentRegistryEntry[]? rawEntries = JsonSerializer.Deserialize<ComponentRegistryEntry[]>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (rawEntries is null || rawEntries.Length == 0) {
            throw new InvalidOperationException("Component registry is empty or invalid.");
        }
        // duplicate detection + ordering + lookup dictionary (1:1 з поточної логіки)
        // ...
        return new ComponentCatalogState(orderedEntries, lookup);
    }

    // Matches / Contains / GetCategorySortKey — без змін з поточного коду
}
```

### 5.4 Перевірка call-sites

Перед PR: `grep -rn "new ComponentInfoCatalog" /Users/a.kravchuk/Projects/clio/clio/ /Users/a.kravchuk/Projects/clio/clio.tests/ /Users/a.kravchuk/Projects/clio/clio.mcp.e2e/`.

DI registration (`services.AddSingleton<IComponentInfoCatalog, ComponentInfoCatalog>()`) лишається без змін — DI авто-resolve-ить parameterless ctor.

---

## 6. CI/CD для composer-repo — GitHub Actions ubuntu-latest

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
          # БЕЗ --skip-duplicate: explicit fail-on-duplicate

      - name: Create git tag
        run: |
          git tag "${PACKAGE_VERSION}"
          git push origin "${PACKAGE_VERSION}"
```

### 6.3 Налаштування секретів composer-repo

- `CLIO_NUGET_API_KEY` — той самий API key (ATF on nuget.org), що публікує clio. Скопіювати у GH Secrets composer-repo.
- `GITHUB_TOKEN` — auto-provisioned, потрібен для `check-origin` (verify clio commit existence через `gh api`).

---

## 7. Послідовність робіт (PR-flow)

### 7.1 Cross-repo dependency

clio не може merge-нути PR з `<PackageReference Include="Creatio.ComponentRegistry" />`, поки `0.1.0` не опубліковано на nuget.org.

### 7.2 Порядок

```
┌─────────────────────────────────────────────────────────────────────┐
│ COMPOSER-REPO BOOTSTRAP                                             │
│ 1. PR #1: layout (sln, csprojs, data/, .github/workflows/, docs/)   │
│    + snapshot ComponentRegistry.json + origin.json з pinned clio SHA│
│ 2. PR #2: composer runtime (Program.cs, Input, Merge, Output)        │
│ 3. PR #3: composer tests + DuplicateDetector + MetadataStamper +    │
│           OriginIntegrityChecker                                     │
└──────────────────────────┬──────────────────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────────────────┐
│ FIRST NuGet PUBLISH                                                 │
│ 4. Manual trigger `release-to-nuget` workflow з PACKAGE_VERSION=0.1.0│
│    - Origin integrity check passes                                  │
│    - Composer tests passes                                          │
│    - dotnet pack + push до api.nuget.org                            │
│    - git tag 0.1.0 створено                                         │
│ 5. ВЕРИФІКАЦІЯ: `dotnet nuget locals all --clear` локально →        │
│    `dotnet add package Creatio.ComponentRegistry --version 0.1.0` → │
│    `Assembly.GetManifestResourceNames()` → 2 resources              │
└──────────────────────────┬──────────────────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────────────────┐
│ CLIO MIGRATION PR                                                   │
│ 6. PR у clio repo, single commit-set:                               │
│    - Directory.Packages.props: + PackageVersion                     │
│    - clio.csproj: + PackageReference, - Content Include             │
│    - ComponentInfoCatalog.cs: переписати loader (Variant C)         │
│    - ComponentInfoToolTests.cs: hermetic LoadFromStream + smoke     │
│    - delete clio/Command/McpServer/Data/ComponentRegistry.json      │
│    - update AGENTS.md                                               │
│ 7. CI clio (build.yml) пройти:                                       │
│    - dotnet restore тягне Creatio.ComponentRegistry@0.1.0 з n.org   │
│    - unit tests зелені                                              │
│    - E2E (ComponentInfoToolE2ETests) зелені — drop-in proof         │
│ 8. Merge clio PR                                                    │
└─────────────────────────────────────────────────────────────────────┘
```

### 7.3 Gotchas

| # | Gotcha | Mitigation |
|---|---|---|
| G1 | clio CI падає бо ще не бачить `Creatio.ComponentRegistry` | Запушити NuGet ДО відкриття clio PR (strict ordering у §7.2). |
| G2 | nuget.org не дозволяє overwrite опублікованої версії — `0.1.0` immutable | Якщо знайшли баг — bump на `0.1.1`. CI-перший пробіг через CI workflow (auto on PR) тестує pack-логіку без push. |
| G3 | `dotnet pack` саккає embedded resources з `obj/composed/`, файл там через runtime composer-step. `obj/` typically gitignored — checkout не має цих файлів | OK: composer-runner запускається на тому ж checkout ПЕРЕД `dotnet pack`. `.gitignore` ігнорує `obj/` як category, але MSBuild створює `obj/composed/` content вже після checkout. |
| G4 | `LogicalName` ≠ constant у clio coordinator | `RegistryResourceName` constant у `ComponentInfoCatalog.cs` + test у Composer.Tests, що build-ить pack і інспектує assembly. |
| G5 | netstandard2.0 vs net8/net10 — NuGet warnings | `netstandard2.0` універсально consume-иться з net8+. Жодних warnings очікувано. |
| G6 | DI: змінений ctor `ComponentInfoCatalog()` ламає explicit `new ComponentInfoCatalog(fs, wd)` | `grep -rn "new ComponentInfoCatalog"` перед PR. |
| G7 | Snapshot input у composer-repo і файл у clio до merge clio-PR — два джерела | Mitigation: composer bootstrap PR одразу робиться; clio-PR з міграцією заплановано як наступний крок. Window короткий. |

---

## 8. Тестова стратегія — Variant C (Hybrid)

### 8.1 Юніт-тести у clio.tests (hermetic)

6 існуючих тестів конвертуємо до `LoadFromStream` (через `internal` factory):

```csharp
private static ComponentInfoTool CreateTool() {
    using var stream = new MemoryStream(Encoding.UTF8.GetBytes(TestRegistryJson));
    var state = ComponentInfoCatalog.LoadFromStream(stream);
    var catalog = new ComponentInfoCatalog(() => state);  // internal ctor
    return new ComponentInfoTool(catalog);
}
```

**Що зберігається:**
- 100% поточних assertions (Count == 6, exact crt.Gallery bulkActions, тощо)
- `MockFileSystem` + `WorkingDirectoriesProvider` зникають
- Tests deterministic + hermetic

### 8.2 Smoke-тести (real embedded resource)

2-3 нові тести у тому ж suite:

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

Smoke-тести катастрофічно ловлять `LogicalName` mismatch (loader падав би at startup).

### 8.3 E2E у clio.mcp.e2e

`ComponentInfoToolE2ETests.cs` залишається без змін — стартує real clio process, читає real embedded resource. Це є наш **drop-in regression detector** (Variant D з Q13 — обраний як заміна snapshot-baseline test).

### 8.4 Composer-tests у composer-repo

| Test | Перевіряє |
|---|---|
| `LocalJsonInputSource_Should_Parse_Manual_Snapshot` | Парсинг 92 записів |
| `OriginIntegrityChecker_Should_Throw_When_Hash_Mismatches` | Захист від stale snapshot |
| `OriginIntegrityChecker_Should_Validate_Commit_Exists` | gh api commit lookup |
| `DuplicateDetector_Should_Throw_On_Duplicates` | Mirror інваріанту clio |
| `OverridesApplier_NoOp_When_Overrides_Empty` | v0.1.0 base case |
| `OverridesApplier_Hides_Components_Listed_In_AiHidden` | Готовність до 0.2.0 |
| `MetadataStamper_Should_Embed_Origin_Json_Verbatim` | Provenance |
| `BundleWriter_Should_Produce_Top_Level_Array_Compatible_With_Clio` | drop-in compatibility |
| `Packed_Assembly_Should_Expose_Two_Embedded_Resources` | Build pack локально, інспектувати |

### 8.5 Acceptance criteria

- [ ] `Creatio.ComponentRegistry@0.1.0` опубліковано на api.nuget.org під ATF
- [ ] `assembly.GetManifestResourceNames()` повертає рівно 2 imena
- [ ] `JsonSerializer.Deserialize<ComponentRegistryEntry[]>(stream)` повертає 92 entries
- [ ] clio build на CI зелений з `<PackageReference Include="Creatio.ComponentRegistry" />`
- [ ] `clio/Command/McpServer/Data/ComponentRegistry.json` видалений з git
- [ ] `ComponentInfoToolTests` зелені (hermetic Variant C + 3 smoke-тести)
- [ ] `ComponentInfoToolE2ETests` зелені (drop-in proof)
- [ ] git tag `0.1.0` у composer-repo створено
- [ ] `metadata.json` усередині пакета містить composer SHA + точну copy `origin.json`

---

## 9. Out-of-scope (явні нагадування)

Не входить у цю частину:
- Жодних змін у моделі `ComponentRegistryEntry`/`ComponentPropertyDefinition` (поля `Availability`, top-level wrapper — це етап 3 і 6)
- Жодних змін у `ComponentInfoArgs`/`ComponentInfoResponse` (нові args — це етап 5)
- Жодного `IPlatformVersionResolver`, жодного cliogate-integration (етап 4)
- Жодного auto-bump, cron-trigger у composer (етап 9)
- Жодного NPM `@creatio/component-registry` integration (етап 7)
- Жодних змін у creatio-ui repo
- Hardcoded `CategoryOrder` залишається (data-driven — етап 6)

---

## Critical Files for Implementation

- [clio/Command/McpServer/Tools/ComponentInfoCatalog.cs](../clio/Command/McpServer/Tools/ComponentInfoCatalog.cs) — переписаний loader
- [clio/Command/McpServer/Data/ComponentRegistry.json](../clio/Command/McpServer/Data/ComponentRegistry.json) — джерело snapshot-у (потім видаляється)
- [Directory.Packages.props](../Directory.Packages.props) — `<PackageVersion …/>` додати
- [clio/clio.csproj](../clio/clio.csproj) — `<PackageReference …/>` додати, `<Content Include…>` видалити
- [clio.tests/Command/McpServer/ComponentInfoToolTests.cs](../clio.tests/Command/McpServer/ComponentInfoToolTests.cs) — Variant C rewrite

---

## Decisions log

Всі open questions закриті в Q&A із власником плану. Підсумок:

| # | Питання | Рішення |
|---|---|---|
| 1 | Технологія composer-а | **.NET 8 console app** (C#, NUnit). Той самий toolchain що clio → нульовий serialization-drift. |
| 2 | `originCommit` provenance | **Sidecar `data/input/<line>/origin.json`** із pinned commit SHA + sha256. CI-check `OriginIntegrityChecker`: sha256(snapshot) ≡ origin.json.sha256 і commit existsAt clio repo. |
| 3 | License | **MIT** для всього composer-repo (consistency з clio). |
| 4 | Hosting composer-repo | **Public GitHub** `Advance-Technologies-Foundation/creatio-component-registry-composer` (consistency з clio екосистемою). |
| 5 | Test strategy для `ComponentInfoCatalog` | **Variant C (Hybrid):** 6 existing tests → hermetic `LoadFromStream`; +3 smoke-тести проти real embedded resource. |
| 6 | NuGet API key | **Reuse `CLIO_NUGET_API_KEY`** (той самий ATF account на nuget.org). Bus-factor можна адресувати пізніше. |
| 7 | Хто створює composer-repo | **Створено** користувачем self-service. Repo empty, ready for first PR. |
| ~~8~~ | ~~Jenkins folder~~ | **N/A** — composer-repo public GitHub → GitHub Actions, не Jenkins. |
| 9 | CI runner | **`ubuntu-latest`** (GH-hosted, free для public repo). nuget.org reachable з public internet (HTTP 403 = auth required, не network block). |
| 10 | `--skip-duplicate` flag | **Без flag** (consistency з clio release.yml). Fail-on-duplicate = explicit signal «bump потрібен». |
| 11.a | Version format | **3-part semver** `0.1.0` (відповідає research MAJOR.MINOR.PATCH rules). |
| 11.b | Tag prefix | **Без `v`** (consistency з clio convention `1.0.0.1`, `2.0.0.1`, …). |
| 12 | Constructor strategy | **Parameterless public ctor** для DI + **internal static `LoadFromStream(Stream)` factory** для hermetic тестів. `InternalsVisibleTo("clio.tests")` уже налаштовано. |
| 13 | Drop-in regression test | **Variant D** — без snapshot baseline test; покладаємось на existing `ComponentInfoToolE2ETests` як regression detector. |

### Архітектурні uplifts (з'явилися в Q&A):

- **Final NuGet destination — public `api.nuget.org`, не Terrasoft Nexus.** clio публікується на nuget.org під ATF owner; `Creatio.ComponentRegistry` слідує тому самому patern-у. Symmetric public ecosystem.
- **Steady-state тригер — GA-tag у creatio-ui, не branch-cut.** Branch-cut виробляє baseline artifact для регресій extractor-а; реальний publish тригериться по GA-tag (`8.3.0`, `8.3.1`, …).
- **Composer має own CI у composer-repo (GitHub Actions),** а не запускається з creatio-ui Jenkins. Це consciously розрив за ownership (Platform-UI ≠ AI/clio team).
- **clio bump через Renovate/Dependabot auto-PR**, не direct commit від composer pipeline. Review-gating preserve-ється.
- **`overrides.json` і `supported-versions.json` живуть у composer-repo** (AI/clio team-owned), не у creatio-ui Jenkins.
