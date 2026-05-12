# Source of truth: automatic registry with three isolated domains

The component catalog is a data product. Its evolution (appearance of components and properties, deprecation) must happen automatically, without manual JSON editing in clio. This document describes the target architecture with three isolated domains: **creatio-ui** (source), **composer** (integration), **clio** (consumer).

## Architectural decisions (locked in)

1. **Source of truth — TS decorators + ViewConfig + JSDoc in creatio-ui.** No manual JSON on the platform side.
2. **The composer lives in a separate repo** `creatio-component-registry-composer`. Not in clio (mixes consumer and integration), not in creatio-ui (composer ≠ UI source).
3. **Distribution UI → composer**: NPM package `@creatio/component-registry` (semver = platform version, for example `8.3.0`).
4. **Distribution composer → clio**: NuGet package `Creatio.ComponentRegistry` (independent semver — see the policy below).
5. **clio consumes via `PackageReference`**, registry data is never committed to the clio repo.
6. **Runtime serving — embedded resource in the NuGet pkg**, read via `Assembly.GetManifestResourceStream`. No network call at runtime.
7. **Sharded authoring in NPM** (per-component JSON), **unified bundle in NuGet**. The composer collapses sharding during merge.
8. **`supported-versions.json` and `overrides.json` — in composer-repo**, not in clio and not in creatio-ui.

## Target architecture

```
                  ┌──────────── creatio-ui ────────────┐
                  │   @CrtViewElement + *ViewConfig    │
                  │   + JSDoc (@since, @aiCategory,    │
                  │           @aiHint, @deprecated)    │
                  │             │                      │
                  │   AST extractor (Jenkins on        │
                  │   branch-cut / push / GA-tag)      │
                  └─────────────┼──────────────────────┘
                                │ npm publish on GA-tag only
                                ▼
              ┌─────── @creatio/component-registry ─────┐
              │   per-version JSON snapshots            │
              │   (sharded internally: one file per     │
              │    component, for review)               │
              └─────────────┼───────────────────────────┘
                            │ npm install (composer CI)
                            ▼
   ┌────── creatio-component-registry-composer ────────┐
   │   supported-versions.json (manual PR)             │
   │   overrides.json (AI team owned)                  │
   │   ──────────────────────                          │
   │   1. pull NPM snapshots for supported versions    │
   │   2. diff snapshots → availability ranges         │
   │   3. merge overrides                              │
   │   4. collapse sharding → single bundle            │
   │   5. stamp metadata.json (provenance)             │
   │   6. dotnet pack + nuget push                     │
   └─────────────┼─────────────────────────────────────┘
                 │ NuGet publish (independent semver)
                 ▼
       ┌─── Creatio.ComponentRegistry NuGet pkg ───┐
       │   ComponentRegistry.json (unified)        │
       │   metadata.json (provenance)              │
       │   $schema reference                       │
       └─────────────┼─────────────────────────────┘
                     │ <PackageReference> in Directory.Packages.props
                     ▼
           ┌─────── clio.csproj ────────┐
           │   ComponentInfoCatalog     │
           │     (reads embedded JSON   │
           │      via Assembly.         │
           │      GetManifestResource)  │
           │   IPlatformVersionResolver │
           │   MCP get-component-info   │
           └────────────────────────────┘
```

## Ownership matrix

| Repo | Owner | Source of truth for |
|---|---|---|
| **creatio-ui** | Platform-UI team | `@CrtViewElement` decorators, `*ViewConfig` interfaces, JSDoc metadata (`@since`, `@deprecated`, `@aiCategory`, `@aiHint`) |
| **creatio-component-registry-composer** | AI / clio team | `supported-versions.json`, `overrides.json`, composer merge logic, NuGet publish |
| **clio** | clio team | `ComponentInfoCatalog` loader, `IPlatformVersionResolver`, MCP tools, guidance resources |

None of the three has write access to another beyond the standard PR flow. NPM and NuGet are **transport contracts**, not shared mutable state.

## Versioning `Creatio.ComponentRegistry` (NuGet)

Independent semver, decoupled from both (clio version, platform version). Bump rules:

- **MAJOR** — breaking schema change. For example, the `availability` format changed, the `componentType` field was renamed, top-level JSON structure is not back-compat. The consumer (clio) needs a coordinated update.
- **MINOR** — a new supported platform line, new components, new properties, new categories. Backwards-compatible — old clio reads without modifications.
- **PATCH** — updates to descriptions, defaults, examples, AI hints. Pure data refresh.

This gives dependabot the right behavior: PATCH auto-merge, MINOR with a review checklist, MAJOR — a blocking PR with a migration plan on the clio side.

## Schema versioning and metadata inside the package

The NuGet pkg contains two files:

**`ComponentRegistry.json`** (data) — top-level:

```json
{
  "$schema": "https://schema.creatio.com/component-registry/v1.json",
  "schemaVersion": "1.0",
  "latestKnownVersion": "8.3.2",
  "categories": [
    { "id": "containers", "order": 0, "label": "Containers" },
    ...
  ],
  "components": [ ... ]
}
```

`schemaVersion` is independent from `registryVersion` (the NuGet pkg version). The consumer only checks schema compatibility — an additional safeguard against incompatible changes.

**`metadata.json`** (provenance):

```json
{
  "registryVersion": "1.5.0",
  "schemaVersion": "1.0",
  "buildTime": "2026-05-12T10:00:00Z",
  "supportedPlatformVersions": ["8.0.x", "8.1.x", "8.2.x", "8.3.x"],
  "latestKnownVersion": "8.3.2",
  "sources": {
    "creatioUi": {
      "8.0.x": { "npmVersion": "8.0.18", "gitSha": "abc..." },
      "8.1.x": { "npmVersion": "8.1.7",  "gitSha": "def..." },
      "8.2.x": { "npmVersion": "8.2.3",  "gitSha": "ghi..." },
      "8.3.x": { "npmVersion": "8.3.2",  "gitSha": "jkl..." }
    },
    "composer": { "gitSha": "mno...", "version": "2.4.1" },
    "overrides": { "gitSha": "mno...", "appliedCount": 17 }
  }
}
```

This closes the diagnostics loop. To the question "why in clio `1.10` with `Creatio.ComponentRegistry 1.5.0` does the AI not see `crt.NewWidget`, which exists in the creatio-ui 8.3 branch?" — we open metadata and see that `1.5.0` was built before `8.3.x` made it into `supported-versions.json`.

## Sources in creatio-ui

Extracted from [extractor-analysis.md](extractor-analysis.md) and repo research:

| Source | What it provides | Quality |
|---|---|---|
| `@CrtViewElement({ type: 'crt.X' })` decorator | Canonical component name | **High** — AST-extractable |
| `*ViewConfig` TS interface (e.g. `ButtonViewConfig`) | Property contract with types | **High** — TS-typed, JSDoc-friendly |
| `@CrtInterfaceDesignerItem` decorator | Defaults (`defaultPropertyValues`), `typeCaption`, `viewElementGroupType` | **High** — designer metadata |
| `api-extractor` (`docModel`) | JSON rollup of public API | **Medium** — does not see decorator content |
| Runtime registry (`BaseViewElementRegistry`) | Ground truth after bootstrap | **High**, but hard in CI |

**Inclusion criterion:** a class with the `@CrtViewElement` decorator. No other heuristics.

**Exclusion filters:**

| Category | Glob |
|---|---|
| Test files | `**/*.spec.ts`, `**/*.spec.ui.ts`, `**/*.spec.tsx`, `**/*.test.ts`, `**/*.test.tsx` |
| Mocks | `**/*.mock.ts`, `**/mocks/**`, `**/__mocks__/**` |
| Built artifacts | `apps/pkgs/**` |
| Designer-only UI (lib) | `libs/studio-enterprise/ui/interface-designer-properties-panel/**` |
| Designer-only UI (subpath) | `**/designtime/**` |
| Standard | `**/node_modules/**`, `dist/**` |

`**/designtime/**` is an exact invariant: 44/44 `*PropertiesPanel` in `designtime/`, 0/192 non-PropertiesPanel in `designtime/`. Verified against `creatio-ui@master`.

**Critical:** the parser ignores the `@CrtViewElement` decorator inside JSDoc comments (otherwise it would pick up `usr.Example` from examples in decorator-definition files). Implementation — ts-morph node-level decorator API, not a text search.

## Extractor pipeline (NPM package authoring)

1. **AST walk** of all `*.ts` files with the filters applied.
2. For each class with `@CrtViewElement`:
   - `componentType` = `decorator.arguments[0].type` (string literal).
   - **NO** semantic filtering by suffixes. `*PropertiesPanel`, `*Request`-named are included — their hiding is delegated to the composer's `overrides.json` (`aiHidden: true`).
3. Resolve the related `ViewConfig` interface (convention: `<ComponentName>ViewConfig` in `view-models/`).
4. For each property in ViewConfig:
   - `type` — text representation of the TS type
   - `description` — JSDoc `@description` or leading comment
   - `required` — absence of the `?` modifier
   - `values` — for union types of string literals
   - `default` — from `@CrtInterfaceDesignerItem.defaultPropertyValues[name]`
   - `availability` — from JSDoc `@since`/`@deprecated`
5. `category` — JSDoc `@aiCategory` overrides; otherwise — mapping via `viewElementGroupType`.
6. **Validation step**: compare the AST-extracted set with the `BaseViewElementRegistry` runtime (optional job). Discrepancies → fail.
7. **Output:** sharded JSON in the NPM package — one file per component (`components/crt.Button.json`, `crt.Input.json`, …) + `manifest.json` with common metadata.

## JSDoc vocabulary in creatio-ui

```typescript
/**
 * @aiCategory interactive
 * @aiHint "Use crt.ButtonToggleGroup for segmented selection"
 */
@CrtViewElement({ type: 'crt.Button', ... })
export class CrtButtonComponent { ... }

export interface ButtonViewConfig extends ... {
    /** Button caption. */
    caption?: string;

    /**
     * Icon placement.
     * @since 8.1.0
     */
    iconPosition?: IconPositionEnum;

    /**
     * @deprecated 8.2.0 - replaced by crt.ButtonToggleGroup
     */
    legacyStyleMode?: string;
}
```

Vocabulary (recommended for the lint rule):

- `@since <version>` → `availability.since`
- `@deprecated <version> - <reason>` → `availability.until` + the description is suffixed with the reason
- `@aiCategory <name>` → category (containers/fields/interactive/display/filtering)
- `@aiHint "..."` → inline hint for AI

`@aiHidden` / `@aiInclude` are deliberately **removed** from the vocabulary: the inclusion criterion is only `@CrtViewElement`, noisy components are hidden by the composer's `overrides.json`. Separation of concerns: the platform reports everything; the AI team decides what to show.

## Composer logic

Repo: `creatio-component-registry-composer`. Files under version control:

- `supported-versions.json` — the list of minor lines that the composer takes into account:
  ```json
  ["8.0.x", "8.1.x", "8.2.x", "8.3.x"]
  ```
- `overrides.json` — AI-specific rules:
  ```json
  {
    "aiHidden": ["crt.TableBooleanCell", "crt.RouterOutlet", "crt.ModuleLoader"],
    "aiOverlay": {
      "crt.EmailInput": {
        "aiHint": "For display-only email rendering prefer crt.ToEmailLink converter."
      }
    }
  }
  ```
- `composer.config.ts` — runtime config (NuGet feed URL, npm registry URL, kerb auth).

**CI run (Jenkins in composer-repo):**

1. Trigger: cron (daily), webhook on npm `@creatio/component-registry` publish, or manual.
2. Pull `@creatio/component-registry@<v>` for all versions from `supported-versions.json` (latest patch in each minor line).
3. Assemble the unified bundle:
   - for each `componentType`: if present in `vN`, absent in `vN-1` → `availability.since = vN`; if was in `vN-1`, disappeared in `vN` → `availability.until = vN`. The same at the property level.
4. Apply `overrides.json`:
   - `aiHidden` entries are excluded entirely.
   - `aiOverlay` is merged on top of auto-data (overlay wins for descriptive fields).
   - Conflict detection: if `aiOverlay` references a `componentType` that does not exist in any of the supported versions — the composer fails with an error (protection against stale overrides).
5. Stamp `metadata.json` with provenance (NPM versions, git SHAs).
6. `dotnet pack` + `dotnet nuget push` to the internal NuGet feed.
7. Bump version per semver rule:
   - detect breaking schema changes → MAJOR;
   - detect new components / properties / supported version → MINOR;
   - otherwise → PATCH.
8. Push the git tag `v<X.Y.Z>` in composer-repo for traceability.

## Failure-mode design

Architectural invariant: **each layer degrades independently; the consumer does not depend on the network at runtime.**

| Scenario | Behavior |
|---|---|
| NPM creatio-ui unavailable | The composer does not build a new version → clio holds the previously pinned NuGet version → AI sees the last stable registry |
| Composer CI broken | The NuGet feed has previous versions → clio works; dependabot temporarily does not bump |
| Internal NuGet feed unavailable | clio build fails; local NuGet cache saves most dev cases; existing clio installs unaffected |
| Composer released a bad NuGet | Pin the previous version in `Directory.Packages.props` (1 line) → new clio build → instant rollback |
| AI client offline | `Creatio.ComponentRegistry` embedded in the clio binary via NuGet content → 0 runtime network dependency |
| Schema breaking change | MAJOR bump → clio CI fails on the old loader → coordinated migration PR |

No scenario touches the AI runtime UX — this is the architectural strength of the NuGet option versus cloud-fetch.

## Integration into clio

### `Directory.Packages.props`

```xml
<Project>
  <ItemGroup>
    <PackageVersion Include="Creatio.ComponentRegistry" Version="1.5.0" />
    ...
  </ItemGroup>
</Project>
```

### `clio.csproj`

```xml
<ItemGroup>
  <PackageReference Include="Creatio.ComponentRegistry" />
</ItemGroup>
```

### `ComponentInfoCatalog` loader

Replacement of the current implementation:

```csharp
// Current:
// string registryPath = Path.Combine(executingDirectory, "Command", "McpServer", "Data", "ComponentRegistry.json");

// Target:
using var stream = typeof(ComponentRegistryEntry).Assembly
    .GetManifestResourceStream("Creatio.ComponentRegistry.ComponentRegistry.json")
    ?? throw new InvalidOperationException("Embedded registry resource not found.");
```

The file [clio/Command/McpServer/Data/ComponentRegistry.json](../clio/Command/McpServer/Data/ComponentRegistry.json) **is removed** from the clio repo. The loader reads from the NuGet-bundled embedded resource.

## Release branch lifecycle in creatio-ui

The base `push to a release branch` trigger works for **already existing** lines. Separately — the event "creatio-ui cuts a new branch for an upcoming release" (`master` forks into `8.3.x`).

**Policy:** there are no prerelease publications. Internal extractor builds on a release branch up to the GA tag are not published to npm — only local Jenkins artifacts.

```
master (8.4 dev) ─┬─ cut → 8.3.x branch created
                  │     │
                  │     ├─ Jenkins on-branch-create (Jenkinsfile.Registry)
                  │     │    ├─ extract @ branch HEAD
                  │     │    ├─ baseline component-registry.json as Jenkins artifact
                  │     │    │   (inspection + regression detector; NOT an npm publish)
                  │     │    └─ diff vs previous-line GA snapshot → preview report
                  │     │
                  │     └─ ongoing commits → re-extract, refresh artifact
                  │          (also without npm publish)
                  │
                  └─ tag 8.3.0 (GA) ─ extract → npm publish @creatio/component-registry@8.3.0
                                            └─ composer-CI cron picks up next run
                                                  └─ manual PR in composer-repo:
                                                      supported-versions.json += "8.3.x"
                                                        └─ composer rebuilds → NuGet bump
                                                            └─ clio dependabot bump
```

Stages:

1. **Branch cut in creatio-ui.** Jenkins runs `Jenkinsfile.Registry` in baseline mode: extract → Jenkins artifact + preview diff vs the latest GA publication of the previous line. **No npm publish.** This catches extractor regressions and inconsistencies BEFORE GA.

2. **Ongoing commits on the release branch up to GA.** The same pipeline re-runs and refreshes the artifact. The composer is not touched.

3. **GA tag.** `npm publish @creatio/component-registry@8.3.0`. The npm dist-tag `latest` is updated only if this is the max version.

4. **Registration in composer-repo.** Manual PR in `supported-versions.json` (a conscious team decision to support the new line). An auto-PR from an npm-monitoring bot is an optional accelerator.

5. **Composer run.** Rebuilds the unified registry, bumps NuGet semver.

6. **clio bump.** Dependabot/Renovate creates a PR with a bumped `<PackageVersion Include="Creatio.ComponentRegistry" Version="x.y.z" />`. Review checklist according to the semver bump level.

7. **Branch retirement.** PR in composer-repo that removes the line from `supported-versions.json`. The composer:
   - if a component/property is absent in the remaining lines — `availability.until` receives the value of the first unsupported version;
   - if present in all of them — no changes.

## Previously open questions, now closed

| # | Question | Decision |
|---|---|---|
| 1 | Owner of `tools/component-registry-extractor/` | Platform-UI team (in the creatio-ui repo) |
| 2 | JSDoc vocabulary as a standard | Yes, with an eslint rule making it mandatory when adding a new `@CrtViewElement` |
| 3 | Who owns `overrides.json` | AI / clio team in composer-repo |
| 4 | Version policy of what is served to AI | `latest patch` of each supported minor line; AI sees the unified registry |
| 5 | Internal npm registry | The `docker-rnd.creatio.com` ecosystem (existing) |
| 6 | Backfill v8.0/v8.1 | Forward-only from the first line that will be in `supported-versions.json` at launch time |
| 7 | `composable-apps/*` | A separate extractor pass with the tag `scope: "app"` in the record (out of scope for v1) |
| 8 | Properties panels | Excluded by the `**/designtime/**` filter at the extractor level |
| 9 | Branch-creation hook in Jenkins | If absent — a nightly diff job (`git branch -r` vs cached) |
| 10 | npm dist-tag policy | `latest` = max version + per-line tag (`8.2`, `8.3`) for backwards-compat consumer pinning |
| 11 | Cadence of GA tags | Per build (`8.3.0`, `8.3.1`, …); the composer cron picks them up in the next cycle |
| 12 | Registration of support | Manual PR in `supported-versions.json` (default); an auto-PR bot is optional |

## Still to close (lower level)

- **MAJOR-bump migration path.** If schema v2 appears, how does clio support both versions simultaneously? Multi-target reader; an adapter in `ComponentInfoCatalog` — a separate design issue.
- **Overrides versioning.** Does `overrides.json` also have semver or does it live as part of the composer git history? The former is cleaner, the latter is simpler.
- **Telemetry contract.** Does the composer record use-frequency in NuGet metadata in order to back-port popular overrides into `creatio-ui` as JSDoc? A strong optimization, but it requires telemetry from MCP servers — a separate design issue.
- **Composition of `composable-apps/*` components.** How to scope them in the unified registry — a separate design issue for v2.

## Why NuGet, not in-repo JSON

Alternatives for final storage were considered — a single embedded JSON committed in the clio repo, per-component sharded in clio, SQLite, cloud-fetch. Comparison details are in git history (PR #595, deleted file `06-storage-distribution-analysis.md`).

The NuGet choice solves 4 specific problems that an in-repo JSON commit drags in:

1. **Auto-PR noise.** The composer bot makes a commit of a 500KB–1MB registry diff on every run. PR review of this diff is useless (a huge auto-generation).
2. **Implicit versioning.** clio v1.10 and v1.11 with different registry states can be identified only by git-blame. NuGet semver gives an explicit pin.
3. **Dependabot-blind.** Bot commits are not monitored by dependabot/Renovate. NuGet PackageReference is the standard.
4. **Parallel-PR collisions.** Code-PR and registry-bump-PR constantly collide in the same file. The NuGet pin lives in `Directory.Packages.props`, code — separately.

Price: one additional step in the composer pipeline (`dotnet pack` + `dotnet nuget push`) and one additional configured dependency in clio. Loss of visual JSON inspection in the IDE is mitigated by publishing a human-readable artifact in Jenkins / release notes.

## What NOT to do (anti-patterns)

- **Do not commit composed JSON into the clio repo.** We lose explicit versioning, dependabot, parallel-PR ergonomics. Final storage = NuGet pkg, not a git-tracked file.
- **Do not parse `*.api.md` as JSON.** The markdown rollup is intended for review, not for parsing.
- **Do not store per-version snapshot files in the clio repo.** Supporting 10 versions = 10× duplication. Instead — NPM dependencies in the composer and a unified bundle in NuGet.
- **Do not do decorator extraction via regex.** AST walk via `ts-morph`.
- **Do not block the creatio-ui release on the extractor.** Build the registry in a separate job that fails isolated.
- **Do not commit registry data with composer write access into clio.** The composer writes only to the NuGet feed. clio picks up via PackageReference.

## Infrastructure artifacts that can be reused

- **api-extractor configs** in `libs/devkit/{common,base,interface-designer}` — `apiReport.enabled: true`. Can be used as a secondary validation source.
- **Nx monorepo** in creatio-ui — `nx.json`, `project.json` in each lib — the standard path for an extractor task.
- **Jenkins pipeline-library** — the `@Library('pipeline-library')` pattern is already in use.
- **`docker-rnd.creatio.com`** — internal ecosystem for publishing artifacts (NPM + NuGet feeds).
- **dependabot/Renovate** — the standard tool for bumping NuGet dependencies in clio.
