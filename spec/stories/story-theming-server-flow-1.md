# Story 1: ThemeService write routes + shared `ThemeRequestBuilder` + `ThemeServiceResponse` parser

**Feature**: theming-server-flow (ENG-91387 — Theming with AI, Toolkit / no-code server flow, Contour B)
**FR coverage**: FR-04, FR-09, FR-10, FR-13, FR-19
**PRD**: [prd-theming-server-flow.md](../prd/prd-theming-server-flow.md)
**ADR**: [adr-theming-server-flow.md](../adr/adr-theming-server-flow.md)
**Status**: ready-for-dev
**Size**: M (half day)

> **Foundation story — blocks Stories 2, 3, 4.** It ships the three additive `KnownRoute` entries and
> the two shared helpers (`ThemeRequestBuilder` for CSS resolution + FR-10 validation; `ThemeServiceResponse`
> + `ThemeServiceResponseParser` for the silent `BaseResponse` parse) that all three write commands reuse.
> No command, no MCP tool here — only the routes and the helpers, with their unit tests.

---

## As a

developer building the `create-theme` / `update-theme` / `delete-theme` commands

## I want

the three `ThemeService` write routes wired and a single shared CSS-resolution + validation helper and
a single shared `BaseResponse` parser

## So that

each of the three commands is a thin shell over verified, unit-tested building blocks, with CSS resolution,
the FR-10 contract, and the success/failure parse implemented and proven exactly once

---

## Acceptance Criteria

- [ ] **AC-01** — Given `ServiceUrlBuilder` with new `KnownRoute.CreateTheme = 44`, `UpdateTheme = 45`,
  `DeleteTheme = 46`, when `Build(KnownRoute.CreateTheme)` is called for a .NET Core environment, then it
  returns `ServiceModel/ThemeService.svc/CreateTheme` (and `UpdateTheme` / `DeleteTheme` resolve to their
  matching `ServiceModel/ThemeService.svc/<Method>` paths). (FR-13, OQ-05: 44/45/46 continue after the
  shipped `ClearThemesCache = 43` — no gap reuse, RR-03.)
- [ ] **AC-02** — Given a .NET Framework environment (`IsNetCore = false`), when `Build` is called for any of
  the three new routes, then the result is prefixed with `0/` (the `0/` is **not** baked into the route string
  — `Build` prepends it, matching the existing `ThemeService.svc` routes).
- [ ] **AC-03** — Given `ThemeRequestBuilder` and both `--css-content` and `--css-content-file` absent (`null`),
  when CSS is resolved, then it fails with a user-friendly error (no content) and reports `Ok=false`. (FR-04,
  FR-10, R-05.)
- [ ] **AC-04** — Given `--css-content ""` (present but empty) and no `--css-content-file`, when CSS is resolved,
  then it is **valid** (`Ok=true`, `CssContent=""`) — empty CSS is allowed, null is not. (FR-10, R-05.)
- [ ] **AC-05** — Given both `--css-content` and `--css-content-file` supplied, when CSS is resolved, then it
  fails with a mutually-exclusive error and reports `Ok=false`, with no file read attempted on the file path.
  (FR-04, AC-06, R-05.)
- [ ] **AC-06** — Given `--css-content-file` points at a path that does not exist (or is unreadable), when CSS is
  resolved, then it fails with a user-friendly error and `Ok=false`; given the file exists and is UTF-8, then its
  text is read verbatim (an empty file → valid empty CSS). (FR-04, AC-07, R-05, OQ-06: UTF-8 read, fail-fast.)
- [ ] **AC-07** — Given the FR-10 field contract, when `ThemeRequestBuilder` validates, then it enforces:
  `id` `^[A-Za-z0-9_-]+$` and ≤100; `caption` required (non-empty) and ≤250; `cssClassName`
  `^[A-Za-z][A-Za-z0-9_-]*$` and ≤100; `cssContent` required (empty allowed, **null not**) and ≤1 MiB
  (1 048 576 bytes). Each violation yields a distinct `Ok=false` + user-friendly error; a >1 MiB `cssContent`
  is rejected. (FR-10, AC-08.)
- [ ] **AC-08** — Given a generated UUID v4 (`Guid.NewGuid().ToString("D")`), when routed through the same
  `id` validation as a user-supplied id, then it passes `^[A-Za-z0-9_-]+$` (hyphens allowed). (FR-05, R-06 —
  the auto-UUID is re-validated, defence in depth.)
- [ ] **AC-09** — Given `ThemeServiceResponseParser.TryGetFailure(response, out errorMessage)`, when the body is
  `{"success":false,"errorInfo":{"errorCode":"X","message":"boom"}}`, then it returns `true` with
  `errorMessage = "boom"`; when the body is `success:true` or empty, then it returns `false` (tolerated as
  success); when the body is a non-empty, non-JSON payload, then it returns `true` with `errorMessage` naming the
  unexpected non-JSON response. In all cases it writes **nothing** to any logger (silent parser — R-01, FR-09).
- [ ] **AC-ERR** — Given any `ThemeRequestBuilder` failure (missing/oversized field, bad regex, missing/dual CSS),
  when surfaced by a caller, then the message is a user-friendly `Error: …` candidate string (no stack trace, no
  bare `catch (Exception)`); the helper performs no HTTP and no logging itself.

## Implementation Notes

Two shared helpers + three additive routes. No command and no MCP tool in this story (Stories 2–4).

**Key file (modify): `clio/Common/ServiceUrlBuilder.cs`** (ADR D4, FR-13) — **Common/ → full-suite trigger (RR-03)**
```csharp
// KnownRoute enum — continue the sequence after ClearThemesCache = 43
CreateTheme = 44,
UpdateTheme = 45,
DeleteTheme = 46
// KnownRoutes map:
{ KnownRoute.CreateTheme, "ServiceModel/ThemeService.svc/CreateTheme" },
{ KnownRoute.UpdateTheme, "ServiceModel/ThemeService.svc/UpdateTheme" },
{ KnownRoute.DeleteTheme, "ServiceModel/ThemeService.svc/DeleteTheme" },
```
`Build(KnownRoute)` prepends `0/` for `.NET Framework` automatically — do **not** bake it into the route string
(matches the shipped `GetAvailableThemes = 42` / `ClearThemesCache = 43` routes).

**Key file (create): `clio/Command/ThemeRequestBuilder.cs`** (ADR D2, R-04, R-05) — `internal static` helper
(no DI, no interface — the documented borderline-OK case, matching the `PageSchemaMetadataHelper` precedent; if
CLIO001 flags the static call, confirm that precedent — do **not** apply `[ResolvedDynamically]`, it is not a DI
registration).
- `ResolveCss(string cssContent, string cssContentFile, out string css, out string error)` — `--css-content` xor
  `--css-content-file`: both `null` → error (no content); both present → error (mutually exclusive); file path →
  `System.IO.File.ReadAllText(path, Encoding.UTF8)`, fail-fast on missing/unreadable; empty file → valid `""`;
  `--css-content ""` → valid `""` (empty allowed, null not — R-05).
- `Validate(string id, string caption, string cssClassName, string cssContent, out string error)` — FR-10
  contract (id regex+≤100; caption non-empty+≤250; cssClassName regex+≤100; cssContent non-null + ≤1 MiB). The
  1 MiB cap (`1_048_576`) lives here so both CLI and MCP enforce it identically (R-04).
- Return a small DTO record (e.g. `ThemeValidationResult { bool Ok, string Error, string CssContent }`) **or**
  out-parameters — the ADR allows either; pick the shape the commands consume cleanly.

**Key file (create): `clio/Command/ThemeServiceResponse.cs`** (ADR D3, R-01, FR-09) — shared record + silent parser
```csharp
// ThemeServiceResponse { bool? Success; ThemeServiceErrorInfo ErrorInfo }  (ErrorInfo { errorCode, message })
// internal static bool ThemeServiceResponseParser.TryGetFailure(string response, out string errorMessage)
//   JsonSerializerOptions { PropertyNameCaseInsensitive = true }
//   success:false OR non-empty non-JSON → failure (out errorMessage); empty / success:true → false
//   WRITES NOTHING to any logger — logging is the caller's job (R-01)
```
Do **NOT** refactor `ListThemesCommand` / `ClearThemesCacheCommand` to use this — they keep their shipped private
copies (PRD non-goal: do not change the already-shipped commands). The new shared record lives alongside the new
commands and is consumed only by Stories 2–4.

Pattern to follow: `ListThemesCommand`'s private `TryParseThemes` silent-parse shape (the model for the shared
`TryGetFailure`); `PageSchemaMetadataHelper` (the `internal static` helper precedent for `ThemeRequestBuilder`);
`KnownRoute.GetAvailableThemes = 42` / `ClearThemesCache = 43` (the route precedent in `ServiceUrlBuilder.cs`).

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | `Build(KnownRoute.{Create,Update,Delete}Theme)` → `ServiceModel/ThemeService.svc/<Method>` (NetCore) and `0/`-prefixed (NetFW); existing theme-route builds unchanged (RR-03) | `clio.tests/Common/ServiceUrlBuilderTests.cs` (extend) |
| Unit `[Category("Unit")]` | `ThemeRequestBuilder`: css resolution matrix (both-null→err, both-present→err, inline-empty→ok, inline-value→ok); FR-10 validation (id/caption/cssClassName regex+length, cssContent null→err / empty→ok / >1 MiB→err); auto-UUID passes id regex (R-06) | `clio.tests/Command/ThemeRequestBuilderTests.cs` |
| Unit `[Category("Unit")]` | `ThemeServiceResponseParser.TryGetFailure`: success:false+message→(true,"boom"); success:true→false; empty→false; non-JSON→(true, body surfaced); **no logger writes** (silent — R-01) | `clio.tests/Command/ThemeServiceResponseParserTests.cs` |
| Integration `[Category("Integration")]` | `ThemeRequestBuilder.ResolveCss` reads a real UTF-8 temp file verbatim; missing/unreadable path → error, no throw; empty file → valid `""` | `clio.tests/Command/ThemeRequestBuilderTests.cs` (`[Category("Integration")]` cases) |

- Body/string assertions that touch serialized JSON must expect `System.Text.Json` escaping of non-ASCII and
  `<`/`>`/`&` to `\uXXXX` (R-10) — assert on a deserialized object or the escaped form, never literal characters.
- `[Category("Unit")]` (never `[Category("UnitTests")]`); naming `MethodName_ShouldBehavior_WhenCondition`
  (e.g. `Build_ShouldReturnCreateThemeRoute_WhenEnvironmentIsNetCore`,
  `Validate_ShouldFail_WhenCssContentExceedsOneMiB`).
- AAA + a `because` on every assertion + `[Description]` on every test.

## Definition of Done

- [ ] Code compiles without Roslyn analyzer warnings (CLIO001-CLIO004; CLIO005 clean — helpers are static utilities, not DI registrations)
- [ ] `KnownRoute.CreateTheme = 44` / `UpdateTheme = 45` / `DeleteTheme = 46` map to `ServiceModel/ThemeService.svc/<Method>`; no gap reuse (OQ-05)
- [ ] `ThemeRequestBuilder` is `internal static` (no DI), enforces the full FR-10 contract incl. the 1 MiB cap and the empty-vs-absent CSS matrix (R-05); generated UUID re-validated (R-06)
- [ ] `ThemeServiceResponseParser.TryGetFailure` is silent (no logger); `success:false` and non-empty non-JSON bodies are failures; only an empty body is tolerated as success (R-01, FR-09)
- [ ] Shipped `ListThemesCommand` / `ClearThemesCacheCommand` NOT refactored (PRD non-goal)
- [ ] Unit tests added with `[Category("Unit")]` — never `[Category("UnitTests")]`
- [ ] Targeted tests run: `dotnet test --filter "Category=Unit&(Module=Command|Module=Common)"`; **full unit suite also run** (`Common/ServiceUrlBuilder.cs` touched — smart-regression rule 4 / RR-03)
- [ ] PR description references this story file

## Dev Agent Record

{Left blank — filled by dev agent during implementation}
- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
