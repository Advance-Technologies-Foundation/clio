# ADR: Theming with AI (consolidated)

**Consolidated:** 2026-07-05. This file supersedes and absorbs the four originals:
`adr-theming-clio-devflow.md`, `adr-theming-server-flow.md`, `adr-theming-native-build.md`,
and `adr-theming-agent-advisor.md`. It is the single design-rationale document for the
"Theming with AI" initiative in clio.

## Status

| Area | Jira | Status |
|------|------|--------|
| Contour A — dev / workspace flow | ENG-90636 | **Shipped** — `clear-themes-cache`, `list-themes`, delegate/guidance model |
| Contour B — no-code / server flow | ENG-91387 | **Shipped** — `create-theme`, `update-theme`, `delete-theme` over native `ThemeService` |
| Native build engine | ENG-90636 (continuation) | **Live** — `build-theme`; `[FeatureToggle("theming")]` removed at go-live (2026-07-08), parity gate verified |
| Agent advisor | theming-agent | **Live** — renamed `theme-color-advisor` → `advise-theme-palette`; reachable (toggle removed at go-live); advisor story still in progress |

**Go-live (2026-07-08).** `[FeatureToggle("theming")]` has been removed from the entire theming
surface (verbs, MCP tools, resources, prompts); the feature is live on all four surfaces and the
public docs ship in the generated export baseline. The parity gate (C-D7) is green. This supersedes
the "dark by default until go-live" state that B-D1 and C-D6 below record as the original design.

---

## Context

"Theming with AI" lets a coding agent create, restyle, and manage custom Creatio themes through
clio. It spans four areas under two Jira epics (both under program epic **ENG-26797**):

- **ENG-90636** — Contour A (dev / workspace flow) **and** the native-build continuation.
- **ENG-91387** — Contour B (no-code / server flow), stacked on ENG-90636.

The four areas compose:

1. **Contour A (dev flow):** an agent drives clio's workspace/push flow to author a theme in package
   files and activate it, needing a surgical theme-cache clear.
2. **Contour B (server flow):** a no-code agent holding only a registered environment + credentials
   manages the full theme lifecycle server-side (create/update/delete) with no workspace and no push.
3. **Native build engine:** clio produces theme CSS in-process (deterministic OKLCH color math ported
   to C#) from a bundled template, retiring the `@creatio/theming` npm package.
4. **Agent advisor:** an interactive palette-conversation MCP tool projecting the internal engine as
   verdict packets so an orchestrating skill can run the conversational theming flow.

Runtime authorization is common to all environment-facing operations: the platform gates real use by
the `CanCustomizeBranding` license + the `CanManageThemes` system operation. clio adds no package gate
for these; the only build-time gate is the shared `theming` feature toggle.

---

## Decisions

### Area A — Contour A: dev / workspace flow (ENG-90636)

**A-D1 — Ownership model: Delegate.** clio does NOT embed the theme template or the creation/design-token
guides and adds NO `new-theme` scaffold command. The agent reads the template + guides from
`node_modules/@creatio/theming` and writes the theme files itself, filling the placeholders
(`<%themeId%>` / `<%themeCaption%>` / `<%themeCssClass%>`) into `Files/themes/<cssClassName>/`. The
theme descriptor is `{id, caption, cssClassName}` — **no `code` field**. clio owns orchestration, npm
wiring, transport (existing `push-workspace`/`pushw`, `push-pkg`), and theme-cache activation.
*(Note: A-D1 is later superseded for the build path by the native-build engine — see C-D9 — which
retires the npm dependency entirely. It is preserved here as the original devflow rationale.)*

**A-D2 — `clear-themes-cache`: the one new capability.** A surgical theme-only cache invalidation
replaces the prototype's blunt full-Redis flush.
- **Route** — `KnownRoute.ClearThemesCache` mapped to the native `ServiceModel/ThemeService.svc/ClearThemesCache`
  in `ServiceUrlBuilder`. `Build(KnownRoute)` prepends `0/` for .NET Framework environments automatically.
- **CLI command** — `ClearThemesCacheCommand : RemoteCommand<ClearThemesCacheOptions>`, verb
  `clear-themes-cache` (alias `flush-themes`), mirroring `RedisCommand`. `ProceedResponse` parses the
  ThemeService `BaseResponse { success, errorInfo }`.
- **MCP tool** — env-aware `BaseTool<ClearThemesCacheOptions>` exposing `-by-environment` /
  `-by-credentials`, via `InternalExecute<ClearThemesCacheCommand>`. Flags: `ReadOnly=false`,
  `Destructive=false`, `Idempotent=true`. Registered via `McpFeatureToggleFilter.RegisterEnabledPrimitives`.
- **No ClioGate, no cliogate bump, no `[RequiresPackage]`** (see R2 below) — the native endpoint is
  gated at runtime by `CanCustomizeBranding` + `CanManageThemes`.

**A-D3 — Delegate guidance + npm wiring.** A thin MCP guidance resource `theming` at
`docs://mcp/guides/theming` (entry point `get-guidance theming`), holding the orchestration prompt only —
it points at `AI_GUIDES_INDEX.md` (version-agnostic) and does NOT restate the token catalog or creation
guide. `@creatio/theming` kept in `clio/tpl/ui-project/package.json` devDependencies. No bespoke theme
section in `AGENTS.md`.

**A-D4 — Scope: Contour A only.** Server-side CRUD was explicitly deferred to ENG-91387 (Area B below).

**A-D5 — Drop the prototype.** Remove the embedded `theme.css.tpl`, the embedded `creatio-theme` /
`design-tokens` guidance + `DESIGN_TOKENS_AI_GUIDE.md` csproj entries, and the entire `new-theme` stack
(command/options/validator, `ThemeCreator`, `IThemeArtifactBuilder`/`ThemeArtifactBuilder`,
`ThemeIdentifiers`, `WorkspacePackageProvisioner`, `NewThemeTool`).

**Binding risks/refinements still load-bearing:**
- **R2 (resolved) — native `ThemeService`, not ClioGate.** The platform exposes a purpose-built native
  endpoint `POST ThemeService.svc/ClearThemesCache` (`IThemeService`) that evicts only the theme cache
  and returns `BaseResponse { Success, ErrorInfo }`. This superseded the draft's ClioGate `[WebInvoke]`
  method + cliogate version bump + `[RequiresPackage]` gate. Runtime auth (`CanCustomizeBranding` +
  `CanManageThemes`) replaces the package gate.
- **R1 — activation fallback (not needed).** The prototype's full `ClearRedisDb` was permitted only as an
  explicit, logged last-resort fallback; R2 made it unnecessary.
- **R3 — guide naming.** Canonical guides are the `THEMING_*`-prefixed files; pointers reference
  `AI_GUIDES_INDEX.md` so they track published naming without clio edits.

### Area B — Contour B: no-code / server flow (ENG-91387)

Three CLI verbs — `create-theme`, `update-theme`, `delete-theme` — each a `RemoteCommand<TOptions>`
over the native `ServiceModel/ThemeService.svc/{CreateTheme,UpdateTheme,DeleteTheme}` endpoints, with
matching env-aware MCP tools. No ClioGate, no cliogate bump, no `[RequiresPackage]`.

**B-D1 — Feature toggle (SUPERSEDES the original "ship enabled").** The original ENG-91387 decision was
to ship the three verbs publicly enabled (siblings shipped enabled; runtime auth already gates use). The
later native-build consolidation reversed this: the **entire** theming surface — `create/update/delete-theme`,
`list-themes`, `clear-themes-cache`, `build-theme`, `advise-theme-palette` — now carries
`[FeatureToggle("theming")]` on both the options class and the MCP tool type, dark by default until
go-live.

**B-D2 — Class topology: flat per verb + one shared static helper.** Three independent options classes
(`CreateThemeOptions`, `UpdateThemeOptions`, `DeleteThemeOptions`), each deriving from `RemoteCommandOptions`
with its own `[Verb]` + `[Option]`s — no shared options base (CommandLineParser gains nothing; the verbs
diverge in required flags). Shared CSS resolution + FR-10 field validation is one `internal static`
`ThemeRequestBuilder` (not a DI service): it resolves `--css-content` XOR `--css-content-file` (UTF-8
read, fail-fast) and validates `id`/`caption`/`cssClassName`/`cssContent` (regex + length). Both are
pure functions — the documented CLIO001 borderline case, matching the `PageSchemaMetadataHelper`
static-helper precedent; if CLIO001 flags it, the fix is confirming the precedent, **not** suppressing
or `[ResolvedDynamically]`.

**B-D3 — Commands: `RemoteCommand<TOptions>` + shared response parsing.**
- `GetRequestData` builds the **camelCase** bare-JSON body by **serializing a record** (never string
  interpolation) so `cssContent` escaping (newlines, quotes, braces, up to 1 MiB) is correct.
- CSS read + mutual-exclusion + FR-10 validation run **inside the command before the HTTP call**; on
  failure `Logger.WriteError("Error: …")` + `CommandSuccess = false` + return, with no HTTP issued.
- Shared `ThemeServiceResponse` record + static `ThemeServiceResponseParser.TryGetFailure` parse the
  `BaseResponse { success, errorInfo:{ errorCode, message } }`: explicit `success:false` → failure
  (surface `errorInfo.message`); empty body → success; non-empty non-JSON body → failure (a login-page
  redirect means the request never reached the service). The shipped `list-themes` / `clear-themes-cache`
  keep their private copies (out of scope to refactor).
- `create-theme` specifics: `--id` optional → auto UUID v4 (`Guid.NewGuid().ToString("D")`), returned to
  the caller; `--css-class-name` + CSS required; `--caption` optional → derived from `--css-class-name`;
  `--package-name` optional → resolved to UId via `PageSchemaMetadataHelper.QueryPackageUId`, and when
  omitted the `packageUId` key is dropped entirely so the server falls back to `CurrentPackageId`. No
  client-side "id exists" pre-check.
- `update-theme` specifics: full overwrite — `--id`, `--caption`, `--css-class-name`, CSS all required;
  **no** `--package-name` (cannot re-home; `GetAvailableThemes` returns `cssFilePath` not `cssContent`,
  so read-modify-write is infeasible). No read-before-write.
- `delete-theme` specifics: `--id` only; **not idempotent** — no existence pre-check; server `success:false`
  → `Error:` + exit 1.

**B-D4 — Routes.** Three additive `KnownRoute` entries mapped to
`ServiceModel/ThemeService.svc/{Create,Update,Delete}Theme`.

**B-D5 — MCP tools: three `BaseTool<TOptions>`, env-aware, asymmetric result shapes.** Each exposes
`-by-environment` + `-by-credentials`, resolving a fresh command per call (never the stale startup
instance), taking **inline `cssContent` only** (no file variant). They fall through the generic
`EnvironmentOptions` arm in `BaseTool.ResolveFromCallContainer` — **no switch edit needed**.
- `create-theme` uses a non-logging `TryCreateTheme(options, out createdId, out error)` data method (the
  `ListThemesCommand.TryGetAvailableThemes` pattern) and returns a **structured `CreateThemeResult
  { success, id, error? }`** so the agent learns the generated id. `update`/`delete` use the standard log
  envelope (`CommandExecutionResult`) — the id is caller-supplied.
- Safety flags (`OpenWorld=false` on all): create `false/false/false`; update `false/false/true`; delete
  `false/**true**/false`. Auto-discovered via `McpFeatureToggleFilter.RegisterEnabledPrimitives`.

**B-D6 — Guidance edit.** Flip the "No-code / server flow" section in `ThemingGuidanceResource` from "not
yet available" to available; add a body section covering create/update/delete; keep the shared sections
and the thin token-catalog pointer (no restatement).

**B-D7 — DI + dispatch wiring.** `AddTransient` the three commands in `BindingsModule`; add the three
`typeof(*ThemeOptions)` to the `CommandOption` list and three dispatch arms in `ExecuteCommandWithOption`.
Tools are auto-discovered (no manual tool list).

**Binding refinements (still load-bearing):**
- **R-01** — `create-theme` mirrors `ListThemesCommand`'s silent/logging split exactly: `TryCreateTheme`
  parses via the shared parser and writes nothing to the logger (no MCP channel noise); logging lives only
  in the CLI path (`create-theme` overrides `ExecuteRemoteCommand`, not `ProceedResponse`).
- **R-02** — CLI echoes the generated id (`Created theme '{id}'`) so a human/agent can chain follow-ups.
- **R-03** — `--package-name` resolution failure fails fast (`Error:`, exit 1, no HTTP); a null UId is never
  sent.
- **R-04** — validation lives in the command/data method, shared by both surfaces, so MCP and CLI enforce
  the identical 1 MiB `cssContent` cap.
- **R-05** — empty-vs-absent CSS matrix: both absent (`null`) → error; `--css-content ""` → valid (empty
  CSS allowed, null not); empty file → valid; both present → error.
- **R-06** — the auto-UUID is re-validated through the same id regex (defence in depth).
- **R-07** — update/delete `ProceedResponse` log failures as errors (`WriteError`) so the exit code is
  non-zero.
- **R-08** — omitted-`packageUId` → server `CurrentPackageId` fallback is a live-stand verification item
  (not unit-verifiable).
- **R-10** — body-assertion tests must expect `System.Text.Json`'s default `\uXXXX` escaping.

### Area C — Native build engine (ENG-90636 continuation)

Target model: **`build-theme = C# color math + bundled template + brand inputs → theme.css`**, retiring
the `@creatio/theming` npm package. A new `Clio.Theming` namespace holds the ported deterministic OKLCH
math; a `build-theme` CLI verb + MCP tool expose it; the template ships bundled in-tool.

**C-D1 — `build-theme` is a compute tool with an optional local-write mode — NOT combined with create.**
Flags `ReadOnly=false, Destructive=false, Idempotent=true, OpenWorld=false`. Two modes:
- **Compute mode** (CLI stdout / MCP default) — returns the `theme.css` string + descriptor + warnings.
- **Workspace-write mode** — CLI takes `--output`; the **MCP tool takes `workspaceDirectory` (absolute) +
  `packageName`** and resolves `<ws>/packages/<pkg>/Files/themes/<cssClassName>/` itself, writing
  `theme.css` + `theme.json` and returning only the **path** (no CSS payload). This keeps the large CSS
  (tens of KB) out of the agent context — the token-cost reason the MCP tool exposes the write mode.

It **never touches an environment** to mutate; `--environment-name` only *resolves the template version*.
It composes with both flows (workspace writes files; no-code passes the returned CSS to `create-theme`).
`ReadOnly=false` because the write mode modifies the local filesystem (safety flag is per-tool, not
per-invocation); `Destructive=false` (writes generated artifacts into a caller-supplied workspace,
mirroring `new-ui-project`). FS checks + path resolution live in `BuildThemeCommand` (owns `IFileSystem`),
so the flat tool stays thin and unit-testable.

**C-D2 — `Clio.Theming` namespace; math = `internal static`, builder = one DI service.** The color math
is stateless `internal static` helpers (`ColorSpace`, `ColorNormalizer`, `PaletteGenerator`, `ColorMetrics`,
`TextTokenResolver`, `FontImportBuilder`), the `PageSchemaMetadataHelper`/`ThemeRequestBuilder` precedent.
`ThemeCssBuilder` is the one behavior class → interface `IThemeCssBuilder` + `AddSingleton` (stateless);
`Build(templateCss, options)` takes the template **as an argument** (no built-in disk read), keeping the
math I/O-free. A new `Theming` test-module trait maps `Theming` → `clio/Theming/`.

**C-D3 — Color-math port: operator-for-operator, `double`, parity hazards pinned.** HEX → sRGB → linear
RGB → OKLab → OKLCH with verbatim matrices; JS `Number` → C# `double` throughout. Non-obvious hazards
that flip hexes: JS `Math.round` semantics (round-half-toward-+∞ with a ULP guard) with **round-then-clamp**
order in `rgbToHex` — **not** `Math.Floor(x+0.5)` (R-07: diverges at `0.49999999999999994`); `**` →
`Math.Pow`; invariant-culture hex parse/format (R-09); the binary searches (`maxChromaInGamut`, `cuspL`),
`suggestAdaptedPrimary500`'s float-accumulation loop, and the gamut test `channel >= -0.001 && <= 1.001`
must reproduce the exact accumulation/comparison (R-11); stable sort via `OrderByDescending` in
`chooseBestAccent` preserving tie order (R-11).

**C-D4 — Template-fill is regex-driven; port patterns verbatim with match timeouts.** Strip the header
comment, `replaceAll('<%themeCssClass%>', …)`, per palette name×step replace, `finalizeTextTokens`,
`applyFonts` (+ Google Fonts `@import` when not default Montserrat). Regexes ported with a `matchTimeout`
(Sonar S6444), plus a post-fill runtime guard (no `<%…%>` remains, every palette step substituted). R-10
hazards: JS non-global `replace` replaces only the first match → use the count-1 .NET overload; use a
`MatchEvaluator` for user-input replacements; `\z` not `$` for end-of-string; keep `[\s\S]` verbatim, no
`Multiline`/`ECMAScript`. `replaceAll('<%themeCssClass%>',…)` is a literal `string.Replace` (no timeout).

**C-D5 — Template sourcing: bundled, version-pinned `IThemeTemplateProvider`.** The freedom.scss-derived
`theme.css.tpl` + `theme.json.tpl` ship **inside clio** under `clio/tpl/themes/{version}/`, copied by the
existing `<Content Include="tpl\**">` glob (same mechanism as `tpl/ui` project templates). `IThemeTemplateProvider`
reads them off disk and picks the **highest bundled version ≤ the target** Creatio version; empty/null
target → highest bundled; below the lowest bundled → `ArgumentException`; missing file →
`InvalidOperationException`. **No network fetch, no cache tier, no override.** (`--version` XOR
`--environment-name`; explicit wins; environment resolves the version via `PlatformVersionResolver`; both
→ mutually-exclusive error — R-03.)

**C-D6 — Feature toggle until the surface is complete and go-live is approved.** `[FeatureToggle("theming")]`
on `BuildThemeOptions` **and** on the `[McpServerToolType]` `BuildThemeTool`. The feature key `theming`
gates the **entire** theming surface (build + CRUD + list + cache + advisor + guidance), each carrying the
attribute on its options class and MCP type (OQ-02). Because the template is bundled, nothing external
gates the feature; go-live = surface complete + parity verified, then flip the toggle. MCP registration
stays through `McpFeatureToggleFilter.RegisterEnabledPrimitives` (`IEnumerable<Type>`).
**Superseded at go-live (2026-07-08): the toggle has been removed and the surface is live (see Status
above); this records the original gated design.**

**C-D7 — Bit-exact validation: spec anchors + generated parity fixture + drift guard.** Three layers:
(1) ported `*.spec.ts` anchors as NUnit assertions; (2) a **committed, frozen JSON parity fixture** of
random `hex → {generateScale, deriveSecondary, accentCandidates}` pairs + full `buildThemeCss` outputs,
captured once from the TS package — this is the **load-bearing gate** because `Math.Pow`/`Math.Cbrt` are
NOT bit-guaranteed across V8↔.NET (R-08 — run a parity spike first; decide fdlibm port vs documented
tolerance), adversarially seeded near rounding/gamut/threshold boundaries; (3) a template/builder contract
guard + committed template fixture. Uniform palette calibration (R-18): assert `generateScale(-500)` for
all five default anchors (primary/secondary/accent/success/error) equals the TS golden **identically** —
no secondary special-case; the math never reproduces the platform's hand-tuned secondary (build-theme
always generates it from `generateScale(deriveSecondary(primary))`).

**C-D8 — Guidance swap; token catalog + prose guides stay out of clio.** Edit `ThemingGuidanceResource`
to replace the npm-fetch instructions with "call `build-theme`", keeping all other sections. The `--crt-*`
**token catalog** is freedom.scss-derived DATA whose home is the CDN/Academy, served later via a future
`get-theme-tokens` tool (out of scope) — embedding it in clio would recreate the cross-repo drift the
Component Registry retired its in-DLL snapshot to avoid. clio's guidance stays lean.

**C-D9 — Decommission `@creatio/theming` FULLY, including its component-styling use (R-01, re-opened
scope).** Target state: the dependency is gone with its styling use handled. Three removals: (1) guidance
npm-fetch text → `build-theme`; (2) the `clio/tpl/ui-project/package.json` devDependency; (3) the
component-styling token-catalog reference rehomed. Investigation confirmed the styling consumption is
**author-time reference documentation only** — no code/SCSS/build import (`styles.scss` is empty; `.ts`
imports only `@creatio-devkit/common`; the `--crt-*` variables are platform `:root` runtime primitives).
Removing the devDependency does not break the Angular build or runtime rendering. The clio-side migration
is SMALL (1-line removal + ~15-25 lines of scaffold guidance repoint); the binding cost is cross-repo —
the `@creatio-devkit/common` REMOTE_COMPONENT_STYLING guide repoint + the token catalog rehoming to
CDN/Academy — which is the same out-of-scope producer workstream as the template. This **supersedes** the
earlier devflow "keep the dep" resolution.

**C-D10 — Out-of-scope seams (flagged):** the upstream `--crt-*` token-catalog generator and the future
read-only `get-theme-tokens` MCP tool. The build-theme template is bundled and does not depend on either.

### Area D — Agent advisor (`advise-theme-palette`, formerly `theme-color-advisor`)

The ported `Clio.Theming` engine is exposed only as the one-shot `build-theme`; its primitives are
internal and unreachable by an orchestrating agent, so the interactive palette conversation (contrast
triage, adapted primary, secondary preview, accent A/B/C) is undeliverable. This area adds the interactive
orchestration layer. Coverage is split between **clio** (the tool) and **one adac skill**
(`creatio-theme-orchestrator`, which owns conversation, intake, and policy).

**D-D1 — One fat, verdict-returning tool.** `advise-theme-palette` (renamed from `theme-color-advisor`) —
a single tool re-called whenever a color input changes, NOT granular per-function tools (6-8 round-trips is
token-disqualifying) and NOT an all-at-once batch planner (inverts the "collect primary first" intake). The
authoritative contract for this tool is this ADR (Area D); the earlier standalone
`spec/theming-agent/theme-color-advisor-contract.md` no longer exists.

**D-D2 — Verdicts, not raw numbers.** Returns `pass`/`warn`/`strong`, boolean gates, precomputed
`similarityBand`/`isBest`/counts, and tool-owned warning codes + threshold-free messages. Every threshold
(3:1 contrast, 0.10/0.07 OKLab similarity) lives in clio; the LLM never compares to a threshold.

**D-D3 — Stateless + offline + read-only.** `ReadOnly=true/Destructive=false/Idempotent=true/OpenWorld=false`,
`[FeatureToggle("theming")]` (superseded at go-live — the toggle was removed with the rest of the theming
surface, see Status), flat `ComponentInfoTool`-style, action-dispatched by an `operation` arg. No
`environmentName` (would force a network call, breaking `OpenWorld=false`); `preview` takes an offline
`version` only. The skill holds all conversation state and re-passes the full input set each call.

**D-D4 — No metadata persistence in v1.** `sourcePrimaryHex`/choices/`acceptedWarnings`/`algorithmVersion`
have no vehicle (the adac server flow sends no `theme.json`); skill-side state only.

**D-D5 — Theme name.** 50 chars is a soft recommendation in guidance/skill; the hard max (250) is already
enforced in `create-theme`/`update-theme`. No new length rejection.

**D-D6 — Apply/default.** A single final confirmation = build + create; making the theme the default is a
separate explicit question (it changes the look for everyone) unless the user asked up front. Owned by the
skill.

**D-D7 — Non-text scope.** Advisor verdicts are non-text WCAG 3:1 ("usable as a brand/UI color on white");
the stricter 4.5:1 text contrast stays a build-time concern in `TextTokenResolver`.

**Engine changes required** (all internal, same-assembly): `ColorNormalizer.TryNormalize`; a 3-state
adapted-primary outcome carrying original contrast; a `MeetsMinContrastOnWhite` predicate; accent
similarity thresholds + `ClassifySimilarityBand`; a `SelectBestValidAccent` filtering on both gates (the
existing `ChooseBestAccent` filters contrast-only with a degenerate fallback — must not be reused);
`IThemeTemplateProvider.TryGetPaletteDefault` (superseded — the advisor now loads the template once via
`GetCssTemplate` and reads the defaults directly, so the provider member was removed); a public offline
`ResolveCompatibleVersion(string)`.
Verdict/warning-code types are tool-owned, not engine.

---

## Rejected / superseded alternatives

**ClioGate endpoint for cache clear (rejected).** The Contour A draft added a `[WebInvoke] ClearThemesCache`
to `CreatioApiGateway.cs` + a cliogate version bump + `[RequiresPackage("cliogate")]`. **Superseded by the
native `ThemeService.svc/ClearThemesCache` endpoint** (A-R2): the platform already ships a purpose-built,
runtime-authorized endpoint, so no ClioGate method, cliogate bump, or package gate is needed.

**Full `ClearRedisDb` as steady-state activation (rejected).** Too blunt (flushes the entire Redis DB);
retained only as a logged last-resort fallback, which R2 made unnecessary.

**Embedded/self-contained theme template + guides in clio (rejected → Delegate, then Native build).** The
prototype embedded the template + guides in clio; Contour A A-D1 rejected this (Option C) for the Delegate
model to avoid duplication/drift. A Hybrid `new-theme` reading the `.tpl` from `node_modules` (Option B) was
also rejected (couples clio C# to the node_modules path/macros/version for little determinism gain).

**npm `@creatio/theming` template + `node_modules` copy (superseded by native bundled build).** The Delegate
model had the agent copy the template out of `node_modules` and had clio keep the npm devDependency. The
native-build engine (C-D5/C-D9) **supersedes** this: the OKLCH math is ported to C#, the template is bundled
in `clio/tpl/themes/{version}/`, and the npm package is fully decommissioned — no `node_modules` copy at
author time.

**"Keep the dep" resolution for component styling (superseded).** The earlier devflow rationale (and PRD
CAP-04) kept `@creatio/theming` for its component-styling token-catalog reference. C-D9 / R-01 **supersedes**
it: the dep is retired with its styling use migrated (author-time docs only; rehomed to CDN/Academy).

**CDN remote-fetch of the build template (superseded by bundling).** The native-build ADR originally fetched
the template from the CDN via a dedicated client with cache/SWR/producer-gating. The 2026-06-30 pivot
**supersedes** this (C-D5): the template is a small, stable, version-pinned asset that ships fine in-tool,
removing the network dependency and the producer gate. (R-04/R-05 staleness/SWR questions are thereby moot.)

**Ship the native build un-toggled (rejected → bundled + feature toggle).** Exposing the half-built verb
day one was rejected; `[FeatureToggle("theming")]` keeps it dark until the surface is complete (C-D6).

**Server-flow shared abstractions (rejected).** A shared `ThemeWriteOptions` base + `ThemeWriteCommand<T>`
base + CSS mixin (Option A) was rejected — CommandLineParser gains nothing and it hides per-verb required
flags. A DI `IThemeRequestBuilder` service (Option A′) was rejected for the `internal static` helper (no
seam any test needs). Client-side existence pre-checks (Option C) rejected by OQ-02/OQ-04. Hand-built JSON
request strings (Option D) rejected — `cssContent` escaping is a correctness landmine, must serialize a
record.

**Combine build + create into one mutating tool (rejected).** Would force an env arg + by-env/by-cred
duplication on a pure compute function and conflate compute with deploy (C-D1).

**Embed the token catalog + prose guides in clio (rejected).** Recreates the cross-repo drift the registry
retired its in-DLL snapshot to avoid; the catalog stays single-sourced on the CDN (C-D8).

**Idiomatic color-math port (`Math.Round` / `x*x*x`) (rejected).** Silently diverges from the TS
calibration at hex boundaries (banker's rounding / last-ULP) — port operator-for-operator (C-D3).

**Granular per-function advisor tools / batch planner (rejected).** Round-trip cost / intake inversion
(D-D1) — one fat verdict-returning tool instead.

---

## Consequences

- **Contour A (live):** single source of truth for template + guides at the time of shipping; surgical
  theme-only cache eviction instead of a full Redis flush; net code reduction (the `new-theme` prototype
  retired). Activation depends on the native `ThemeService.svc/ClearThemesCache` + runtime auth; no cliogate
  re-deploy.
- **Contour B (live):** the no-code theme catalog is complete (read + cache + create + update + delete) with
  no workspace and no push. `create-theme` returns the generated id so an agent chains follow-ups without a
  list round-trip. Purely additive — no behavior change to `list-themes`/`clear-themes-cache`; create uses
  the non-logging data path, update/delete the clean log envelope.
- **Native build (implemented, live):** agents stop shelling out to Node; theme CSS is produced
  in-process, deterministically, bit-exact with the retired package; the template ships bundled (no network
  dependency, no producer gate). The MCP workspace-write mode keeps the large CSS out of the agent context.
- **Agent advisor (implemented, live; story in-progress):** the interactive palette conversation is
  deliverable; the skill owns state/policy, clio owns thresholds and verdicts.
- **Gating:** `[FeatureToggle("theming")]` was removed at go-live (2026-07-08) — the entire theming surface
  is live on all four surfaces. Runtime auth (`CanCustomizeBranding` + `CanManageThemes`) gates real
  environment use independently.
- **Breaking change:** none across all areas — additive verbs/routes/tools/guidance, a scaffold-dependency
  removal, and a formerly feature-toggle-gated surface (toggle removed at go-live). A GitHub Release note suffices.
- **Standing risks:** float parity (`Math.Pow`/`Math.Cbrt` ULP divergence — the mandatory parity fixture is
  the gate); regex parity (ported verbatim with match timeouts); `build-theme` output can exceed the
  downstream 1 MiB `cssContent` cap (R-14, enforced in `create-theme`, not pre-checked by `build-theme`);
  unsanitized `cssContent` sent verbatim (server does not sanitize either — a documented scope decision).

---

## Open items

- **Native build — decommission `@creatio/theming` (story 7).** Externally gated: blocked on the token
  catalog rehoming to CDN/Academy **and** the cross-repo `@creatio-devkit/common` REMOTE_COMPONENT_STYLING
  guide repoint. Removing the dep before the catalog is rehomed leaves an author-time documentation gap. The
  npm deprecation/unpublish is a creatio-ui workstream outside clio's control. (C-D9 / R-01)
- **Native build — go-live (story 8) — DONE 2026-07-08.** `[FeatureToggle("theming")]` removed; the surface is
  live (tests, docs, MCP tool complete) and the parity gate is verified/green — no external producer ticket,
  since the template is bundled. (C-D6 / R-12)
- **Agent advisor — story in-progress.** Engine changes + `advise-theme-palette` tool + DTOs + unit/e2e tests
  + `ThemingGuidanceResource` rewrite. Reachable now (toggle removed at go-live); the advisor story itself is
  still in progress. Deferred follow-ups from
  the contract: threshold calibration on real brands, the specific Academy article URL, metadata persistence
  if a future flow needs re-editing from source colors.
