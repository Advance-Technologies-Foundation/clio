# Source of truth: CI-driven extractor publishing through a GitLab repository mirrored to the CDN

The component catalog is a data product. Its evolution (appearance of components and properties, deprecation) must happen automatically, without manual JSON editing in clio. This document describes the **target architecture**: **creatio-ui** (source + CI; Jenkins job is planned) generates the per-version JSON files and `git push`es them into the **static-files-mcp** GitLab repository (`gitdigital.creatio.com/academy/static-files-mcp`). The **academy** infrastructure mirrors that repository to the public CDN tree at `academy.creatio.com/api/mcp/` every 5 minutes. **clio** is the pure consumer side — it owns the version resolver, the file cache, and the multi-layer fallback.

The CDN at academy.creatio.com is the transport contract. The GitLab repository is the storage layer that decouples the producer (Jenkins) from the academy edge.

> **Note on prior iteration.** An earlier version of this research (PR [clio#595](https://github.com/Advance-Technologies-Foundation/clio/pull/595)) proposed a three-domain NuGet-based delivery via a dedicated composer repo. After architectural review with the team that approach was abandoned in favor of the CDN model described here. The composer repo and the corresponding `Creatio.ComponentRegistry@0.1.0` NuGet package are obsolete and will be removed.

## Architectural decisions (locked in)

1. **Source of truth — TS decorators + ViewConfig + JSDoc in creatio-ui.** No manual JSON on the platform side.
2. **The extractor runs in `creatio-ui` Jenkins (planned)** at GA-tag time (`8.2.0`, `8.2.1`, `8.3.0`, …). The extracted JSON is committed to the **static-files-mcp** GitLab repository (`gitdigital.creatio.com/academy/static-files-mcp`) by the same job. Until the Jenkins automation lands, files are added to that repository manually. No separate composer repo, no second CI stage.
3. **Storage and distribution split.** The `static-files-mcp` GitLab repository is the storage layer; the academy infrastructure mirrors it to the public CDN tree at `academy.creatio.com/api/mcp/` every 5 minutes. URL pattern on the CDN: `https://academy.creatio.com/api/mcp/{version}/ComponentRegistry.json`. A `latest/ComponentRegistry.json` alias points to the freshest GA. No authentication.
4. **Per-version files, not per-entry availability.** Each GA-tag publishes a self-contained JSON file. clio selects the file matching the platform version of the target environment; it does not filter records inside a file.
5. **JSON shape: drop-in compatible with the current in-repo `ComponentRegistry.json`** — top-level array of `ComponentRegistryEntry` objects. No wrapper, no `schemaVersion`, no `categories` block in v1.
6. **No AI-side overrides in v1.** The CI emits the full extracted set. AI-team curation is a possible future stage.
7. **clio consumes via HTTP**, with a three-layer fallback chain: CDN → file cache (`~/.clio/cache/component-registry/`) → embedded snapshot in `clio.dll`. The embedded snapshot is regenerated at clio build time by an MSBuild target that fetches `latest/ComponentRegistry.json` from the CDN.
8. **Cache policy: TTL 24h, stale-while-revalidate.** AI requests never block on the network — expired cache is returned synchronously while a background refresh runs.

## Target architecture

```
            ┌──────────── creatio-ui monorepo ────────────┐
            │   @CrtViewElement + *ViewConfig             │
            │   + JSDoc (@since, @aiCategory, @aiHint,    │
            │            @deprecated)                     │
            │                                             │
            │   Jenkinsfile.ComponentRegistry (planned)   │
            │     trigger: GA-tag (8.2.0, 8.2.1, ...)     │
            │     - AST walk (ts-morph)                   │
            │     - emit ComponentRegistry.json           │
            │     - git push to static-files-mcp          │
            │                                             │
            │   Branch-cut: same job in baseline-only     │
            │     mode → Jenkins artifact + diff report,  │
            │     NO git push                             │
            └─────────────┬───────────────────────────────┘
                          │ git push (Jenkins — planned;
                          │  manual until automation lands)
                          ▼
   ┌─── GitLab: gitdigital.creatio.com/academy/static-files-mcp ─┐
   │     8.2.0/ComponentRegistry.json                            │
   │     8.2.1/ComponentRegistry.json                            │
   │     8.3.0/ComponentRegistry.json                            │
   │     latest/ComponentRegistry.json (alias to freshest GA)    │
   │                                                             │
   │     Each file: top-level JSON array of                      │
   │                ComponentRegistryEntry (drop-in              │
   │                compatible with the current clio shape)      │
   └─────────────┬───────────────────────────────────────────────┘
                 │ academy infrastructure mirrors the
                 │  repository to the CDN tree every 5 minutes
                 ▼
   ┌─── https://academy.creatio.com/api/mcp/ ──────────────────┐
   │     8.2.0/ComponentRegistry.json                          │
   │     8.2.1/ComponentRegistry.json                          │
   │     8.3.0/ComponentRegistry.json                          │
   │     latest/ComponentRegistry.json (alias to freshest GA)  │
   └─────────────┬─────────────────────────────────────────────┘
                 │ HTTPS GET, no auth, 24h TTL,
                 │ stale-while-revalidate
                 ▼
         ┌─────────── clio ────────────┐
         │                              │
         │  Fallback chain on resolve:  │
         │    1. CDN GET                │
         │    2. ~/.clio/cache/...      │  ← stale-while-revalidate
         │    3. embedded snapshot      │  ← built into clio.dll via
         │                              │     MSBuild ResolveCdnSnapshot
         │                              │     target at `dotnet pack` time
         │                              │     (committed seed snapshot for
         │                              │     bootstrap and offline build)
         │                              │
         │  ComponentInfoCatalog        │
         │  IPlatformVersionResolver    │
         │    (cliogate GetSysInfo)     │
         │  get-component-info MCP tool │
         │    - Args unchanged          │
         │    - Response adds:          │
         │        resolvedTargetVersion │
         │        resolvedFrom          │
         └──────────────────────────────┘
```

## Ownership matrix

| Repo / system | Owner | Source of truth for |
|---|---|---|
| **creatio-ui** | Platform-UI team | `@CrtViewElement` decorators, `*ViewConfig` interfaces, JSDoc metadata, the Jenkins extractor (planned), the git push step into `static-files-mcp` |
| **`static-files-mcp` GitLab repo** (`gitdigital.creatio.com/academy/static-files-mcp`) | Academy team (write access shared with the producer job) | Storage of the per-version JSON files; review/history of every published payload via git |
| **academy.creatio.com** infra | Academy team | The 5-minute mirror job that materialises the repository as the public CDN tree under `/api/mcp/`; uptime, cache headers, TLS |
| **clio** | clio team | HTTP client, file cache, build-time-embedded snapshot, `IPlatformVersionResolver`, `ComponentInfoCatalog`, MCP tools, guidance resources |

The CDN is **not a domain** in the architectural sense — it is a transport contract. The contract (URL pattern, JSON shape, freshness expectations) is described in [jenkins-pipeline-spec.md](jenkins-pipeline-spec.md), which now also covers the GitLab repository layout and the mirror cadence.

## JSON shape (v1)

The CDN serves a flat top-level array — byte-for-byte compatible with the current [clio/Command/McpServer/Data/ComponentRegistry.json](../clio/Command/McpServer/Data/ComponentRegistry.json):

```json
[
  {
    "componentType": "crt.Button",
    "category": "interactive",
    "description": "Clickable action element.",
    "container": false,
    "properties": {
      "type":     { "type": "string", "description": "Must be crt.Button." },
      "caption":  { "type": "string", "description": "Button caption." }
    },
    "typicalChildren": [],
    "example": { /* ... */ }
  },
  ...
]
```

No `availability` fields per entry. No top-level wrapper. The consumer (`ComponentRegistryEntry` POCO in clio) is unchanged from the existing shape — pure drop-in.

**Why no per-entry `availability`:** version selection happens at the **file level** on the CDN (`8.2.0/ComponentRegistry.json` vs `8.3.0/ComponentRegistry.json`). The catalog inside a file is already version-specific; there is nothing to filter at request time inside clio.

This trades richness for simplicity. A future schema bump (when AI needs cross-version diffing in the same response, or per-property `@since` data) is a coordinated CDN-shape + clio change, not part of v1.

## Lifecycle of a platform version on the CDN

```
master (8.4 dev) ─┬─ cut → 8.3.x branch created
                  │     │
                  │     ├─ Jenkins on branch-cut (baseline mode)
                  │     │    ├─ extract @ branch HEAD
                  │     │    ├─ Jenkins artifact ComponentRegistry.json
                  │     │    │   (inspection + regression detector;
                  │     │    │    NO git push, NO CDN refresh)
                  │     │    └─ diff vs previous-line GA → preview report
                  │     │
                  │     └─ ongoing commits on 8.3.x → re-extract,
                  │          refresh Jenkins artifact (still no git push)
                  │
                  └─ tag 8.3.0 (GA) ─ extract → git push to static-files-mcp
                                        ↳ 8.3.0/ComponentRegistry.json
                                        ↳ latest/ComponentRegistry.json
                                            (updated only if 8.3.0 > current latest)
                                                ↳ academy mirror picks it up
                                                   within ≤5 minutes
                                                ↳ /api/mcp/8.3.0/...
                                                ↳ /api/mcp/latest/...
                                                ↳ clio runtime picks it up
                                                   on next 24h-TTL refresh
```

Stages:

1. **Branch cut in creatio-ui.** Jenkins runs the extractor in baseline mode: AST walk → Jenkins artifact + preview diff vs the latest GA publication of the previous line. **No git push to `static-files-mcp`.** This catches extractor regressions and inconsistencies BEFORE GA.
2. **Ongoing commits on the release branch up to GA.** The same Jenkins job re-runs and refreshes the artifact. The GitLab repository is not touched.
3. **GA tag.** Extractor runs in release mode: AST walk + git push of `8.3.0/ComponentRegistry.json` into `static-files-mcp`. The `latest/ComponentRegistry.json` alias is updated **only if** `8.3.0` is the maximum semver-sorted GA published so far (protects against accidental backports overwriting `latest`). The academy mirror copies the new files into the public CDN tree within 5 minutes.
4. **clio bump path.** None required — clio picks up the new file on the next 24h-TTL refresh, with no PR in clio repo. To **bake the new version into the clio embedded fallback**, a clio release is built — the MSBuild fetch target embeds `latest/ComponentRegistry.json` at that moment.

## Failure-mode design

Architectural invariant: **each layer degrades independently; the consumer does not depend on the network at runtime.**

| Scenario | Behavior |
|---|---|
| academy.creatio.com unavailable | clio first try fails → falls back to file cache (24h stale OK) → if no cache, embedded snapshot. AI sees a valid catalog at all times. |
| CDN returns 404 for a requested version | clio falls back to `latest/ComponentRegistry.json`, then to file cache, then to embedded. Logged with `source=embedded`. |
| CDN returns malformed JSON | Parse fails → fall to next tier in the chain. The bad response is **not cached**. |
| File cache is corrupted | Parse fails → fall to embedded snapshot. Cache file is deleted. |
| Embedded snapshot missing in DLL | Hard fail at startup with a clear error message. (Embedded is built into clio.dll by MSBuild; absence means a broken clio build.) |
| Stale cache (>24h) but CDN reachable | Return stale immediately, refresh in background — AI does not wait. |
| Stale cache (>24h) and CDN down | Return stale anyway. Stale-while-error indefinitely until CDN recovers. |
| MSBuild fetch step fails at clio build | Use committed seed-snapshot in repo as the embedded resource. Build still succeeds; embedded just freezes at the last successful seed update. |
| `GetSysInfo` returns version > the freshest `latest/ComponentRegistry.json` on CDN | Use the `latest/ComponentRegistry.json` content with a warning logged. AI sees `resolvedFrom: "latest-fallback"`. |
| Academy mirror lags behind a fresh git push | Up to ~5 minutes of staleness on the CDN. clio's 24h TTL absorbs this — the request either hits the cache or sees the prior payload. The next mirror tick reconciles. |
| `static-files-mcp` push succeeds but mirror job fails | The CDN keeps serving the last successfully-mirrored payload. Producer fix in the mirror job; the GitLab repository remains the canonical state. |
| Schema breaking change (e.g. wrapper object instead of array) | Catalog parse fails everywhere → fall to embedded snapshot. clio team coordinates a release that handles both shapes. Shape change requires advance coordination per [jenkins-pipeline-spec.md](jenkins-pipeline-spec.md). |

No scenario touches the AI runtime UX. The architectural strength of the multi-layer fallback is that **every degraded mode still yields a catalog**.

## Comparison vs. the prior NuGet model

| Aspect | NuGet model (PR #595) | CDN model (this branch) |
|---|---|---|
| Domains | 3 (creatio-ui, composer-repo, clio) | 2 (creatio-ui, clio) |
| Transport | NuGet via nuget.org (under ATF owner) | HTTPS CDN at academy.creatio.com |
| Distribution latency | clio rebuild + bump PR + merge → hours | 24h TTL refresh → at most one day, no human action |
| Offline runtime | Yes (embedded in NuGet pkg) | Yes (embedded in clio.dll via build-time fetch) |
| Per-version model | Per-entry `availability` ranges in a single bundle | Per-file (one CDN file per platform version) |
| AI-side overrides | Owned by AI/clio team in composer-repo | Removed in v1 |
| Schema versioning | `schemaVersion` field in NuGet pkg | None (shape is the contract; coordinated change required) |
| Failure surface | nuget.org outage / NuGet pin mismatch | CDN outage / cache staleness |
| Number of CI pipelines | 3 (UI, composer, clio) | 2 (UI, clio) |
| Cross-team coordination cost | High (composer-repo as integration point with own ownership) | Low (creatio-ui CI emits into the academy-owned GitLab repository; the mirror is academy infra; clio is pure consumer) |
| Versioning of the registry itself | NuGet semver (independent of platform) | None — platform version IS the version |

**Why the CDN model wins for our use case:**

- **Refresh latency.** A catalog fix or new GA reaches every clio user within 24h with no human action. The NuGet model required a chain of PR reviews (composer-repo → bump → clio PR → merge → release).
- **No integration repo.** The composer existed to merge per-version NPM snapshots into a unified bundle and to apply overrides. With per-file delivery on the CDN and no overrides in v1, there is nothing to merge.
- **No NuGet feed dependency** for runtime catalog access. clio depends on academy.creatio.com (a Creatio-owned infra) instead of nuget.org (a public third party).
- **Simpler ownership.** Two teams own the contract; the academy team only hosts static files behind a defined URL pattern.

**Trade-offs we accept:**

- **No telemetry on registry version use** (NuGet model could correlate via package downloads; CDN gives only HTTP access logs at the academy infrastructure layer).
- **No per-entry version ranges in v1.** If a user is on `8.2.0` and AI tries a property added in `8.3.0`, that property is simply not in the `8.2.0.json` catalog — clean from the AI's perspective, but means we cannot represent "removed-in" semantics for the same component across versions in a single response.
- **CDN cache headers and freshness become an academy infra concern** (Cache-Control, CDN edge TTLs interacting with clio's 24h app-level TTL). Documented in [jenkins-pipeline-spec.md § Headers](jenkins-pipeline-spec.md).

## Out of scope (v1)

- Per-entry `Availability` ranges in the catalog. The file is the unit of versioning.
- Cross-version diff inside a single API call.
- AI-side overrides (`aiHidden`, `aiOverlay`).
- Explicit `target-version` parameter in `ComponentInfoArgs` (the internal resolver supports it; tool surface activates only `GetSysInfo > latest` in v1).
- Data-driven categories. The hardcoded `CategoryOrder` array in clio remains.
- A `clio component-registry refresh` CLI command (planned for the implementation plan, not a separate architectural feature).
- Composable-apps scoping (out of scope for v1, same as in the prior plan).

## Sources in creatio-ui

Extracted from [extractor-analysis.md](extractor-analysis.md) and the prior research:

| Source | What it provides | Quality |
|---|---|---|
| `@CrtViewElement({ type: 'crt.X' })` decorator | Canonical component name | **High** — AST-extractable |
| `*ViewConfig` TS interface | Property contract with types | **High** — TS-typed, JSDoc-friendly |
| `@CrtInterfaceDesignerItem` decorator | Defaults (`defaultPropertyValues`), `typeCaption`, `viewElementGroupType` | **High** — designer metadata |
| `api-extractor` (`docModel`) | JSON rollup of public API | **Medium** — does not see decorator content |
| Runtime registry (`BaseViewElementRegistry`) | Ground truth after bootstrap | **High**, but hard in CI |

**Inclusion criterion:** a class with the `@CrtViewElement` decorator.

**Exclusion filters** (unchanged from the prior research):

| Category | Glob |
|---|---|
| Test files | `**/*.spec.ts`, `**/*.spec.ui.ts`, `**/*.spec.tsx`, `**/*.test.ts`, `**/*.test.tsx` |
| Mocks | `**/*.mock.ts`, `**/mocks/**`, `**/__mocks__/**` |
| Built artifacts | `apps/pkgs/**` |
| Designer-only UI (lib) | `libs/studio-enterprise/ui/interface-designer-properties-panel/**` |
| Designer-only UI (subpath) | `**/designtime/**` |
| Standard | `**/node_modules/**`, `dist/**` |

`**/designtime/**` is an exact invariant: 44/44 `*PropertiesPanel` in `designtime/`, 0/192 non-PropertiesPanel in `designtime/`. Verified against `creatio-ui@master`. Details in [extractor-analysis.md](extractor-analysis.md).

**Critical:** the parser ignores the `@CrtViewElement` decorator inside JSDoc comments (otherwise it would pick up `usr.Example` from examples in decorator-definition files). Implementation — ts-morph node-level decorator API, not a text search.

## Extractor pipeline (Jenkins-side, target)

1. **AST walk** of all `*.ts` files with the filters applied.
2. For each class with `@CrtViewElement`:
   - `componentType` = `decorator.arguments[0].type` (string literal).
   - **NO** semantic filtering by suffixes. `*PropertiesPanel`-, `*Request`-named are included (the noise list from extractor-analysis is published as-is; pruning is deferred to a possible v2 curation stage).
3. Resolve the related `ViewConfig` interface (convention: `<ComponentName>ViewConfig` in `view-models/`).
4. For each property in ViewConfig:
   - `type` — text representation of the TS type
   - `description` — JSDoc `@description` or leading comment
   - `required` — absence of the `?` modifier
   - `values` — for union types of string literals
   - `default` — from `@CrtInterfaceDesignerItem.defaultPropertyValues[name]`
5. `category` — JSDoc `@aiCategory` overrides; otherwise — mapping via `viewElementGroupType`.
6. **Output:** a single JSON file matching the current top-level array shape. Path: `dist/component-registry/{version}.json` in the producer workspace; uploaded under the canonical CDN path below.
7. **Publish step:** git push of `{version}/ComponentRegistry.json` into the `static-files-mcp` GitLab repository. On success and only if `{version} > current latest semver`, the same commit also writes `latest/ComponentRegistry.json` with identical content. The academy mirror copies both files to `https://academy.creatio.com/api/mcp/{version}/ComponentRegistry.json` and `https://academy.creatio.com/api/mcp/latest/ComponentRegistry.json` on its next 5-minute tick.

Note: JSDoc `@since` / `@deprecated` parsing **is allowed in extractor output** (the schema simply ignores the fields in v1). This preserves the optionality of switching the model to per-entry availability later without changing the extractor — the consumer side (clio) just starts reading them.

## What NOT to do (anti-patterns)

- **Do not commit a generated registry into the clio repo at runtime.** The runtime path is CDN → cache → embedded. The embedded snapshot IS committed (seed only), but it is the fallback floor, not the runtime source.
- **Do not run the extractor at clio build time over `creatio-ui` checkouts.** clio does not have access to the platform monorepo. The embedded snapshot is fetched from the CDN, not re-extracted.
- **Do not parse `*.api.md` as JSON.** The markdown rollup is intended for review, not for parsing.
- **Do not store per-version snapshot files in the clio repo.** Only one committed seed file. Per-version files live on the CDN.
- **Do not do decorator extraction via regex.** AST walk via `ts-morph`.
- **Do not block the creatio-ui release on the extractor.** Build the registry in a separate Jenkins job that fails isolated; a registry-publish failure must not block a platform GA.
- **Do not have clio mutate the CDN or the GitLab repository.** clio is read-only. The only side-effect of clio is its own local cache.
- **Do not skip the `latest/ComponentRegistry.json` semver check.** A backport (8.2.5 published after 8.3.0 is GA) must not overwrite the `latest` alias to point at the older version.
- **Do not embed the API endpoint URL deep in clio code.** Use a single configurable constant — see [clio-target-structure.md](clio-target-structure.md).
- **Do not bypass the GitLab repository as the storage layer.** Any path that publishes directly to the academy edge breaks the audit trail and the diff-vs-history capability that the mirror flow gives us for free.

## Infrastructure artifacts that can be reused

- **academy.creatio.com** — existing public infrastructure (the academy site itself). The `/api/mcp/` prefix is a new path under existing TLS + DNS.
- **`static-files-mcp` GitLab repository** (`gitdigital.creatio.com/academy/static-files-mcp`) — already hosting the live `8.3.4/` and `latest/` payloads; serves as the storage layer between Jenkins and the mirror.
- **Academy 5-minute mirror job** — academy-owned scheduled sync that materialises the repository as the public CDN tree under `/api/mcp/`.
- **Jenkins pipeline-library** in creatio-ui — the `@Library('pipeline-library')` pattern is already in use.
- **Nx monorepo** in creatio-ui — `nx.json`, `project.json` in each lib — the standard path for an extractor task. See [jenkins-pipeline-spec.md](jenkins-pipeline-spec.md) for the proposed `nx` target naming.
- **MSBuild custom targets** in clio's csproj — used as the fetch-at-pack mechanism (`<Target Name="ResolveCdnSnapshot" BeforeTargets="…">`).

## Previously open questions, now closed by the CDN model

| # | Question | Decision |
|---|---|---|
| 1 | Owner of the extractor | Platform-UI team (in creatio-ui repo) |
| 2 | JSDoc vocabulary as a standard | Yes; not enforced by an eslint rule in v1 (deferred — not blocking) |
| 3 | Per-version vs unified bundle | Per-version files in the GitLab repo and on the CDN |
| 4 | What AI sees when no env is known | `latest/ComponentRegistry.json` content + `resolvedFrom: "latest-fallback"` marker in MCP response |
| 5 | Distribution channel | `static-files-mcp` GitLab repo as storage; academy.creatio.com public HTTPS as the mirrored edge, no auth |
| 6 | Backfill of old versions | Forward-only from the first GA tag that runs the new Jenkins job; pre-existing GAs are not retroactively published. While the Jenkins automation is being built, the `static-files-mcp` repo can be primed manually for the versions that AI needs today |
| 7 | `composable-apps/*` | A separate extractor pass in a future stage |
| 8 | Properties panels | Excluded by `**/designtime/**` filter at extractor level |
| 9 | Branch-creation hook in Jenkins | Polling or webhook in the same `Jenkinsfile.ComponentRegistry` |
| 10 | `latest/ComponentRegistry.json` semantics | Pointer to the max-semver GA file; updated atomically only when `version > current latest` |
| 11 | Cadence of GA tags | Per build (`8.3.0`, `8.3.1`, …); each one triggers a git push into `static-files-mcp`; the academy mirror copies the change within ≤5 minutes |
| 12 | Registration of new lines | None needed — every GA tag automatically gets a per-version file. No `supported-versions.json` to maintain |

## Still to close (lower level)

- **Bootstrap of the embedded fallback.** Before the first creatio-ui Jenkins job runs (and while the `static-files-mcp` repo is being primed manually), the CDN may not yet serve `latest/ComponentRegistry.json`. clio's MSBuild fetch target handles that: fall back to the committed seed-snapshot in repo. Documented in [implementation-plan-part1-cdn.md](implementation-plan-part1-cdn.md).
- **Seed-snapshot refresh policy.** When and how does the committed seed file in the clio repo get refreshed once the CDN is live? Options: (a) manual periodic PR, (b) GitHub Action cron, (c) frozen at first-release-after-CDN-active. **Default for v1:** the seed file is committed once with the current curated 92 records and never refreshed by automation — only manually when an embedded baseline obviously stales. This is conservative; reviewable later.
- **Schema change coordination.** If the JSON shape on the CDN needs to evolve (e.g., add `availability`), how do clio's existing clients handle it? **Default:** clio parses lenient (extra fields ignored). A breaking shape change (`array` → `object`) requires a coordinated multi-PR cycle and is out of scope for v1.
- **CDN cache headers.** What `Cache-Control` does academy emit for these files? `max-age=86400` matches clio's 24h TTL; longer is fine, shorter wastes the academy infra. To be confirmed with the academy team.
- **Telemetry contract.** What does clio log on each fetch (`source=cdn|cache|embedded`, latency, HTTP status)? Documented in [clio-target-structure.md](clio-target-structure.md).

## Why a CDN-based approach beats committing the file in clio (the alternative we did not pick)

The minimum-viable alternative is: keep the file in the clio repo, refresh it periodically via a bot PR. Reasons the CDN model wins:

1. **Refresh latency.** A bot-PR loop has merge friction (review, CI). CDN refresh is fully automatic on the platform release cadence.
2. **No repo bloat.** The registry grows over time. Keeping it in the clio repo blocks git operations slightly more each release.
3. **Cleaner ownership.** Platform-UI owns the catalog content; clio owns only the consumption. Committing in clio means clio reviewers gate-keep platform changes.
4. **Single artifact across clio versions.** Two clio versions (1.10 and 1.11) running against the same Creatio environment both see the same catalog at the same time — without a clio re-release.

## Why a CDN-based approach beats nuget.org (the model we abandoned)

See the [Comparison table](#comparison-vs-the-prior-nuget-model). Summary: lower refresh latency, fewer pipelines, no third-party feed in the runtime path, fewer cross-team coordination points, and no integration repo to maintain.
