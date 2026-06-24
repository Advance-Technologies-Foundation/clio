# ADR: Theming with AI — Toolkit (vibe-coder / server flow)

**Status**: Accepted (revised after adversarial bmad-reviewer pass — see "Post-review refinements")
**Author**: Architect Agent
**PRD**: [prd-theming-server-flow.md](../prd/prd-theming-server-flow.md)
**Jira**: ENG-91387 (epic ENG-26797) — Contour B (no-code / server flow); sibling Contour A = ENG-90636 (shipped)
**Created**: 2026-06-21
**stepsCompleted**: [1, 2, 3, 4]

---

## Context

An AI coding agent driving the Creatio AI Toolkit (the "vibe-coder" flow) can today only *read* themes
(`list-themes`) and refresh the cache (`clear-themes-cache`); it cannot create, restyle, or delete a theme
on an environment without a clio workspace, package, and push (Contour A, ENG-90636). This ADR designs the
**write half** of the catalog — `create-theme` / `update-theme` / `delete-theme` — as thin clio commands over
the native Creatio `ThemeService` write endpoints, with matching env-aware MCP tools, so a no-code agent
holding only a registered environment + credentials can manage the full theme lifecycle (read + cache +
create + update + delete) server-side, with no workspace and no push.

All six PRD open questions are already resolved (OQ-01…OQ-06, recorded in the task brief); this ADR designs to
those answers and does not reopen them. This branch (`feature/ENG-91387-theming-server-flow`) is **stacked on**
`feature/ENG-90636-theming-with-ai-clio-dev-flow`, which contributes the `ThemeService` route pattern, the
`ListThemesCommand` / `ClearThemesCacheCommand` reference shapes, the `ThemingGuidanceResource`, and the
`KnownRoute.ClearThemesCache = 43` baseline.

## Decision

Add three CLI verbs — `create-theme`, `update-theme`, `delete-theme` — each as a `RemoteCommand<TOptions>`
calling the native `ServiceModel/ThemeService.svc/{CreateTheme,UpdateTheme,DeleteTheme}` endpoint, three matching
`KnownRoute` entries (44/45/46), three env-aware `BaseTool<TOptions>` MCP tools (each `-by-environment` /
`-by-credentials`), one shared CSS-resolution + validation helper, and a guidance edit flipping the "No-code /
server flow" section in `ThemingGuidanceResource` to *available*. Commands **ship enabled** (no `[FeatureToggle]`),
mirroring the shipped siblings; real use is gated at runtime by the platform (`CanCustomizeBranding` license +
`CanManageThemes` system operation), not by clio. No ClioGate method, no cliogate bump, no `[RequiresPackage]`.

---

## Decisions in detail

### D1 — Ship enabled, no `[FeatureToggle]` (OQ-01)

The three verbs ship publicly enabled, exactly like `list-themes` / `clear-themes-cache`. Rationale: the siblings
shipped enabled; the task mandates public docs and a guidance "available" flip; and runtime auth
(`CanCustomizeBranding` + `CanManageThemes`) already gates real use, so a build-time toggle would only hide a
correctly-guarded surface. No `[FeatureToggle]` on any options class or MCP tool class.

### D2 — Class topology: one options class + one command + one tool per verb; one shared CSS helper

**Three independent options classes** (`CreateThemeOptions`, `UpdateThemeOptions`, `DeleteThemeOptions`), each
deriving directly from `RemoteCommandOptions` and carrying its own `[Verb]` + `[Option]`s. No shared intermediate
options base class and no CSS-input "mixin" options type:

- CommandLineParser reflects over the concrete options type; an abstract/shared options base buys no parser
  benefit and the repo convention (e.g. `ListThemesOptions`, `ClearThemesCacheOptions`) is one flat options class
  per verb. The three verbs also diverge in required flags and in whether `--id` / `--package-name` exist, so a
  shared options base would mostly hold *optional* members that each verb re-documents anyway.
- The duplicated members are just `[Option]` declarations (data, no behavior) — duplication here is cheaper than
  an inheritance hierarchy that obscures which flags each verb actually exposes in `-H` help.

**Shared CSS resolution + FR-10 validation** is factored into **one static helper** `ThemeRequestBuilder`
(`clio/Command/ThemeRequestBuilder.cs`), not a DI service:

- It does two things: (a) resolve `--css-content` xor `--css-content-file` into a CSS string (read file as UTF-8,
  fail fast on missing/unreadable file — OQ-06), and (b) validate the FR-10 field contract (`id`, `caption`,
  `cssClassName`, `cssContent` length/regex). Both are pure/near-pure functions with no injected collaborators.
- **CLIO001 judgement:** CLIO001 pushes behavior classes toward an interface + DI. This helper is the documented
  borderline case — it has no state, no dependency, and is exercised directly by the three commands and the unit
  tests. Modeling it as an injected service would add a registration and an interface with no seam that any test
  or caller benefits from. **Decision: keep it `internal static`**, matching the existing
  `PageSchemaMetadataHelper` precedent (an `internal static` helper invoked by `PageCreateCommand`). It performs no
  HTTP and no behavior that needs substitution — file I/O is `System.IO.File.ReadAllText`, which the command-level
  tests cover via real temp files (Integration) and string inputs (Unit). If CLIO001 flags the static call, the
  fix is **not** `[ResolvedDynamically]` (that is for dead-looking DI registrations) — the helper is not a DI
  registration at all; it is a utility class, the same shape CLIO005/CLIO001 already tolerate for
  `PageSchemaMetadataHelper`.

`ThemeRequestBuilder` returns a small DTO record `ThemeValidationResult { bool Ok, string Error, string CssContent }`
(or equivalent out-parameters) so the command surfaces `Error: …` and skips the HTTP call on any failure.

### D3 — Commands: `RemoteCommand<TOptions>` + shared response record (FR-01..FR-03, FR-08, FR-09)

Each command derives from `RemoteCommand<TOptions>`, mirroring `ClearThemesCacheCommand`:

- `protected override string ServicePath => _urlBuilder.Build(ServiceUrlBuilder.KnownRoute.{Create|Update|Delete}Theme);`
- `GetRequestData(options)` builds the **camelCase** bare-JSON body with `System.Text.Json`
  (`JsonSerializer.Serialize`, default camelCase via `JsonNamingPolicy.CamelCase` or a serialized record with
  `[JsonPropertyName]`). Using a serialized request record (not hand-concatenated strings) guarantees correct
  escaping of `cssContent` (newlines, quotes, braces) — this is the single most error-prone part and must not be
  string interpolation.
- **CSS read + mutual-exclusion + FR-10 validation run inside the command *before* the HTTP call**, in an override
  of `ExecuteRemoteCommand` (or a pre-check at the top of `GetRequestData` that sets `CommandSuccess = false` and
  throws `SilentException` / writes `Error:` and returns). Preferred shape: override `ExecuteRemoteCommand`, call
  `ThemeRequestBuilder` first, and on failure `Logger.WriteError($"Error: {msg}")` + `CommandSuccess = false` +
  `return` — no `ApplicationClient.ExecutePostRequest` is issued (satisfies AC-06/AC-07/AC-08 "without an HTTP
  call"). `RemoteCommand.Execute` then returns exit 1 because `CommandSuccess == false`.
- **Response parsing — shared `ThemeServiceResponse` + one parse helper.** All three `ProceedResponse` overrides
  parse the identical `BaseResponse { success, errorInfo:{ errorCode, message } }` with the exact ListThemes /
  ClearThemesCache approach: `JsonSerializerOptions { PropertyNameCaseInsensitive = true }`; treat an
  **explicit `success:false` as failure** (surface `errorInfo.message`); tolerate an **empty** body as success
  (contract default) but treat a **non-empty, non-JSON** body as a failure — ThemeService always answers with
  JSON, so a non-JSON payload (e.g. an auth-redirect login page) means the request never reached the service and
  must not be reported as success; the diagnostic surfaces the raw response, matching `delete-package` (FR-09). To avoid 3× duplication, extract a shared `ThemeServiceResponse` record + a static
  `ThemeServiceResponseParser.TryGetFailure(string response, out string errorMessage)` in
  `clio/Command/ThemeServiceResponse.cs`. `ListThemesCommand` and `ClearThemesCacheCommand` keep their private
  copies as shipped (do not refactor the siblings — they are out of scope per the PRD non-goal "will not change
  the behavior of the already-shipped commands"); the new shared record lives alongside the new commands.

**Create exposes a non-logging `TryCreateTheme` data method; update/delete go through the log-envelope.** (See D5
for why this asymmetry is correct and matches the sibling precedent.)

#### `create-theme` specifics (FR-05, FR-08, OQ-04)
- `--id` optional → when omitted, generate a UUID v4 (`Guid.NewGuid().ToString("D")`) — matches `^[A-Za-z0-9_-]+$`
  (hyphens allowed) and is surfaced back to the caller (CLI prints it; MCP returns it).
- `--css-class-name` and CSS content are required; `--caption` is optional and is derived from `--css-class-name`
  when omitted (e.g. `ocean-theme` → `Ocean`, dropping a trailing `theme` word and title-casing).
- `--package-name` optional → resolved to UId via `PageSchemaMetadataHelper.QueryPackageUId`; when omitted, omit the
  `packageUId` key entirely (no redundant zero GUID on the wire) so the server falls back to the `CurrentPackageId`
  system setting (AC-03). `PageSchemaMetadataHelper` is `internal static` in the same `Clio.Command` namespace, so
  the create command calls `QueryPackageUId(ApplicationClient, _urlBuilder, options.PackageName)` directly.
- **No client-side "id already exists" pre-check** (OQ-04): rely on the server's `InvalidOperationException`
  surfaced via `errorInfo.message`.

#### `update-theme` specifics (FR-06, OQ-03)
- Full overwrite: `--id`, `--caption`, `--css-class-name`, and CSS content all required. **No** `--package-name`
  (UpdateTheme cannot re-home a theme; and `GetAvailableThemes` returns `cssFilePath`, not `cssContent`, so a
  read-modify-write partial update is not feasible anyway). No read-before-write.

#### `delete-theme` specifics (FR-07, OQ-02)
- `--id` only. **Not idempotent at the clio layer**: no existence pre-check; a server `success:false` (e.g.
  unknown id) surfaces as `Error: …` and exit 1 (AC-05).

### D4 — Routes: three additive `KnownRoute` entries 44/45/46 (FR-13, OQ-05)

`ClearThemesCache = 43` is the verified current max, so:

```csharp
// clio/Common/ServiceUrlBuilder.cs — KnownRoute additions (continue the sequence)
CreateTheme = 44,
UpdateTheme = 45,
DeleteTheme = 46
// KnownRoutes map:
{ KnownRoute.CreateTheme, "ServiceModel/ThemeService.svc/CreateTheme" },
{ KnownRoute.UpdateTheme, "ServiceModel/ThemeService.svc/UpdateTheme" },
{ KnownRoute.DeleteTheme, "ServiceModel/ThemeService.svc/DeleteTheme" },
```

`ServiceUrlBuilder.Build(KnownRoute)` prepends `0/` for `.NET Framework` (`IsNetCore = false`) automatically — do
not bake it into the route string (matches the two existing `ThemeService.svc` routes).

### D5 — MCP tools: three `BaseTool<TOptions>`, env-aware, structured create / log-envelope update+delete (FR-11, FR-12)

Each tool derives from `BaseTool<TOptions>` and exposes `-by-environment` + `-by-credentials`, executed via the
env-aware path so a fresh command is resolved for the per-call environment (never the stale startup-time instance).
Tools take **inline `cssContent` only** (no `--css-content-file` equivalent — A-06). Descriptions route to
`get-guidance theming`.

**No `BaseTool.ResolveFromCallContainer` switch edit is required.** The switch (`BaseTool.cs` ll. 110–127) has
special arms only for the four *local-resolution* options types (`CreateTestProjectOptions`, `AddPackageOptions`,
`CreateWorkspaceCommandOptions`, `CreateUiProjectOptions`) that can run with no environment, then a generic
`EnvironmentOptions envOptions => commandResolver.Resolve<TService>(envOptions)` arm. `Create/Update/DeleteThemeOptions`
derive from `RemoteCommandOptions : EnvironmentOptions` and are always environment-bound, so they fall through to
the generic `EnvironmentOptions` arm. Unlike the workspace/local-creation tools (`CreateUiProjectOptions`,
`CreateWorkspaceCommandOptions`, `AddPackageOptions`, `CreateTestProjectOptions`) that get a dedicated
`ResolveWithoutEnvironment` arm because they can run with no environment, these server-flow options are never
local-only — so the generic arm is correct and sufficient and **no switch edit is needed**. (There is no theme arm
in the current switch — verified, `BaseTool.cs` ll. 110–127 — and none should be added; do not reference a removed
prototype's `CreateThemeOptions` here.) **Implementer must verify** at build time that the new tools resolve through
the generic arm (a unit test asserting env-aware resolution covers this).

**Tool result shape (asymmetric, by design):**

| Tool | Path | Result type | Rationale |
|------|------|-------------|-----------|
| `create-theme` | `ExecuteWithCleanLog` + `ResolveCommand<CreateThemeCommand>` + `TryCreateTheme` | structured `CreateThemeResult { success, id, error? }` (mirrors `ListThemesResult`) | The agent must learn the **generated id** when `--id` was omitted, to follow up with `update-theme` / `delete-theme` / set-default. A log-envelope would bury the id in free text. |
| `update-theme` | `InternalExecute<UpdateThemeCommand>` (log envelope) | `CommandExecutionResult` (mirrors `ClearThemesCacheTool`) | Update keys on a caller-supplied id; there is no generated value to return. |
| `delete-theme` | `InternalExecute<DeleteThemeCommand>` (log envelope) | `CommandExecutionResult` | Same — id is caller-supplied; pass/fail + message is enough. |

So `create-theme` exposes a non-logging `TryCreateTheme(options, out string createdId, out string errorMessage)`
on `CreateThemeCommand` (the `ListThemesCommand.TryGetAvailableThemes` pattern); `update`/`delete` need no such
data method and run through the standard `BaseTool` log-envelope. By-credentials variants validate via
`CommandExecutionResult.ValidateCredentials` (update/delete) or inline `*Result.Failure(...)` guards (create,
like `ListThemesTool`).

**Safety flags (FR-12), `OpenWorld=false` on all three:**

| Tool | ReadOnly | Destructive | Idempotent |
|------|----------|-------------|------------|
| `create-theme-by-*` | false | false | false |
| `update-theme-by-*` | false | false | true |
| `delete-theme-by-*` | false | **true** | false |

MCP tool classes carry `[McpServerToolType]` (inherited from `BaseTool<T>`) and are **auto-discovered** by
assembly scan through `McpFeatureToggleFilter.RegisterEnabledPrimitives` (`IEnumerable<Type>`); there is **no**
manual tool list to append to in `BindingsModule`. **Never** pass a `Type[]` and never reintroduce
`*FromAssembly` (RR-04 — silently registers nothing).

### D6 — Guidance edit: flip "No-code / server flow" to available (FR-15, AC-11)

Edit `ThemingGuidanceResource.Guide` (do not rewrite it):

1. Under **"Which flow"**, replace `- No-code / server flow — not yet available in clio.` with a line that
   describes the now-available server flow and routes to a new body section, e.g.
   `- No-code / server flow — use it when you have only a registered environment and credentials (no workspace/package) — see "No-code / server flow".`
2. Add a **"No-code / server flow"** body section after the existing "Workspace / dev flow" section:
   - Prerequisites: a registered environment + `CanCustomizeBranding` license + `CanManageThemes` operation.
   - Create with `create-theme-by-environment` (css-class-name + inline cssContent required; `--caption` optional →
     derived from css-class-name when omitted; `--id` optional →
     auto-generated and returned; `--package-name` optional → omitted means the `CurrentPackageId` system setting).
   - Restyle with `update-theme-by-environment` (full overwrite by id; no package parameter — cannot re-home).
   - Delete with `delete-theme-by-environment` (by id; not idempotent — deleting an unknown id is an error). Cross-
     reference the existing default-theme caveat ("If you delete the theme that is currently the default…") already
     in the "Get / set the default theme" section.
   - Confirm with `list-themes-by-environment`.
3. Keep the shared **"Source of truth — @creatio/theming"**, **"List themes"**, and
   **"Get / set the default theme"** sections unchanged. **Do not restate** the `--crt-*` token catalog or
   authoring rules — the section stays a thin pointer (CM-03 / single source of truth).

No `GuidanceCatalog` change is needed — the `["theming"]` entry already exists and resolves to the same
`ThemingGuidanceResource.Guide`. The existing `get-guidance theming` discovery test continues to cover resolution;
extend it to assert the server-flow text is present and the token catalog is *not* restated.

### D7 — DI + dispatch wiring (FR-14)

- `clio/BindingsModule.cs` (next to the shipped `services.AddTransient<ListThemesCommand>();`, ~l. 475):
  `services.AddTransient<CreateThemeCommand>();`, `…<UpdateThemeCommand>();`, `…<DeleteThemeCommand>();`.
  The MCP tools are constructor-injected with these commands, so the registrations are *consumed* (no CLIO005
  dead-registration risk). No tool registration line — tools are auto-discovered.
- `clio/Program.cs`:
  - Add `typeof(CreateThemeOptions)`, `typeof(UpdateThemeOptions)`, `typeof(DeleteThemeOptions)` to the
    `CommandOption` type list (next to `ListThemesOptions` ~l. 203).
  - Add three dispatch arms in `ExecuteCommandWithOption` (next to the `ListThemesOptions` arm ~l. 288):
    `CreateThemeOptions opts => Resolve<CreateThemeCommand>(opts).Execute(opts),` and the same for update/delete.

---

## Alternatives Considered

| Option | Pros | Cons | Status |
|--------|------|------|--------|
| **B (chosen): three flat options + commands + tools, one static CSS/validation helper, shared response record** | Mirrors shipped siblings exactly; minimal new surface; correct escaping via serialized record; create returns generated id | Three near-identical command shells; one borderline static helper under CLIO001 | **Chosen** |
| A: shared abstract `ThemeWriteOptions` base + `ThemeWriteCommand<T>` base + shared CSS mixin | Less duplicated `[Option]` text | CommandLineParser gains nothing from an options base; hides per-verb required-flag differences in `-H`; over-abstracts three small verbs; diverges from sibling convention | Rejected: abstraction cost > the data duplication it removes |
| A′: CSS resolution/validation as a DI `IThemeRequestBuilder` service | "Pure DI" purity; mockable | Adds an interface + registration + a mock with no seam any test needs (file I/O covered by temp files; validation is deterministic); contradicts the `PageSchemaMetadataHelper` static-helper precedent | Rejected: static utility is the documented borderline-OK case |
| C: client-side existence pre-checks (create id-exists, delete not-found → no-op) | "Friendlier" messages; idempotent delete | Extra round-trips; races the server; OQ-02/OQ-04 explicitly resolved against it; server already returns precise errors | Rejected by OQ-02 / OQ-04 |
| D: hand-built JSON string for the request body | No serializer dependency | `cssContent` escaping (quotes/newlines/braces, up to 1 MiB) is a correctness landmine | Rejected: must serialize a record |
| E: gate behind `[FeatureToggle]` until the server flow stabilizes | Hidden while immature | Runtime auth already gates use; siblings shipped enabled; task mandates public docs/guidance flip | Rejected by OQ-01 |

---

## Implementation Plan

### Files to create

| File | Purpose |
|------|---------|
| `clio/Command/CreateThemeCommand.cs` | `CreateThemeOptions : RemoteCommandOptions` (`[Verb("create-theme")]`) + `CreateThemeCommand : RemoteCommand<CreateThemeOptions>`; `ServicePath = Build(KnownRoute.CreateTheme)`; CSS resolve + FR-10 validate + `--package-name`→UId before HTTP; serialized camelCase body; `ProceedResponse` parses `ThemeServiceResponse`; non-logging `TryCreateTheme(out id, out error)` for MCP |
| `clio/Command/UpdateThemeCommand.cs` | `UpdateThemeOptions : RemoteCommandOptions` (`[Verb("update-theme")]`) + `UpdateThemeCommand`; `Build(KnownRoute.UpdateTheme)`; full-overwrite body (no package); shared validate + parse |
| `clio/Command/DeleteThemeCommand.cs` | `DeleteThemeOptions : RemoteCommandOptions` (`[Verb("delete-theme")]`) + `DeleteThemeCommand`; `Build(KnownRoute.DeleteTheme)`; `{ id }` body; shared parse (success:false → fail, not idempotent) |
| `clio/Command/ThemeRequestBuilder.cs` | `internal static` helper: `--css-content` xor `--css-content-file` resolution (UTF-8 read, fail-fast on missing/unreadable), FR-10 field validation (`id`/`caption`/`cssClassName` regex+length, `cssContent` ≤1 MiB, empty-ok-null-not) |
| `clio/Command/ThemeServiceResponse.cs` | Shared `ThemeServiceResponse { bool? Success; ThemeServiceErrorInfo ErrorInfo }` record + `internal static` `ThemeServiceResponseParser.TryGetFailure(response, out errorMessage)` reused by the three new commands |
| `clio/Command/McpServer/Tools/CreateThemeTool.cs` | `BaseTool<CreateThemeOptions>`; `create-theme-by-environment` / `-by-credentials`; `ExecuteWithCleanLog` + `ResolveCommand<CreateThemeCommand>.TryCreateTheme`; structured `CreateThemeResult { success, id, error? }`; flags `false/false/false`, `OpenWorld=false`; description → `get-guidance theming` |
| `clio/Command/McpServer/Tools/UpdateThemeTool.cs` | `BaseTool<UpdateThemeOptions>`; `update-theme-by-*`; `InternalExecute<UpdateThemeCommand>`; flags `false/false/true`, `OpenWorld=false` |
| `clio/Command/McpServer/Tools/DeleteThemeTool.cs` | `BaseTool<DeleteThemeOptions>`; `delete-theme-by-*`; `InternalExecute<DeleteThemeCommand>`; flags `false/true/false`, `OpenWorld=false` |
| `clio/help/en/create-theme.txt` | CLI `-H` help |
| `clio/help/en/update-theme.txt` | CLI `-H` help |
| `clio/help/en/delete-theme.txt` | CLI `-H` help |
| `clio/docs/commands/create-theme.md` | Detailed GitHub docs |
| `clio/docs/commands/update-theme.md` | Detailed GitHub docs |
| `clio/docs/commands/delete-theme.md` | Detailed GitHub docs |
| `clio.tests/Command/CreateThemeCommand.Tests.cs` | Unit: route build, camelCase body, auto-UUID, packageUId key omitted when no --package-name, css mutual-exclusion, FR-10 validation, `BaseResponse` parse |
| `clio.tests/Command/UpdateThemeCommand.Tests.cs` | Unit: route build, full-overwrite body (no package), css resolution, validation, parse |
| `clio.tests/Command/DeleteThemeCommand.Tests.cs` | Unit: route build, `{ id }` body, success:false→fail, empty→success, non-JSON→fail |
| `clio.tests/Command/McpServer/CreateThemeToolTests.cs` | Unit: arg mapping, env-aware resolution, structured result carries id, flags |
| `clio.tests/Command/McpServer/UpdateThemeToolTests.cs` | Unit: arg mapping, env-aware `InternalExecute`, flags (Idempotent=true) |
| `clio.tests/Command/McpServer/DeleteThemeToolTests.cs` | Unit: arg mapping, env-aware `InternalExecute`, flags (Destructive=true) |
| `clio.mcp.e2e/CreateThemeToolE2ETests.cs` | E2E: both create variants advertised by the real `clio mcp-server` (NOT in CI — manual) |
| `clio.mcp.e2e/UpdateThemeToolE2ETests.cs` | E2E: both update variants advertised (NOT in CI — manual) |
| `clio.mcp.e2e/DeleteThemeToolE2ETests.cs` | E2E: both delete variants advertised (NOT in CI — manual) |

### Files to modify

| File | Change description |
|------|-------------------|
| `clio/Common/ServiceUrlBuilder.cs` | Add `KnownRoute.CreateTheme = 44`, `UpdateTheme = 45`, `DeleteTheme = 46` + three `KnownRoutes` entries `ServiceModel/ThemeService.svc/{Create,Update,Delete}Theme` (**Common/ → full-suite trigger, RR-03**) |
| `clio/Program.cs` | Add three `typeof(*ThemeOptions)` to `CommandOption` list (~l. 203) + three dispatch arms in `ExecuteCommandWithOption` (~l. 288) (**dispatch chokepoint → full-suite trigger**) |
| `clio/BindingsModule.cs` | `AddTransient<{Create,Update,Delete}ThemeCommand>()` (~l. 475) (**DI composition root → full-suite trigger, RR-01**) |
| `clio/Command/McpServer/Resources/ThemingGuidanceResource.cs` | Flip "No-code / server flow — not yet available" to available; add "No-code / server flow" body section; route from "Which flow"; keep shared sections + token-catalog pointer (D6, RR-02) |
| `clio/Commands.md` | Add `create-theme` / `update-theme` / `delete-theme` index + section entries |
| `clio/Wiki/WikiAnchors.txt` | Add anchors for the three verbs |
| `docs/McpCapabilityMap.md` | Add the six tool variants + note the updated `docs://mcp/guides/theming` resource |
| `clio.tests/Command/McpServer/GuidanceGetToolTests.cs` | Extend: assert `theming` server-flow section present + token catalog not restated (CM-03) |

### Files to delete

None. This is purely additive; the prototype `new-theme` stack was already removed under ENG-90636 (D5 of the
sibling ADR).

### Key interfaces / contracts

```csharp
// clio/Command/CreateThemeCommand.cs
[Verb("create-theme", HelpText = "Create a custom Creatio theme on the target environment via ThemeService")]
public class CreateThemeOptions : RemoteCommandOptions {
    [Option("id", Required = false, HelpText = "Theme id (^[A-Za-z0-9_-]+$, ≤100). Omitted → auto-generated UUID v4.")]
    public string Id { get; set; }
    [Option("caption", Required = false, HelpText = "Human-readable theme caption (≤250); derived from css-class-name when omitted")]
    public string Caption { get; set; }
    [Option("css-class-name", Required = true, HelpText = "CSS class applied when active (^[A-Za-z][A-Za-z0-9_-]*$, ≤100)")]
    public string CssClassName { get; set; }
    [Option("css-content", Required = false, HelpText = "Inline theme CSS (mutually exclusive with --css-content-file)")]
    public string CssContent { get; set; }
    [Option("css-content-file", Required = false, HelpText = "Path to a UTF-8 CSS file (mutually exclusive with --css-content)")]
    public string CssContentFile { get; set; }
    [Option("package-name", Required = false, HelpText = "Owning package NAME; omitted → CurrentPackageId system setting")]
    public string PackageName { get; set; }
}

public class CreateThemeCommand : RemoteCommand<CreateThemeOptions> {
    private readonly IServiceUrlBuilder _urlBuilder;
    public CreateThemeCommand(IApplicationClient applicationClient, EnvironmentSettings settings, IServiceUrlBuilder urlBuilder)
        : base(applicationClient, settings) => _urlBuilder = urlBuilder;
    protected override string ServicePath => _urlBuilder.Build(ServiceUrlBuilder.KnownRoute.CreateTheme);

    // Non-logging data method for the MCP tool (mirrors ListThemesCommand.TryGetAvailableThemes):
    // resolves CSS + validates (ThemeRequestBuilder), resolves packageUId, posts the camelCase body,
    // parses ThemeServiceResponse; returns the effective id (generated when --id omitted).
    public virtual bool TryCreateTheme(CreateThemeOptions options, out string createdId, out string errorMessage);
    // GetRequestData: serialize record { id, caption, cssClassName, cssContent, packageUId } camelCase.
    // ProceedResponse: ThemeServiceResponseParser.TryGetFailure(...) → CommandSuccess=false + Logger.WriteError.
}
```

```csharp
// update-theme body: { id, caption, cssClassName, cssContent }  — NO packageUId
// delete-theme body: { id }
```

```csharp
// clio/Command/McpServer/Tools/CreateThemeTool.cs (shape)
[McpServerTool(Name = "create-theme-by-environment", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false),
 Description("Create a custom Creatio theme on a registered environment. Returns { success, id, error? } — " +
   "id is the created theme id (auto-generated when omitted). For the theme workflow, read get-guidance theming first.")]
public CreateThemeResult CreateThemeByName(
    [Required] string environmentName, [Required] string cssClassName, [Required] string cssContent,
    string caption = null, string id = null, string packageName = null) { /* ResolveCommand + TryCreateTheme via ExecuteWithCleanLog; caption derived from cssClassName when omitted */ }
```

### CLI flag specification

| Verb | Flag | Type | Required | Notes |
|------|------|------|----------|-------|
| create-theme | `--id` | string | No | omitted → auto UUID v4 |
| create-theme | `--caption` | string | No | ≤250; derived from css-class-name when omitted |
| create-theme | `--css-class-name` | string | Yes | `^[A-Za-z][A-Za-z0-9_-]*$` ≤100 |
| create-theme | `--css-content` | string | one-of | xor `--css-content-file` |
| create-theme | `--css-content-file` | string | one-of | xor `--css-content`; UTF-8 file |
| create-theme | `--package-name` | string | No | package NAME → UId; omitted → CurrentPackageId |
| update-theme | `--id` | string | Yes | existing theme id |
| update-theme | `--caption` | string | Yes | full overwrite |
| update-theme | `--css-class-name` | string | Yes | full overwrite |
| update-theme | `--css-content` / `--css-content-file` | string | one-of | xor |
| delete-theme | `--id` | string | Yes | not idempotent |
| (all) | inherited `RemoteCommandOptions` (`-e/--environment`, URI/login/password, `--timeout`) | — | per base | standard remote options |

All long names are kebab-case (CLIO001 — FR-19). No existing flags renamed; no aliases required.

### Test strategy

| Layer | Framework | What to cover | File(s) |
|-------|-----------|---------------|---------|
| Unit | `BaseCommandTests<TOptions>` + NSubstitute | route build (`KnownRoute.{Create,Update,Delete}Theme` → `ServiceModel/ThemeService.svc/<Method>`, `0/`-prefixed on NetFW); camelCase request body; create auto-UUID + packageUId key omitted when no `--package-name` (sent only when `--package-name`→UId resolves); css `--css-content` xor `--css-content-file`; FR-10 validation (regex/length/empty-ok-null-not); `ThemeServiceResponse` parse (success / success=false+errorInfo / empty→success / non-empty non-JSON→failure) | `clio.tests/Command/{Create,Update,Delete}ThemeCommand.Tests.cs` |
| Unit | NSubstitute | MCP arg mapping; env-aware resolution (generic `EnvironmentOptions` arm); create structured result carries id; safety flags (FR-12); description → `get-guidance theming` | `clio.tests/Command/McpServer/{Create,Update,Delete}ThemeToolTests.cs` |
| Unit | NSubstitute | `get-guidance theming` resolves; server-flow section present; token catalog not restated | `clio.tests/Command/McpServer/GuidanceGetToolTests.cs` (extend) |
| Integration | Real FS temp files | `--css-content-file` UTF-8 read; missing/unreadable file → `Error:` no HTTP | `clio.tests/Command/{Create,Update}ThemeCommand.Tests.cs` (`[Category("Integration")]` cases) |
| E2E (MCP) | `clio.mcp.e2e` (NOT in CI — manual) | all six variants advertised by real `clio mcp-server` with correct flags; representative create→update→delete round-trip | `clio.mcp.e2e/{Create,Update,Delete}ThemeToolE2ETests.cs` |
| E2E (live stand) | Manual runbook | full create → list → update → delete against a real Creatio with the required license/operation | Manual |

All unit tests: `[Category("Unit")]`, `MethodName_ShouldExpectedBehavior_WhenCondition`, explicit AAA, a `because`
per assertion, `[Description]` per test. **MCP E2E is not in CI — manual only (flag in the test plan).**
**Full-suite trigger (smart-regression rule 4): `BindingsModule.cs`, `Program.cs`, and `Common/ServiceUrlBuilder.cs`
all change** — run the full `Category=Unit` suite in addition to targeted `Module=Command|Common|McpServer`.

## Consequences

- **Positive**
  - Completes the no-code theme catalog (read + cache + create + update + delete) so the Toolkit vibe-coder
    manages themes end to end with no workspace and no push.
  - Reuses the shipped `RemoteCommand` + `ThemeService` + `BaseTool` patterns verbatim; the only genuinely new
    code is request-body construction, the shared CSS/validation helper, and the create structured result.
  - `create-theme` returns the generated id, so an agent can chain update/delete/set-default without a list round-trip.
  - Purely additive: no behavior change to `list-themes` / `clear-themes-cache` (CM-01) and no MCP channel-noise
    regression (CM-02 — create uses the non-logging data path; update/delete use the clean log envelope).
- **Trade-offs / risks**
  - **RR-04 (MCP registration hazard):** tools register via `McpFeatureToggleFilter.RegisterEnabledPrimitives`
    (`IEnumerable<Type>`). Never a `Type[]`, never `*FromAssembly` — either silently registers nothing.
  - **RR-05 (unsanitized cssContent):** clio sends `cssContent` verbatim (the server does not sanitize either);
    the only client-side guard is the FR-10 length/format validation. CSS is opaque text to clio — this is a
    deliberate scope decision, documented in the PRD non-goals.
  - **Stacked-branch dependency:** this work sits on top of ENG-90636; merge order matters
    (`KnownRoute.ClearThemesCache = 43` and the sibling commands/guidance must land first).
  - **Static helper under CLIO001:** `ThemeRequestBuilder` is an intentional `internal static` utility, justified
    above; if CLIO001 flags it, fix by confirming the `PageSchemaMetadataHelper` precedent, not by suppressing.
  - **A-01/A-02 contract assumptions:** the three write endpoints must exist and return the same
    `BaseResponse { success, errorInfo }` shape on supported Creatio versions; if not, a version gate may be needed
    later (out of scope here).
- **Breaking change**: No. Three new verbs + flags, three additive `KnownRoute` entries, three additive MCP tools,
  an additive guidance section. A standard "added `create-theme` / `update-theme` / `delete-theme`" note in
  `RELEASE.md` is sufficient; no migration path.

## Post-review refinements (bmad-reviewer, 2026-06-21)

The adversarial review found no blockers and confirmed the design is sound against the codebase. These are the
binding clarifications it surfaced — they REFINE the decisions above; the implementer and story-writer treat them
as authoritative.

**R-01 (was L1-01) — `create-theme` silent vs. logging parse split (the one that matters).** `CreateThemeCommand`
must mirror `ListThemesCommand`'s split exactly: a private/static *silent* parse (the ListThemes `TryParseThemes`
shape — here the shared `ThemeServiceResponseParser.TryGetFailure`) used by BOTH paths, with the **logging** living
only in the CLI path. Concretely:
- `TryCreateTheme(options, out string createdId, out string errorMessage)` resolves CSS + validates
  (`ThemeRequestBuilder`), resolves `packageUId`, POSTs the body, then parses the response **inline via the shared
  parser** and returns `(false, errorMessage)` / `(true, createdId)`. It writes **nothing** to the logger (CM-02 —
  no log noise on the MCP channel) and never calls `ProceedResponse`.
- The CLI path is `ExecuteRemoteCommand` → call `TryCreateTheme` → on success `Logger.WriteInfo($"Created theme
  '{createdId}'")` (R-02), on failure `Logger.WriteError($"Error: {errorMessage}")` + `CommandSuccess = false`.
  `create-theme` therefore overrides `ExecuteRemoteCommand` (not `ProceedResponse`) — the same separation
  `ListThemesCommand` uses. update/delete keep the simpler `ProceedResponse` override (no data method needed).

**R-02 (was L1-02) — CLI echoes the generated id.** When `--id` is omitted the CLI must print the effective id
(`Logger.WriteInfo($"Created theme '{id}'")`) so a human/agent on the CLI surface can chain update/delete/set-default
without a `list-themes` round-trip (AC-02).

**R-03 (was L1-04) — `--package-name` resolution failure path.** `PageSchemaMetadataHelper.QueryPackageUId` returns
`(null, "Package 'X' not found…")` on a miss. create must fail fast on a non-null error: surface `Error: {error}`,
exit 1, and issue **no** `CreateTheme` HTTP call — the same fail-fast block as CSS/FR-10 validation. The `TryCreateTheme`
data method returns `(false, error)` in this case. A `null` UId is never sent to the service.

**R-04 (was L2-02) — validation lives in the command/data method, shared by both surfaces.** `ThemeRequestBuilder`
CSS-resolution + FR-10 validation (incl. the 1 MiB `cssContent` cap) runs inside the command (the `TryCreateTheme`/
`ExecuteRemoteCommand` body), NOT in the options layer — so the MCP path enforces the identical limits as the CLI
(no surface can push >1 MiB while the other rejects it).

**R-05 (was L2-03) — empty-vs-absent CSS matrix.** CommandLineParser binds an unsupplied string option to `null` and
a supplied-but-empty one to `""`. `ThemeRequestBuilder` distinguishes them: both `--css-content` and
`--css-content-file` absent (`null`) → `Error:` (no content); `--css-content ""` present → **valid** (empty CSS is
allowed, null is not — FR-10); `--css-content-file` pointing at an empty file → valid empty CSS; both present → `Error:`
(mutually exclusive). Same logic on create and update.

**R-06 (was L2-04) — re-validate the auto-UUID.** The generated id (`Guid.NewGuid().ToString("D")`) is routed through
the same FR-10 id validation as a user-supplied id (defence in depth — a future generator change can't emit an
id that fails `^[A-Za-z0-9_-]+$`).

**R-07 (was L2-01) — update/delete log failures as errors.** The update/delete `ProceedResponse` overrides call
`Logger.WriteError(...)` (not `WriteInfo`) on `success:false`, so the MCP log-envelope carries the failure as an
`ErrorMessage` and the exit code is non-zero (matches `ClearThemesCacheCommand` ll. 56–62; satisfies AC-09/AC-ERR
for the update/delete surface).

**R-08 (was L1-03 / A-05) — omitted-`packageUId` server fallback is a live-stand verification item.** When
`--package-name` is omitted the `packageUId` key is dropped from the request body entirely (no redundant zero GUID
on the wire). The assumption that a missing `packageUId` makes the server use `CurrentPackageId` (AC-03) is per the
documented `IThemeService` contract but is NOT unit-verifiable. Added to the manual live-stand runbook: a
`create-theme` with `--package-name` omitted must succeed and land in the CurrentPackageId package. If the server
distinguishes a missing key from an empty GUID (e.g. a nullable contract) and rejects the omission, the default
create path is broken and needs a follow-up.

**R-09 (was L3-01/L3-02/L3-03) — AC testability split (for the test plan).**
- AC-04 "overwritten **in its current package**" is a live-stand assertion; the clio **unit** assertion is that the
  update body contains `{id, caption, cssClassName, cssContent}` and **no `packageUId` key**.
- AC-10 "advertised by the real `clio mcp-server`" is satisfied **only by the manual `clio.mcp.e2e` tests (NOT in
  CI)**; unit tests assert the safety-flag attribute values on the tool classes, not the live tool manifest.
- AC-ERR splits by surface: CLI → `Error: {message}` + exit 1; MCP create → structured `{success:false, error}`;
  MCP update/delete → `ErrorMessage` in `execution-log-messages` + non-zero `exit-code`.

**R-10 (review note (b)) — body-assertion tests expect JSON escaping.** `System.Text.Json` escapes non-ASCII and
`<`/`>`/`&` to `\uXXXX` by default. Unit tests that assert on the raw request-body string must expect the escaped
form (or assert on a deserialized object), not literal characters. (Optionally configure
`UnsafeRelaxedJsonEscaping` if a test needs literal output — but the server accepts the escaped form, so default
escaping is fine and tests should match it.)

## Pre-implementation Checklist

- [ ] All new CLI option long names are kebab-case (`--id`, `--caption`, `--css-class-name`, `--css-content`,
      `--css-content-file`, `--package-name`) — CLIO001
- [ ] Three commands registered in `BindingsModule.cs` and wired (CommandOption list + dispatch switch) in `Program.cs`
- [ ] `KnownRoute.CreateTheme = 44` / `UpdateTheme = 45` / `DeleteTheme = 46` map to `ServiceModel/ThemeService.svc/<Method>`
- [ ] Request bodies built by serializing a record (camelCase) — no string interpolation; `cssContent` escaping verified
- [ ] CSS resolution + mutual-exclusion + FR-10 validation run *before* any HTTP call (fail fast with `Error:`)
- [ ] `ProceedResponse` parses the shared `ThemeServiceResponse`; `success:false` and non-empty non-JSON bodies are failures; only an empty body is tolerated as success
- [ ] `create-theme` returns the effective id (generated when `--id` omitted); MCP create result carries it
- [ ] `delete-theme` is NOT idempotent (no pre-check; server error → exit 1); MCP `Idempotent=false`
- [ ] MCP safety flags per FR-12; `OpenWorld=false`; descriptions route to `get-guidance theming`
- [ ] MCP tools resolve through the generic `EnvironmentOptions` arm — no `BaseTool` switch edit (verify with a test)
- [ ] MCP tools registered via `McpFeatureToggleFilter.RegisterEnabledPrimitives` (`IEnumerable<Type>`) — no `Type[]`, no `*FromAssembly`
- [ ] No `[FeatureToggle]`, no ClioGate method, no cliogate bump, no `[RequiresPackage]`
- [ ] `ThemingGuidanceResource` server-flow section added; token catalog NOT restated; shared sections kept
- [ ] Docs (`help/en`, `docs/commands`, `Commands.md`, `Wiki/WikiAnchors.txt`, `docs/McpCapabilityMap.md`) updated
- [ ] `clio.mcp.e2e` coverage added for all six variants (NOT in CI — manual); unit mapping tests are insufficient alone
- [ ] Full unit suite run (BindingsModule/Program/Common touched) + targeted `Module=Command|Common|McpServer`
- [ ] `.codex/workspace-diary.md` entry appended
