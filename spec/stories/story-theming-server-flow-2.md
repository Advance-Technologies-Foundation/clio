# Story 2: `create-theme` CLI command

**Feature**: theming-server-flow (ENG-91387 — Theming with AI, Toolkit / no-code server flow, Contour B)
**FR coverage**: FR-01, FR-05, FR-08, FR-09, FR-14, FR-19
**PRD**: [prd-theming-server-flow.md](../prd/prd-theming-server-flow.md)
**ADR**: [adr-theming-server-flow.md](../adr/adr-theming-server-flow.md)
**Status**: ready-for-dev
**Size**: L (full day)

> **Depends on Story 1** (`KnownRoute.CreateTheme = 44`, `ThemeRequestBuilder`, `ThemeServiceResponse`).
> `create-theme` carries the most surface of the three verbs: optional `--id` with auto-UUID, the
> `--package-name`→UId resolution with a fail-fast error path, the camelCase serialized body, and the
> non-logging `TryCreateTheme` data method the MCP create tool (Story 4) consumes. It overrides
> `ExecuteRemoteCommand` (not `ProceedResponse`) so the CLI surface logs while the data method stays silent (R-01).

---

## As a

AI coding agent (vibe-coder via the Creatio AI Toolkit), or a developer scripting from the CLI

## I want

a `create-theme` verb that creates a custom theme on a target environment from inline CSS or a CSS file, with an
optional id and optional owning package

## So that

I can deliver branding to a no-code Creatio instance with no workspace and no push, and learn the created theme id
to chain `update-theme` / `delete-theme` / set-default

---

## Acceptance Criteria

- [ ] **AC-01** — Given a registered environment and a caller with `CanCustomizeBranding` + `CanManageThemes`, when
  `clio create-theme --caption "X" --css-class-name "x-theme" --css-content "…" -e <env>` runs, then the command
  POSTs to `ServiceModel/ThemeService.svc/CreateTheme` (via `KnownRoute.CreateTheme`), the server returns
  `success:true`, and clio exits 0. (FR-01, AC-01.)
- [ ] **AC-02** — Given `--id` omitted, when the command runs, then it generates a UUID v4
  (`Guid.NewGuid().ToString("D")`), re-validates it through the shared `id` regex (R-06), sends it as the body
  `id`, and on success prints `Created theme '<id>'` so the effective id is visible on the CLI surface (FR-05,
  AC-02, R-02). Given `--id <value>` supplied, then that value is sent verbatim (after FR-10 validation).
- [ ] **AC-03** — Given `--package-name <name>` for a package that exists, when the command runs, then
  `PageSchemaMetadataHelper.QueryPackageUId(ApplicationClient, _urlBuilder, name)` resolves it and that
  `packageUId` is sent; given `--package-name` omitted, then no `packageUId` key is sent (so the server falls
  back to the `CurrentPackageId` system setting). (FR-08, AC-03.)
- [ ] **AC-04** — Given `--package-name` that resolves to an error (`QueryPackageUId` returns a non-null error,
  e.g. "Package 'X' not found…"), when the command runs, then clio surfaces `Error: {error}`, exits non-zero, and
  issues **no** `CreateTheme` HTTP call; a `null` UId is never sent. (R-03 — same fail-fast block as CSS/FR-10
  validation.)
- [ ] **AC-05** — Given the request body is built, when serialized, then it is a `System.Text.Json`-serialized
  **record** (not string interpolation) with camelCase keys `{ id, caption, cssClassName, cssContent, packageUId }`,
  so `cssContent` (newlines, quotes, braces) is escaped correctly; tests assert on the escaped form or a
  deserialized object (R-10). (FR-01, ADR D3.)
- [ ] **AC-06** — Given both `--css-content` and `--css-content-file` (AC-06), or a missing file (AC-07), or an
  FR-10 violation (oversized/bad-regex/missing field, AC-08), when the command runs, then
  `ThemeRequestBuilder` (Story 1) fails first: clio prints `Error: …`, sets `CommandSuccess = false`, exits
  non-zero, and issues **no** HTTP call. (FR-04, FR-10, R-04, R-05.)
- [ ] **AC-07** — Given the response is parsed, when handled, then `ThemeServiceResponseParser.TryGetFailure`
  (Story 1) treats an explicit `success:false` and a non-empty non-JSON body as failures (surface
  `errorInfo.message` or the unexpected-non-JSON diagnostic), and tolerates only an empty body as success (FR-09).
  On a `success:false` from a caller lacking the license/operation
  (AC-09), clio surfaces `Error: {errorInfo.message}` and exits non-zero.
- [ ] **AC-08** — Given the MCP tool needs a non-logging path, when `CreateThemeCommand` exposes
  `virtual bool TryCreateTheme(CreateThemeOptions options, out string createdId, out string errorMessage)`, then
  that method resolves CSS + validates (`ThemeRequestBuilder`), resolves `packageUId`, POSTs the body, parses the
  response **inline via the shared parser**, and returns `(true, createdId)` / `(false, errorMessage)` while
  writing **nothing** to the logger and never calling `ProceedResponse` (R-01, CM-02). The CLI path
  (`ExecuteRemoteCommand` override) calls `TryCreateTheme` and does the logging (`WriteInfo` on success — R-02,
  `WriteError($"Error: {msg}")` + `CommandSuccess = false` on failure).
- [ ] **AC-09** — Given the verb and DI wiring, when `clio --help` / the parser lists verbs, then `create-theme`
  is reachable: `CreateThemeCommand` registered in `BindingsModule.cs`, `typeof(CreateThemeOptions)` in the
  `Program.cs` `CommandOption` list, and a dispatch arm `CreateThemeOptions opts => Resolve<CreateThemeCommand>(opts).Execute(opts)`.
  No `[RequiresPackage]`, no ClioGate (FR-14, D1, D7); `[FeatureToggle("theming")]` added later (native-build consolidation — ADR D1 SUPERSEDED).
- [ ] **AC-ERR** — Given any invalid input, package-resolution failure, or server `success:false`, clio prints
  `Error: {message}` and exits non-zero; on success it exits 0 and prints the effective id. No bare
  `catch (Exception)`; all option long-names are kebab-case (`--id`, `--caption`, `--css-class-name`,
  `--css-content`, `--css-content-file`, `--package-name` — FR-19).

## Implementation Notes

This is a `RemoteCommand<CreateThemeOptions>`, mirroring `ClearThemesCacheCommand` for the shell and
`ListThemesCommand` for the silent-data-method / logging-CLI split.

**Key file (create): `clio/Command/CreateThemeCommand.cs`** (ADR D3 create-specifics, R-01, R-02, R-03, R-06)
```csharp
[Verb("create-theme", HelpText = "Create a custom Creatio theme on the target environment via ThemeService")]
public class CreateThemeOptions : RemoteCommandOptions {
    [Option("id", Required = false, HelpText = "Theme id (^[A-Za-z0-9_-]+$, ≤100). Omitted → auto-generated UUID v4.")] public string Id { get; set; }
    [Option("caption", Required = false, HelpText = "Human-readable theme caption (≤250); derived from css-class-name when omitted")] public string Caption { get; set; }
    [Option("css-class-name", Required = true, HelpText = "CSS class applied when active (^[A-Za-z][A-Za-z0-9_-]*$, ≤100)")] public string CssClassName { get; set; }
    [Option("css-content", Required = false, HelpText = "Inline theme CSS (mutually exclusive with --css-content-file)")] public string CssContent { get; set; }
    [Option("css-content-file", Required = false, HelpText = "Path to a UTF-8 CSS file (mutually exclusive with --css-content)")] public string CssContentFile { get; set; }
    [Option("package-name", Required = false, HelpText = "Owning package NAME; omitted → CurrentPackageId system setting")] public string PackageName { get; set; }
}

public class CreateThemeCommand : RemoteCommand<CreateThemeOptions> {
    private readonly IServiceUrlBuilder _urlBuilder;
    public CreateThemeCommand(IApplicationClient applicationClient, EnvironmentSettings settings, IServiceUrlBuilder urlBuilder)
        : base(applicationClient, settings) => _urlBuilder = urlBuilder;
    protected override string ServicePath => _urlBuilder.Build(ServiceUrlBuilder.KnownRoute.CreateTheme);

    // Non-logging data method for the MCP tool (mirrors ListThemesCommand.TryGetAvailableThemes):
    //   ThemeRequestBuilder.ResolveCss + Validate  → on fail: (false, error), NO HTTP
    //   id = options.Id ?? Guid.NewGuid().ToString("D"); re-validate id (R-06)
    //   packageUId = options.PackageName == null ? null (key omitted from the body)
    //              : PageSchemaMetadataHelper.QueryPackageUId(...) — on error (non-null) → (false, error), NO HTTP (R-03)
    //   POST serialized camelCase record { id, caption, cssClassName, cssContent, packageUId? } (packageUId omitted when null)
    //   ThemeServiceResponseParser.TryGetFailure(response, out err) → (false, err) / (true, id)
    //   writes NOTHING to the logger
    public virtual bool TryCreateTheme(CreateThemeOptions options, out string createdId, out string errorMessage);

    // CLI surface: override ExecuteRemoteCommand → call TryCreateTheme;
    //   success → Logger.WriteInfo($"Created theme '{createdId}'") (R-02);
    //   failure → Logger.WriteError($"Error: {errorMessage}") + CommandSuccess = false (R-01)
}
```
- `PageSchemaMetadataHelper` is `internal static` in the same `Clio.Command` namespace — call
  `QueryPackageUId(ApplicationClient, _urlBuilder, options.PackageName)` directly (the `create-page` pattern).
- **No client-side "id already exists" pre-check** (OQ-04) — rely on the server's `InvalidOperationException`
  surfaced via `errorInfo.message`.
- Body MUST be a serialized record (R-10 escaping) — never string interpolation (Alternative D rejected).

**Key file (modify): `clio/BindingsModule.cs`** — `services.AddTransient<CreateThemeCommand>();` (~l. 475, next to
`AddTransient<ListThemesCommand>();`). **DI composition root → full-suite trigger (RR-01).** The MCP create tool
(Story 4) injects this command, so the registration is consumed (no CLIO005 risk).

**Key file (modify): `clio/Program.cs`** — add `typeof(CreateThemeOptions)` to the `CommandOption` list (~l. 203,
next to `ListThemesOptions`) + a dispatch arm in `ExecuteCommandWithOption` (~l. 288):
`CreateThemeOptions opts => Resolve<CreateThemeCommand>(opts).Execute(opts),`. **Dispatch chokepoint →
full-suite trigger.**

Pattern to follow: `ListThemesCommand` (silent `TryGetAvailableThemes` + logging `ExecuteRemoteCommand` split);
`ClearThemesCacheCommand` (`RemoteCommand` shell + `ServicePath => _urlBuilder.Build(...)`); `PageCreateCommand`
(the `PageSchemaMetadataHelper.QueryPackageUId` call site).

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | route build (`KnownRoute.CreateTheme` → `ServiceModel/ThemeService.svc/CreateTheme`, NetCore + `0/` NetFW); camelCase body `{id,caption,cssClassName,cssContent,packageUId}` (escaped per R-10); `--id` omitted → auto-UUID matches `^[A-Za-z0-9_-]+$` and is the body id; `--package-name` omitted → no `packageUId` key in the body | `clio.tests/Command/CreateThemeCommand.Tests.cs` |
| Unit `[Category("Unit")]` | `--package-name` resolves via `QueryPackageUId` → sent UId; `QueryPackageUId` error → `Error:` + `CommandSuccess=false` + **no HTTP** (R-03); CSS mutual-exclusion / missing-file / FR-10 violation → `Error:` + no HTTP (AC-06/07/08); `success:false` → `Error: errorInfo.message` + exit 1 (AC-09); empty→success, non-JSON→`Error:` + exit 1 | `clio.tests/Command/CreateThemeCommand.Tests.cs` |
| Unit `[Category("Unit")]` | `TryCreateTheme` returns `(true,id)` on success and `(false,error)` on each failure path, writing **nothing** to the logger (R-01 / CM-02); CLI `ExecuteRemoteCommand` prints `Created theme '<id>'` on success (R-02) | `clio.tests/Command/CreateThemeCommand.Tests.cs` |
| Integration `[Category("Integration")]` | `--css-content-file` UTF-8 read end-to-end through the command (real temp file); missing/unreadable file → `Error:` no HTTP | `clio.tests/Command/CreateThemeCommand.Tests.cs` (`[Category("Integration")]` cases) |

- Use `BaseCommandTests<CreateThemeOptions>` as the fixture base; resolve the command from DI; register doubles
  (`IApplicationClient`, `IServiceUrlBuilder`) in `AdditionalRegistrations`; `ClearReceivedCalls` in teardown.
- `[Category("Unit")]` (never `[Category("UnitTests")]`); naming `MethodName_ShouldBehavior_WhenCondition`
  (e.g. `TryCreateTheme_ShouldReturnGeneratedId_WhenIdOmitted`,
  `Execute_ShouldFailWithoutHttp_WhenPackageNameUnresolved`).
- Body-string assertions expect `System.Text.Json` escaping (R-10) — assert escaped form or deserialize.
- AAA + a `because` on every assertion + `[Description]` on every test.

## Definition of Done

- [ ] Code compiles without Roslyn analyzer warnings (CLIO001-CLIO005)
- [ ] All new CLI option long-names kebab-case (FR-19); no `[RequiresPackage]`, no ClioGate (D1); `[FeatureToggle("theming")]` added later (ADR D1 SUPERSEDED)
- [ ] `--id` omitted → auto-UUID generated, re-validated (R-06), sent as body id, and echoed on the CLI (R-02)
- [ ] `--package-name` resolved via `QueryPackageUId`; omitted → no `packageUId` key in the body; resolution error → fail-fast, no HTTP (R-03)
- [ ] Request body is a serialized camelCase record (no string interpolation; `cssContent` escaping verified — R-10)
- [ ] CSS resolution + FR-10 validation run before the HTTP call (fail fast with `Error:` — R-04/R-05); only `success:false` is a failure (R-01/FR-09)
- [ ] `TryCreateTheme(out id, out error)` is non-logging (R-01); CLI logging lives only in `ExecuteRemoteCommand`
- [ ] `CreateThemeCommand` registered in `BindingsModule.cs` + wired in `Program.cs` (`CommandOption` list + dispatch arm)
- [ ] Unit tests added with `[Category("Unit")]` — never `[Category("UnitTests")]`
- [ ] Targeted tests run: `dotnet test --filter "Category=Unit&Module=Command"`; **full unit suite also run** (`BindingsModule.cs` / `Program.cs` touched — smart-regression rule 4 / RR-01)
- [ ] PR description references this story file

## Dev Agent Record

{Left blank — filled by dev agent during implementation}
- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
