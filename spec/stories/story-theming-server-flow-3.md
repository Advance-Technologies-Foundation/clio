# Story 3: `update-theme` + `delete-theme` CLI commands

**Feature**: theming-server-flow (ENG-91387 — Theming with AI, Toolkit / no-code server flow, Contour B)
**FR coverage**: FR-02, FR-03, FR-06, FR-07, FR-09, FR-14, FR-19
**PRD**: [prd-theming-server-flow.md](../prd/prd-theming-server-flow.md)
**ADR**: [adr-theming-server-flow.md](../adr/adr-theming-server-flow.md)
**Status**: ready-for-dev
**Size**: M (half day)

> **Depends on Story 1** (`KnownRoute.UpdateTheme = 45` / `DeleteTheme = 46`, `ThemeRequestBuilder`,
> `ThemeServiceResponse`). Both verbs are the simpler shape than `create-theme`: no auto-id, no
> package parameter, no structured data method — each is a `RemoteCommand` whose `ProceedResponse`
> override calls `Logger.WriteError` on `success:false` (R-07). Kept together because they share the
> same `ProceedResponse` log-envelope pattern and the same Story-1 helpers.

---

## As a

AI coding agent (vibe-coder), or a developer scripting from the CLI

## I want

an `update-theme` verb that overwrites an existing theme's content by id, and a `delete-theme` verb that removes a
theme by id

## So that

I can iterate on a theme without re-creating it, and clean up experiments and abandoned drafts — both directly on
an environment with no workspace and no push

---

## Acceptance Criteria

### update-theme (FR-02, FR-06, OQ-03)

- [ ] **AC-01** — Given an existing theme id, when `clio update-theme --id <id> --caption "Y" --css-class-name
  "y-theme" --css-content-file ./theme.css -e <env>` runs, then the command POSTs to
  `ServiceModel/ThemeService.svc/UpdateTheme` (via `KnownRoute.UpdateTheme`), the server returns `success:true`,
  and clio exits 0. (FR-02, AC-04.)
- [ ] **AC-02** — Given the update request body, when serialized, then it is a `System.Text.Json` camelCase
  **record** with exactly `{ id, caption, cssClassName, cssContent }` and **no `packageUId` key** (UpdateTheme
  cannot re-home a theme; full overwrite, no read-before-write). (FR-06, R-09 AC-04 unit-assertion: body has no
  `packageUId`.) `--id`, `--caption`, `--css-class-name`, and CSS content are all **required**.
- [ ] **AC-03** — Given both CSS inputs / a missing file / an FR-10 violation, when `update-theme` runs, then
  `ThemeRequestBuilder` (Story 1) fails first: `Error: …`, `CommandSuccess = false`, exit non-zero, **no** HTTP
  call (same fail-fast as create — AC-06/07/08, R-04/R-05).

### delete-theme (FR-03, FR-07, OQ-02)

- [ ] **AC-04** — Given an existing theme id, when `clio delete-theme --id <id> -e <env>` runs, then the command
  POSTs `{ id }` to `ServiceModel/ThemeService.svc/DeleteTheme` (via `KnownRoute.DeleteTheme`), the theme is
  removed, and clio exits 0. `delete-theme` requires `--id` only and exposes no other bespoke flags. (FR-03,
  FR-07, AC-05.)
- [ ] **AC-05** — Given a non-existent id, when `delete-theme` runs, then the server returns `success:false` and
  clio prints `Error: {errorInfo.message}` and exits non-zero — **not idempotent at the clio layer** (no
  existence pre-check; a server error is a failure, not a no-op). (OQ-02, AC-05.)

### shared (both verbs)

- [ ] **AC-06** — Given the response is parsed, when handled, then each `ProceedResponse` override uses the
  shared `ThemeServiceResponseParser.TryGetFailure` (Story 1): only an explicit `success:false` is a failure;
  empty / non-JSON body is tolerated as success (FR-09). On `success:false` (e.g. caller lacks the license /
  operation — AC-09), the override calls `Logger.WriteError(...)` (**not** `WriteInfo`) so the failure is carried
  as an error and the exit code is non-zero (R-07, mirrors `ClearThemesCacheCommand`).
- [ ] **AC-07** — Given the verbs and DI wiring, when `clio --help` / the parser lists verbs, then both
  `update-theme` and `delete-theme` are reachable: `UpdateThemeCommand` / `DeleteThemeCommand` registered in
  `BindingsModule.cs`, `typeof(UpdateThemeOptions)` / `typeof(DeleteThemeOptions)` in the `Program.cs`
  `CommandOption` list, and matching dispatch arms in `ExecuteCommandWithOption`. No `[FeatureToggle]`, no
  `[RequiresPackage]`, no ClioGate (FR-14, D1, D7). Neither verb has a `--package-name` flag.
- [ ] **AC-ERR** — Given any invalid input or server `success:false`, clio prints `Error: {message}` and exits
  non-zero; on success it exits 0. No bare `catch (Exception)`; all option long-names kebab-case
  (`--id`, `--caption`, `--css-class-name`, `--css-content`, `--css-content-file` — FR-19).

## Implementation Notes

Two `RemoteCommand` shells, both using the simpler `ProceedResponse` override (no data method — that asymmetry is
create-only, ADR D5). Neither has a `--package-name`.

**Key file (create): `clio/Command/UpdateThemeCommand.cs`** (ADR D3 update-specifics, R-07, R-09)
```csharp
[Verb("update-theme", HelpText = "Overwrite an existing Creatio theme's content by id via ThemeService")]
public class UpdateThemeOptions : RemoteCommandOptions {
    [Option("id", Required = true, HelpText = "Existing theme id to overwrite (^[A-Za-z0-9_-]+$, ≤100)")] public string Id { get; set; }
    [Option("caption", Required = true, HelpText = "Human-readable theme caption (≤250)")] public string Caption { get; set; }
    [Option("css-class-name", Required = true, HelpText = "CSS class applied when active (^[A-Za-z][A-Za-z0-9_-]*$, ≤100)")] public string CssClassName { get; set; }
    [Option("css-content", Required = false, HelpText = "Inline theme CSS (mutually exclusive with --css-content-file)")] public string CssContent { get; set; }
    [Option("css-content-file", Required = false, HelpText = "Path to a UTF-8 CSS file (mutually exclusive with --css-content)")] public string CssContentFile { get; set; }
    // NO --package-name (cannot re-home a theme)
}
// UpdateThemeCommand : RemoteCommand<UpdateThemeOptions>; ServicePath => Build(KnownRoute.UpdateTheme)
// ExecuteRemoteCommand: ThemeRequestBuilder.ResolveCss + Validate before HTTP (fail fast, no HTTP on failure)
// GetRequestData: serialized camelCase record { id, caption, cssClassName, cssContent } — NO packageUId (R-09)
// ProceedResponse: ThemeServiceResponseParser.TryGetFailure → Logger.WriteError + CommandSuccess=false (R-07)
```

**Key file (create): `clio/Command/DeleteThemeCommand.cs`** (ADR D3 delete-specifics, OQ-02, R-07)
```csharp
[Verb("delete-theme", HelpText = "Delete a Creatio theme by id via ThemeService")]
public class DeleteThemeOptions : RemoteCommandOptions {
    [Option("id", Required = true, HelpText = "Theme id to delete (^[A-Za-z0-9_-]+$, ≤100)")] public string Id { get; set; }
    // no other bespoke flags
}
// DeleteThemeCommand : RemoteCommand<DeleteThemeOptions>; ServicePath => Build(KnownRoute.DeleteTheme)
// GetRequestData: serialized camelCase record { id }
// ProceedResponse: ThemeServiceResponseParser.TryGetFailure → Logger.WriteError + CommandSuccess=false (R-07);
//   NOT idempotent — no existence pre-check; server success:false → fail (OQ-02)
```
- Both bodies are serialized records, never string interpolation (R-10 escaping). `update-theme` runs the
  `ThemeRequestBuilder` CSS-resolution + FR-10 validation before the HTTP call (same fail-fast as create);
  `delete-theme` only validates the `id` field (FR-10 id rule) before sending `{ id }`.

**Key file (modify): `clio/BindingsModule.cs`** — `AddTransient<UpdateThemeCommand>();`,
`AddTransient<DeleteThemeCommand>();` (~l. 475). **DI composition root → full-suite trigger (RR-01).** The MCP
update/delete tools (Story 4) inject these commands, so the registrations are consumed.

**Key file (modify): `clio/Program.cs`** — add `typeof(UpdateThemeOptions)`, `typeof(DeleteThemeOptions)` to the
`CommandOption` list + two dispatch arms in `ExecuteCommandWithOption`
(`UpdateThemeOptions opts => Resolve<UpdateThemeCommand>(opts).Execute(opts),` and same for delete). **Dispatch
chokepoint → full-suite trigger.**

Pattern to follow: `ClearThemesCacheCommand.ProceedResponse` (ll. 56–62 — the `success:false` → `WriteError`
log-envelope idiom that R-07 mirrors); `ListThemesCommand` for the `RemoteCommand` shell.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | update: route build (`KnownRoute.UpdateTheme`, NetCore + `0/` NetFW); body `{id,caption,cssClassName,cssContent}` with **no `packageUId` key** (R-09); CSS resolution + FR-10 validation → `Error:` no HTTP on failure; `success:false` → `WriteError` + exit 1 (R-07); empty/non-JSON tolerated | `clio.tests/Command/UpdateThemeCommand.Tests.cs` |
| Unit `[Category("Unit")]` | delete: route build (`KnownRoute.DeleteTheme`, NetCore + `0/` NetFW); body `{ id }`; `success:false` (unknown id) → `WriteError` + exit 1 (not idempotent — OQ-02); empty/non-JSON tolerated; id FR-10 validation | `clio.tests/Command/DeleteThemeCommand.Tests.cs` |
| Integration `[Category("Integration")]` | update `--css-content-file` UTF-8 read end-to-end (real temp file); missing/unreadable file → `Error:` no HTTP | `clio.tests/Command/UpdateThemeCommand.Tests.cs` (`[Category("Integration")]` cases) |

- Use `BaseCommandTests<UpdateThemeOptions>` / `BaseCommandTests<DeleteThemeOptions>` as fixture bases; resolve
  from DI; register doubles in `AdditionalRegistrations`; `ClearReceivedCalls` in teardown.
- `[Category("Unit")]` (never `[Category("UnitTests")]`); naming `MethodName_ShouldBehavior_WhenCondition`
  (e.g. `GetRequestData_ShouldOmitPackageUId_WhenUpdating`,
  `ProceedResponse_ShouldExitNonZero_WhenDeleteIdUnknown`).
- Body-string assertions expect `System.Text.Json` escaping (R-10) — assert escaped form or deserialize.
- AAA + a `because` on every assertion + `[Description]` on every test.

## Definition of Done

- [ ] Code compiles without Roslyn analyzer warnings (CLIO001-CLIO005)
- [ ] All new CLI option long-names kebab-case (FR-19); no `[FeatureToggle]`, no `[RequiresPackage]`, no ClioGate (D1)
- [ ] `update-theme` body is `{id,caption,cssClassName,cssContent}` with **no `packageUId`** (full overwrite, no re-home — R-09); CSS resolution + FR-10 validation run before HTTP
- [ ] `delete-theme` body is `{ id }`, **not idempotent** (server `success:false` → fail, no pre-check — OQ-02)
- [ ] Both `ProceedResponse` overrides call `Logger.WriteError` on `success:false` (R-07); only `success:false` is a failure; empty/non-JSON tolerated (FR-09)
- [ ] Bodies are serialized camelCase records (no string interpolation — R-10)
- [ ] `UpdateThemeCommand` / `DeleteThemeCommand` registered in `BindingsModule.cs` + wired in `Program.cs`
- [ ] Unit tests added with `[Category("Unit")]` — never `[Category("UnitTests")]`
- [ ] Targeted tests run: `dotnet test --filter "Category=Unit&Module=Command"`; **full unit suite also run** (`BindingsModule.cs` / `Program.cs` touched — smart-regression rule 4 / RR-01)
- [ ] PR description references this story file

## Dev Agent Record

{Left blank — filled by dev agent during implementation}
- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
