# ADR: Theming with AI — Clio dev flow (ENG-90636)

**Status**: Accepted (implemented; R2 resolved 2026-06-18 — native `ThemeService`, no ClioGate)
**Author**: Architect Agent
**PRD/SPEC**: [spec-theming-clio-devflow.md](../prd/spec-theming-clio-devflow.md)
**Jira**: ENG-90636 — "Theming with AI. Clio (dev flow)" (Contour A)
**Created**: 2026-06-18
**stepsCompleted**: [1, 2, 3, 4]

---

## Context

A coding agent driving the Creatio AI Toolkit must create / re-tokenize a custom theme and ship it to an environment through clio's dev/workspace flow (Contour A). The theme **template** and **authoring guides** now ship in the npm package `@creatio/theming`; the ENG-89624 / ENG-90889 prototype embedded both in clio, which is no longer the right source of truth. The prototype also activated a new theme bluntly via `clear-redis-db` (a full Redis flush). This ADR records the already-decided architecture for reshaping that prototype: clio **delegates** all theme authoring to the npm package and contributes only orchestration guidance, npm wiring, transport, and one new capability — a **surgical theme-cache clear**.

All decisions below were made and locked during clarification; this ADR records them faithfully and is not re-opening them.

## Decision

clio adopts the **Delegate ownership model**: it does NOT embed the theme template or the creation/design-token guides and adds NO `new-theme` scaffold command/tool. The coding agent reads the template + guides from `node_modules/@creatio/theming` and writes the theme files itself. clio adds exactly one new capability — a surgical `clear-themes-cache` command (CLI + env-aware MCP tool + ClioGate endpoint) replacing the prototype's full-Redis flush — plus a thin `theming` MCP guidance pointer and npm wiring in the ui-project template. Scope is **Contour A only**; server-side ThemeService CRUD (Contour B) is a separate ticket.

---

## Decisions in detail

### D1 — Ownership model: Delegate (LOCKED)

clio owns orchestration, npm wiring, transport, and theme-cache activation. The npm package `@creatio/theming` owns the theme template (`theme.{json,css}.tpl`), the creation guide, the design-token catalog, the fonts guide, and the token-usage policy. The agent copies the template out of `node_modules` and fills the placeholders (`<%themeId%>` / `<%themeCaption%>` / `<%themeCssClass%>`) into `Files/themes/<cssClassName>/` itself. The theme descriptor is `{id, caption, cssClassName}` with **no `code` field** — satisfied by the package's `theme.json.tpl`; clio authors no descriptor template.

| Concern | Owner |
|---|---|
| Theme template (`theme.{json,css}.tpl`) | `@creatio/theming` (node_modules) — agent copies |
| Creation guide, design-token catalog, fonts guide, token-usage policy | `@creatio/theming` — clio only points to it |
| Orchestration (register env → ensure pkg → npm i → scaffold from package → push → clear theme cache) | clio — thin MCP guidance (`get-guidance theming`) |
| Transport: deploy | clio — existing `push-workspace` / `pushw`, `push-pkg` |
| Activation: theme cache invalidation | clio — NEW `clear-themes-cache` |

### D2 — `clear-themes-cache`: the one new clio capability (LOCKED)

A surgical theme-only cache invalidation replaces the prototype's blunt `clear-redis-db`. All mechanical wiring is known from existing patterns:

> **R2 superseded the ClioGate route + the cliogate gate.** As shipped, the route is the native
> `ServiceModel/ThemeService.svc/ClearThemesCache`, there is **no** ClioGate method, **no** cliogate bump,
> and **no** `[RequiresPackage]`. The bullets below are corrected to the implemented design.

- **Route** — add `KnownRoute.ClearThemesCache = 42` (continues the sequence after the current highest `41`) and `{KnownRoute.ClearThemesCache, "ServiceModel/ThemeService.svc/ClearThemesCache"}` to `KnownRoutes` in `clio/Common/ServiceUrlBuilder.cs`. `ServiceUrlBuilder.Build` prepends `0/` for `.NET Framework` environments automatically.
- **CLI command** — `ClearThemesCacheCommand : RemoteCommand<ClearThemesCacheOptions>`, verb `clear-themes-cache` (alias `flush-themes`), mirroring `clio/Command/RedisCommand.cs`: `protected override string ServicePath => _urlBuilder.Build(KnownRoute.ClearThemesCache);`. **No** `[RequiresPackage]` — the native endpoint is gated at runtime by `CanCustomizeBranding` + `CanManageThemes`. `ProceedResponse` parses the `ThemeService` `BaseResponse { success, errorInfo }`. Register in `clio/Program.cs` (verb wiring) and `clio/BindingsModule.cs` (DI). No new CLI options beyond the inherited `RemoteCommandOptions` (`-e`, etc.), so no CLIO001 surface beyond the verb/alias.
- **MCP tool** — environment-sensitive `BaseTool<ClearThemesCacheOptions>` exposing the two standard connection-mode tools `clear-themes-cache-by-environment` and `clear-themes-cache-by-credentials`, both via the env-aware path `InternalExecute<ClearThemesCacheCommand>(options)` (resolves a fresh command for the current request's environment). Safety flags: `ReadOnly=false`, `Destructive=false`, `Idempotent=true`. Registered via the existing `McpFeatureToggleFilter.RegisterEnabledPrimitives` seam (`IEnumerable<Type>` — never a `Type[]`, never `*FromAssembly`).
- **~~ClioGate endpoint~~ — superseded by R2.** No ClioGate method is added: the platform's native `ThemeService.svc/ClearThemesCache` already evicts only the theme cache. The earlier draft (a new `[WebInvoke]` in `cliogate/Files/cs/CreatioApiGateway.cs` with `CheckCanManageSolution()` first, plus a cliogate bump) is no longer applicable.

### D3 — Delegate guidance + npm wiring (LOCKED)

- **Thin MCP guidance resource** `theming` at `docs://mcp/guides/theming`, mirroring `clio/Command/McpServer/Resources/WorkspaceUiProjectGuidanceResource.cs` and registered in `clio/Command/McpServer/Resources/GuidanceCatalog.cs` (entry point for the agent = `get-guidance theming`). Its text is the ENG-88812 orchestration prompt only: ensure `@creatio/theming` is installed → read `node_modules/@creatio/theming/AI_GUIDES_INDEX.md` → `THEMING_CREATION_AI_GUIDE.md` + `THEMING_DESIGN_TOKENS_AI_GUIDE.md` + `templates/` → copy `theme.{json,css}.tpl` into `Files/themes/<cssClassName>/` and fill placeholders → `push-workspace` / `push-pkg` → `clear-themes-cache`. It **does NOT restate** the token catalog or creation guide; pointers reference `AI_GUIDES_INDEX.md` (version-agnostic) so they track the published naming.
- **npm dependency** — keep `@creatio/theming` in `clio/tpl/ui-project/package.json` devDependencies: it is the `--crt-*` design-token catalog component styling consumes (per `@creatio-devkit/common`'s REMOTE_COMPONENT_STYLING guide) as well as the theme-authoring source (version pinned in `clio/tpl/ui-project/package.json`).
- **`AGENTS.md`** — NO bespoke theme section. Theme authoring is delivered via the `get-guidance theming` MCP entry; the existing component-styling pointer (→ `@creatio-devkit/common`) already reaches the `@creatio/theming` design-token catalog transitively (a theme is not a remote module).
- **Plain-package case** — for a non-ui-project package, the `theming` guidance instructs the agent to drop a minimal `package.json` and run `npm i @creatio/theming` so `node_modules` exists.

### D4 — Scope: Contour A only (LOCKED)

Workspace/dev flow + push is in scope. Server-side `ThemeService` CRUD (create/update/delete/list-themes — Contour B / no-code) is explicitly out of scope and tracked under a separate ticket.

### D5 — Drop from the prototype (LOCKED)

Under Delegate, no embedded theme content survives. Remove / do not port:

- `clio/tpl/themes/theme.css.tpl` (template now lives in the package).
- The embedded `creatio-theme` + `design-tokens` MCP guidance text and the `DESIGN_TOKENS_AI_GUIDE.md` `EmbeddedResource` / `CopyToOutputDirectory` csproj entries.
- The entire `new-theme` stack: command / options / validator, `ThemeCreator`, `IThemeArtifactBuilder` / `ThemeArtifactBuilder`, `ThemeIdentifiers`, `WorkspacePackageProvisioner`, and `NewThemeTool`.

---

## Alternatives Considered

| Option | Pros | Cons | Status |
|--------|------|------|--------|
| **A: Delegate** (chosen) — package owns template + guides; clio adds only `clear-themes-cache`, thin `theming` pointer, npm wiring | Single source of truth; no duplication/drift; clio stays transport+orchestration; agent edits palettes anyway | Requires `@creatio/theming` present in `node_modules`; pointer must track published guide names | **Chosen** |
| B: Hybrid — a clio `new-theme` command that reads the `.tpl` from `node_modules` | Deterministic scaffold step inside clio | Couples clio C# to the node_modules path / macros / package version; determinism buys little since the agent re-tokenizes palettes afterward regardless | Rejected |
| C: Self-contained — keep the prototype's embedded template + guidance | No npm dependency at author time | Duplicates the package; diverges over time; contradicts "template ships in npm" (the explicit reshaping driver) | Rejected |

For theme-cache activation, the prototype's full `ClearRedisDb` is rejected as the steady-state mechanism (too blunt — flushes the entire Redis DB) and retained only as an explicit, logged last-resort fallback (see R1).

---

## Implementation Plan

### Files to create

| File | Purpose |
|------|---------|
| `clio/Command/ClearThemesCacheCommand.cs` | `ClearThemesCacheOptions : RemoteCommandOptions` (`[Verb("clear-themes-cache", Aliases=["flush-themes"])]`) + `ClearThemesCacheCommand : RemoteCommand<ClearThemesCacheOptions>` mirroring `RedisCommand.cs` |
| `clio/Command/McpServer/Tools/ClearThemesCacheTool.cs` | Env-aware MCP tool (`BaseTool<TOptions>`, `InternalExecute<TCommand>`), flags `ReadOnly=false`/`Destructive=false`/`Idempotent=true` |
| `clio/Command/McpServer/Resources/ThemingGuidanceResource.cs` | Thin `theming` guidance (`docs://mcp/guides/theming`), orchestration prompt only |
| `clio/help/en/clear-themes-cache.txt` | CLI `-H` help |
| `clio/docs/commands/clear-themes-cache.md` | Detailed GitHub docs |
| `clio.tests/Command/ClearThemesCacheCommand.Tests.cs` | Unit tests (route build, command, `BaseResponse` parsing — no `[RequiresPackage]`) |
| `clio.tests/Command/McpServer/ClearThemesCacheToolTests.cs` | Unit: MCP arg mapping + env-aware execution path |
| `clio.mcp.e2e/ClearThemesCacheToolE2ETests.cs` | E2E: both `clear-themes-cache` tools advertised by the real MCP server (NOT in CI — manual) |

### Files to modify

| File | Change description |
|------|-------------------|
| `clio/Common/ServiceUrlBuilder.cs` | Add `KnownRoute.ClearThemesCache = 42` + `KnownRoutes` entry `ServiceModel/ThemeService.svc/ClearThemesCache` (native endpoint, R2) |
| `clio/Program.cs` | Wire `clear-themes-cache` verb to its command (gated MCP registration unchanged via `McpFeatureToggleFilter`) |
| `clio/BindingsModule.cs` | Register `ClearThemesCacheCommand` (DI) — full-suite trigger per smart-regression policy |
| `clio/Command/McpServer/Resources/GuidanceCatalog.cs` | Add `["theming"] = Create("theming", "<desc>", ThemingGuidanceResource.Guide)` |
| `cliogate/Files/cs/CreatioApiGateway.cs` | ~~New `[WebInvoke] ClearThemesCache`~~ — **superseded by R2**: native `ThemeService.svc/ClearThemesCache` used, no ClioGate change |
| `clio/cliogate/cliogate.gz` | ~~Rebuilt after cliogate bump~~ — **superseded by R2**: no cliogate change |
| `clio/tpl/ui-project/package.json` | Add `@creatio/theming` to devDependencies (design-token catalog for component styling + theme authoring) |
| `clio/tpl/ui-project/AGENTS.md` | Unchanged — no theme section (theme authoring via `get-guidance theming`) |
| `clio/Commands.md` | Add `clear-themes-cache` to overview/index |
| `clio/Wiki/WikiAnchors.txt` | Add `clear-themes-cache` anchor |
| `docs/McpCapabilityMap.md` | Add `clear-themes-cache` tool + `docs://mcp/guides/theming` resource |
| ~~`clio/cliogate/` version + `[RequiresPackage("cliogate","<new>")]`~~ | **Superseded by R2** — no cliogate bump, no `[RequiresPackage]` (native endpoint, runtime auth) |

### Files to delete (drop from prototype — D5)

`clio/tpl/themes/theme.css.tpl`; embedded `creatio-theme` + `design-tokens` guidance resources and their `DESIGN_TOKENS_AI_GUIDE.md` csproj `EmbeddedResource`/`CopyToOutputDirectory` entries; the entire `new-theme` stack (command/options/validator, `ThemeCreator`, `IThemeArtifactBuilder`/`ThemeArtifactBuilder`, `ThemeIdentifiers`, `WorkspacePackageProvisioner`, `NewThemeTool`). Run CLIO005 after removal to catch newly-dead DI registrations.

### Key interfaces / contracts

```csharp
// clio/Command/Theming/ClearThemesCacheCommand.cs
[Verb("clear-themes-cache", Aliases = ["flush-themes"], HelpText = "Refresh the Creatio theme catalog cache")]
[FeatureToggle("theming")] // gated with the whole theming surface (adr-theming-native-build.md OQ-02); added post-ship
public class ClearThemesCacheOptions : RemoteCommandOptions { } // no [RequiresPackage] — native endpoint, runtime auth

public class ClearThemesCacheCommand : RemoteCommand<ClearThemesCacheOptions>
{
    private readonly IServiceUrlBuilder _urlBuilder;
    public ClearThemesCacheCommand(IApplicationClient applicationClient, EnvironmentSettings settings, IServiceUrlBuilder urlBuilder)
        : base(applicationClient, settings) => _urlBuilder = urlBuilder;
    protected override string ServicePath => _urlBuilder.Build(ServiceUrlBuilder.KnownRoute.ClearThemesCache);
    // ProceedResponse parses the ThemeService BaseResponse { success, errorInfo }: success=false → failure + errorInfo.message.
}
```

```csharp
// clio/Common/ServiceUrlBuilder.cs — KnownRoute additions
ClearThemesCache = 42,
// KnownRoutes:
{ KnownRoute.ClearThemesCache, "ServiceModel/ThemeService.svc/ClearThemesCache" },
```

> **~~ClioGate endpoint contract~~ — superseded by R2.** No ClioGate `[WebInvoke] ClearThemesCache` method exists. The command calls the native `ServiceModel/ThemeService.svc/ClearThemesCache` endpoint (`IThemeService`), which returns `BaseResponse { success, errorInfo }` and is gated at runtime by `CanCustomizeBranding` + `CanManageThemes`.

### CLI flag specification

| Flag | Type | Required | Description |
|------|------|----------|-------------|
| (verb) `clear-themes-cache` / alias `flush-themes` | — | — | Invalidate only the theme cache |
| inherited `RemoteCommandOptions` (`-e/--environment`, URI/login/password, etc.) | string | per base | Standard remote-command environment options |

No new bespoke flags are introduced; verb and alias are kebab-case. CLIO001 has no new surface to flag.

### Test strategy

| Layer | Framework | What to cover | File |
|-------|----------|--------------|------|
| Unit | NSubstitute / `BaseCommandTests<ClearThemesCacheOptions>` | route build (`KnownRoute.ClearThemesCache → ServiceModel/ThemeService.svc/ClearThemesCache`, `0/`-prefixed on NetFW), `ServicePath`, `BaseResponse` parsing (success / success=false+errorInfo / empty→success / non-JSON→failure) | `clio.tests/Command/ClearThemesCacheCommand.Tests.cs` |
| Unit | NSubstitute | MCP arg mapping + env-aware `InternalExecute<TCommand>` selection, safety flags | `clio.tests/Command/McpServer/ClearThemesCacheToolTests.cs` |
| Unit | NSubstitute | `get-guidance theming` resolves; article URI = `docs://mcp/guides/theming`; delegates to `@creatio/theming` | `clio.tests/Command/McpServer/GuidanceGetToolTests.cs` |
| Integration | Real FS / stub | template-copy + push flow as needed | `clio.tests/...Tests.cs` |
| E2E | clio.mcp.e2e | `clear-themes-cache` tool against real `clio mcp-server`; `get-guidance theming` discovery | `clio.mcp.e2e/...` |

All unit tests use `[Category("Unit")]`, `MethodName_ShouldExpectedBehavior_WhenCondition`, AAA + a `because` per assertion + `[Description]`. **MCP E2E is NOT in CI yet — manual execution only.** Because `BindingsModule.cs` / `Program.cs` / `Common/` are touched, the full unit suite is required (smart-regression rule 4) in addition to targeted `Module=Command|Common|McpServer` filters.

---

## Consequences

- **Positive**
  - Single source of truth: the template + guides live once in `@creatio/theming`; no clio/package drift.
  - clio's surface stays small — one verb, one MCP tool, one thin guidance pointer, npm wiring — and the embedded `new-theme` stack is retired (net code reduction).
  - Theme activation becomes surgical (theme-only eviction) instead of a full Redis flush, so unrelated cached state survives.
  - The `theming` guidance points at `AI_GUIDES_INDEX.md`, so it tracks the package's published guide naming without clio edits.
- **Trade-offs / costs**
  - Authoring now depends on `@creatio/theming` being present in `node_modules` (ui-project devDependency, or a minimal `package.json` + `npm i` for plain packages). Pointers are version-agnostic (they reference `AI_GUIDES_INDEX.md`); the dependency version is pinned in `package.json`.
  - Activation depends on the platform's native `ThemeService.svc/ClearThemesCache` endpoint (present in supported Creatio versions); the caller needs the `CanCustomizeBranding` license + `CanManageThemes` system operation at runtime. No cliogate re-deploy is required (R2).
- **Breaking change**: No. New verb + alias, additive `KnownRoute`, additive MCP tool/resource, additive npm wiring. Removal of the never-merged prototype `new-theme` stack and embedded theme content has no public-surface impact (it never shipped). No `RELEASE.md` migration entry required beyond a standard "added `clear-themes-cache`" note.

---

## Risks & open questions

- **R1 — Activation fallback.** Theme-cache clear must be surgical (constraint C2). The full `ClearRedisDb` is permitted only as an explicit, **logged** last-resort fallback if no surgical mechanism is available in time.
- **R2 — RESOLVED (2026-06-18) via resolution (a).** The platform exposes a native, purpose-built endpoint: the Creatio `ThemeService` web service (`IThemeService` in `Terrasoft.Core.ServiceModelContract.Theme.Interfaces`) has `POST ThemeService.svc/ClearThemesCache`, documented as *"Forces a refresh of the theme catalog so that the next GetAvailableThemes call observes any themes added or modified outside this service."* It returns `BaseResponse { Success, ErrorInfo }` and requires the `CanCustomizeBranding` license + `CanManageThemes` system operation (runtime auth — **not** a clio package gate).
  - **Outcome:** `KnownRoute.ClearThemesCache = 42` is wired to `ServiceModel/ThemeService.svc/ClearThemesCache` (NOT the ClioGate `/rest/CreatioApiGateway/...` route the draft assumed). **No ClioGate method is added, no cliogate version bump, and no `[RequiresPackage("cliogate", …)]` gate** on `ClearThemesCacheOptions`. `ClearThemesCacheCommand.ProceedResponse` parses the `BaseResponse` (case-insensitive `success`; surfaces `errorInfo.message` on failure). The R1 full-`ClearRedisDb` fallback was therefore **not needed**.
  - The earlier draft route value `/rest/CreatioApiGateway/ClearThemesCache` and the ClioGate-method / cliogate-bump notes elsewhere in this ADR are **superseded** by this resolution.
  - All other wiring (route enum, CLI command, MCP tool, docs, tests, npm pointers) was unblocked from the start and is implemented.
- **R3 — Package guide naming.** Canonical guides in `@creatio/theming` are the `THEMING_*`-prefixed files; the older `THEME_CREATION_AI_GUIDE.md` / `DESIGN_TOKENS_AI_GUIDE.md` in dist are stale. Pointers reference `AI_GUIDES_INDEX.md` so they track the guide naming without clio edits; the dependency version is pinned in `package.json`.

---

## Impact on testing / docs / MCP (repo policy)

- **MCP (`create-mcp-tool` / `test-mcp-tool` skills):** new `clear-themes-cache` tool (env-aware `BaseTool` path, registered via `McpFeatureToggleFilter.RegisterEnabledPrimitives`) and new `theming` guidance resource. Mandatory `clio.mcp.e2e` coverage for both (tool execution + `get-guidance theming` discovery) — unit mapping tests alone are insufficient. Do not pass a `Type[]` to `WithTools/WithResources/WithPrompts` and do not reintroduce `*FromAssembly`.
- **Docs (`document-command` skill):** `clio/help/en/clear-themes-cache.txt`, `clio/docs/commands/clear-themes-cache.md`, `clio/Commands.md`, `clio/Wiki/WikiAnchors.txt`, and `docs/McpCapabilityMap.md` (tool + resource). Use the canonical verb `clear-themes-cache` in filenames.
- **Tests:** targeted `dotnet test --filter "Category=Unit&(Module=Command|Module=Common|Module=McpServer)"` before commit; full unit suite additionally because `BindingsModule.cs` / `Program.cs` / `Common/` change. Run CLIO005 after deleting the prototype stack to remove any newly-dead DI registrations.

---

## Pre-implementation Checklist

- [x] R2 resolved (2026-06-18, resolution (a)) — native `ThemeService.svc/ClearThemesCache`; R1 full-flush fallback not needed
- [x] All new CLI surface is kebab-case (verb `clear-themes-cache`, alias `flush-themes`) — CLIO001
- [x] `ClearThemesCacheCommand` registered in `BindingsModule.cs` + wired in `Program.cs`
- [x] `KnownRoute.ClearThemesCache = 42` maps to `ServiceModel/ThemeService.svc/ClearThemesCache` (native endpoint)
- [x] **No** ClioGate method, **no** cliogate bump, **no** `[RequiresPackage]` — superseded by R2 (runtime auth via `CanCustomizeBranding` + `CanManageThemes`)
- [x] MCP tool uses env-aware `InternalExecute<TCommand>`; registered via `McpFeatureToggleFilter.RegisterEnabledPrimitives` (`IEnumerable<Type>`)
- [x] `theming` guidance is a thin pointer (no token catalog restated); registered in `GuidanceCatalog`
- [x] `@creatio/theming` in `clio/tpl/ui-project/package.json` devDependencies (styling + theme-authoring catalog); no bespoke theme section in `AGENTS.md` (theme authoring via `get-guidance theming`)
- [x] Prototype stack absent (D5 — branch fresh off master); CLIO005 clean
- [x] Error messages user-friendly; no raw `HttpClient` (uses `IApplicationClient`); no bare `catch (Exception)` (catches `JsonException` specifically)
- [x] `clio.mcp.e2e` coverage added (both tools advertised); live theme-activation E2E is manual (not in CI) — **pending**
- [x] Docs + MCP capability map updated; `.codex/workspace-diary.md` entry appended
