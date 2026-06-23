# Test Plan: Theming with AI — Toolkit (vibe-coder / server flow)

**Feature**: theming-server-flow (ENG-91387, epic ENG-26797 — Contour B / no-code server flow)
**Stories**: [story-1](../stories/story-theming-server-flow-1.md) · [story-2](../stories/story-theming-server-flow-2.md) · [story-3](../stories/story-theming-server-flow-3.md) · [story-4](../stories/story-theming-server-flow-4.md) · [story-5](../stories/story-theming-server-flow-5.md) · [story-6](../stories/story-theming-server-flow-6.md)
**PRD**: [prd-theming-server-flow.md](../prd/prd-theming-server-flow.md) · **ADR**: [adr-theming-server-flow.md](../adr/adr-theming-server-flow.md)
**Author**: QA Planner Agent
**Status**: Draft
**Created**: 2026-06-21

---

## Scope

### In scope
- `create-theme` / `update-theme` / `delete-theme` CLI verbs over native `ServiceModel/ThemeService.svc/{Create,Update,Delete}Theme`.
- The three additive `ServiceUrlBuilder.KnownRoute` entries (`CreateTheme = 44`, `UpdateTheme = 45`, `DeleteTheme = 46`) and their `0/`-prefix behavior on `.NET Framework`.
- The shared `ThemeRequestBuilder` (CSS `--css-content` xor `--css-content-file` resolution + FR-10 field validation, incl. the 1 MiB cap) and the shared `ThemeServiceResponseParser` (silent `BaseResponse` parse).
- camelCase serialized request bodies (no string interpolation; JSON escaping per R-10) and `--package-name`→UId resolution + fail-fast (R-03).
- The six env-aware MCP tool variants (`{create,update,delete}-theme-by-{environment,credentials}`), their arg mapping, env-aware resolution, structured-vs-log-envelope result shapes, and FR-12 safety flags.
- The `ThemingGuidanceResource` "No-code / server flow" flip and the `get-guidance theming` discovery assertions (CM-03 — token catalog not restated).

### Out of scope (with reason)
- Theme **activation** — no `ThemeService` activate endpoint; covered by guidance only (PRD non-goal).
- Local font binary upload — `cssContent` text only (PRD non-goal).
- Workspace / package scaffolding, `push-workspace`, `push-pkg` — that is Contour A / ENG-90636.
- Behavior changes to the shipped `list-themes` / `clear-themes-cache` and their MCP tools, and any refactor of their private response parsers (PRD non-goal; their existing tests are pure regression guard — see "Regression Guard").
- Client-side `cssContent` sanitization (RR-05 — sent verbatim, matching the server).
- ClioGate method / cliogate bump / `[RequiresPackage]` / `[FeatureToggle]` — none added (D1, FR-17).

---

## Regression Scope (smart-regression policy)

The change touches **three full-suite triggers** (smart-regression rule 4): `clio/BindingsModule.cs` (DI composition root, RR-01), `clio/Program.cs` (dispatch chokepoint, RR-01), and `clio/Common/ServiceUrlBuilder.cs` (shared `Common/`, RR-03). The full `Category=Unit` suite is therefore **mandatory** in addition to the targeted module filter.

```bash
# Targeted (run first — fast feedback on the changed modules)
dotnet test clio.tests/clio.tests.csproj \
  --filter "Category=Unit&(Module=Command|Module=Common|Module=McpServer)"

# Full unit suite (MANDATORY — BindingsModule.cs + Program.cs + Common/ServiceUrlBuilder.cs touched, rule 4)
dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit"

# Integration (PR-merge tier — the --css-content-file real temp-file reads)
dotnet test clio.tests/clio.tests.csproj --filter "Category=Integration&Module=Command"
```

MCP E2E (`clio.mcp.e2e/`) is **mandatory per repo policy (AGENTS.md MCP maintenance policy)** but is **NOT in CI** — run it manually (see TC-E2E-* and the PR checklist gate below).

---

## Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|-----------|
| Shared `ServiceUrlBuilder` (Common/) — new routes break existing theme/route builds, or `0/` prefix not applied on NetFW (RR-03) | Med | High | TC-U-01..03 assert all three new builds + `0/`; full unit suite re-runs `ServiceUrlBuilder.cs` existing route tests (regression guard). |
| Shared `ThemingGuidanceResource` / `GuidanceCatalog` with ENG-90636 — guidance edit breaks `get-guidance theming` resolution (RR-02, CM-03) | Med | High | TC-U-20..22 extend the existing `GuidanceGetToolTests`; resolution stays green; assert token catalog NOT restated. |
| MCP `IEnumerable<Type>` registration regressed to `Type[]` / `*FromAssembly` → tools silently register nothing (RR-04) | Low | High | TC-U-19 asserts the env-aware resolution seam; TC-E2E-01..03 (manual) confirm the real `mcp-server` actually advertises all six variants — the only check that catches a silent-no-op registration. |
| Unsanitized `cssContent` (RR-05) — escaping landmine if body is string-interpolated | Med | High | TC-U-09/13/16 assert the body is a serialized record with R-10 JSON escaping (quotes/newlines/`<`/`>`/`&`/non-ASCII → `\uXXXX`); 1 MiB cap enforced in the command/data method (R-04) so both surfaces reject identically. |
| `--package-name`→UId resolution failure leaks a `null` UId or fires an HTTP call (R-03) | Med | Med | TC-U-11 asserts fail-fast `Error:` + `CommandSuccess=false` + **no** `ExecutePostRequest`. |
| `create-theme` MCP path leaks console output onto the JSON-RPC channel (CM-02) | Med | Med | TC-U-08/17 assert `TryCreateTheme` writes nothing to the logger; the silent shared parser (TC-U-06) underpins this. |
| Stacked-branch dependency — built on `feature/ENG-90636-…`; merge order matters (`ClearThemesCache = 43` baseline + sibling guidance must land first) | Med | Med | No reuse of enum gap (44/45/46 continue the sequence); regression guard re-runs the ENG-90636 theme tests; document merge order in the PR. |
| `update-theme` body accidentally carries a `packageUId` key (R-09, FR-06 — cannot re-home) | Low | Med | TC-U-14 asserts the update body has exactly `{id,caption,cssClassName,cssContent}` and **no** `packageUId` key. |
| `delete-theme` treated as idempotent (server "not found" swallowed) contrary to OQ-02 | Low | Med | TC-U-18 asserts `success:false` → `WriteError` + exit 1 (not a no-op). |

---

## Traceability Matrix (case → requirement)

| Story | FR / AC / R | Cases |
|-------|-------------|-------|
| 1 | FR-13, FR-04, FR-10, FR-09, R-05, R-06, R-01, R-10 | TC-U-01..07, TC-I-01..03 |
| 2 | FR-01, FR-05, FR-08, FR-09, R-01..R-03, R-06, R-10, AC-02/03/06/07/08 | TC-U-08..12, TC-I-04..05 |
| 3 | FR-02, FR-03, FR-06, FR-07, FR-09, R-07, R-09, OQ-02 | TC-U-13..18, TC-I-06 |
| 4 | FR-11, FR-12, FR-14, R-04, R-09, RR-04, AC-10/AC-ERR | TC-U-19..19f, TC-E2E-01..04 |
| 5 | FR-15, CM-03, RR-02, AC-11 | TC-U-20..22, TC-E2E-05 |
| 6 | FR-16 | TC-U-23 (if docs-presence gate exists), doc review (PR) |
| — | AC-04 ("in current package"), AC-10 (live manifest), R-08 (omitted-packageUId fallback), AC-01 round-trip | TC-M-01..04 (manual live stand) |

---

## Unit Tests — `clio.tests/`

> Conventions for every case: `[Category("Unit")]` (NEVER `UnitTests`); `MethodName_ShouldExpectedBehavior_WhenCondition`; explicit AAA; a `because` on every assertion; `[Description]` on every test. Command fixtures derive from `BaseCommandTests<TOptions>`, resolve the SUT from `Container`, register `IApplicationClient` / `IServiceUrlBuilder` doubles in `AdditionalRegistrations`, and `ClearReceivedCalls` in teardown (mirror `ListThemesCommandTestCase`). MCP fixtures use the `ListThemesToolTests` pattern: a `Fake*Command` subclass overriding the data method + a substitute `IToolCommandResolver`.

### Story 1 — routes + shared helpers (`ServiceUrlBuilder.cs`, `ThemeRequestBuilder`, `ThemeServiceResponseParser`)

#### TC-U-01 — Route build for all three new routes (NetCore)
- **Level**: Unit · **File**: `clio.tests/Common/ServiceUrlBuilder.cs` (extend) · **Module**: Common
- **Traces**: FR-13, S1-AC-01, OQ-05, RR-03
- **Preconditions**: `EnvironmentSettings.IsNetCore = true`, `Uri = "http://localhost"`.
- **Steps**: `Build(KnownRoute.CreateTheme)`, `Build(KnownRoute.UpdateTheme)`, `Build(KnownRoute.DeleteTheme)`.
- **Expected**: returns `http://localhost/ServiceModel/ThemeService.svc/CreateTheme` (and `/UpdateTheme`, `/DeleteTheme`) — no `0/` prefix on .NET Core.

#### TC-U-02 — Route build prefixes `0/` on .NET Framework
- **Level**: Unit · **File**: `ServiceUrlBuilder.cs` (extend) · **Module**: Common
- **Traces**: FR-13, S1-AC-02, RR-03
- **Preconditions**: `EnvironmentSettings.IsNetCore = false`, `Uri = "http://localhost"`.
- **Steps**: `Build` each of the three new routes.
- **Expected**: each result is `http://localhost/0/ServiceModel/ThemeService.svc/<Method>` — the `0/` is prepended by `Build`, not baked into the route string. Name e.g. `Build_ShouldPrefixZeroSlash_WhenEnvironmentIsNetFramework`.

#### TC-U-03 — Enum values continue the sequence, no gap reuse
- **Level**: Unit · **File**: `ServiceUrlBuilder.cs` (extend) · **Module**: Common
- **Traces**: FR-13, OQ-05
- **Preconditions**: none.
- **Steps**: assert `(int)KnownRoute.CreateTheme == 44`, `UpdateTheme == 45`, `DeleteTheme == 46`; assert the existing `GetAvailableThemes`/`ClearThemesCache` routes still resolve unchanged.
- **Expected**: 44/45/46; existing theme routes unregressed (RR-03 guard).

#### TC-U-04 — CSS resolution matrix (absent / empty / mutual-exclusion)
- **Level**: Unit · **File**: `clio.tests/Command/ThemeRequestBuilderTests.cs` · **Module**: Command
- **Traces**: FR-04, FR-10, S1-AC-03/04/05, R-05, AC-06
- **Preconditions**: none (pure helper, string inputs only).
- **Steps / Expected** (one `[TestCase]`/test per row):
  - both `null` → `Ok=false`, "no content" error.
  - `--css-content ""` (present, empty), file absent → `Ok=true`, `CssContent == ""` (empty allowed, null not).
  - `--css-content "body{}"`, file absent → `Ok=true`, `CssContent == "body{}"`.
  - both `--css-content` and `--css-content-file` present → `Ok=false`, mutually-exclusive error, **no file read attempted**.
- Name e.g. `ResolveCss_ShouldFail_WhenBothInputsAbsent`, `ResolveCss_ShouldReturnEmpty_WhenInlineContentIsEmptyString`.

#### TC-U-05 — FR-10 field validation contract
- **Level**: Unit · **File**: `ThemeRequestBuilderTests.cs` · **Module**: Command
- **Traces**: FR-10, S1-AC-07, AC-08
- **Preconditions**: none.
- **Steps / Expected** (distinct `Ok=false` + user-friendly error per violation):
  - `id` not matching `^[A-Za-z0-9_-]+$` → fail; `id` length 101 → fail; valid id → pass.
  - `caption` null/empty → fail; `caption` length 251 → fail.
  - `cssClassName` not matching `^[A-Za-z][A-Za-z0-9_-]*$` (e.g. leading digit `1theme`) → fail; length 101 → fail.
  - `cssContent` null → fail; `cssContent` `""` → pass; `cssContent` exactly 1 048 576 bytes → pass; 1 048 577 bytes → fail (1 MiB cap). Name e.g. `Validate_ShouldFail_WhenCssContentExceedsOneMiB`.

#### TC-U-06 — `ThemeServiceResponseParser.TryGetFailure` (silent parse)
- **Level**: Unit · **File**: `clio.tests/Command/ThemeServiceResponseParserTests.cs` · **Module**: Command
- **Traces**: FR-09, S1-AC-09, R-01
- **Preconditions**: none.
- **Steps / Expected**:
  - `{"success":false,"errorInfo":{"errorCode":"X","message":"boom"}}` → returns `true`, `errorMessage == "boom"`.
  - `{"success":true}` → returns `false`.
  - empty string / whitespace → returns `false`.
  - non-JSON (`"OK"`) → returns `false` (tolerated).
  - **No logger writes on any path** — the parser is silent (assert via a substitute logger never being passed/called; the helper takes no logger). Name e.g. `TryGetFailure_ShouldReturnTrueWithMessage_WhenSuccessIsFalse`.

#### TC-U-07 — Auto-UUID re-validation (defence in depth)
- **Level**: Unit · **File**: `ThemeRequestBuilderTests.cs` · **Module**: Command
- **Traces**: FR-05, S1-AC-08, R-06
- **Preconditions**: none.
- **Steps**: validate `Guid.NewGuid().ToString("D")` through the same `id` rule (loop a handful of generated values).
- **Expected**: passes `^[A-Za-z0-9_-]+$` and ≤100 every time (hyphens allowed). Name e.g. `Validate_ShouldAcceptGeneratedUuid_WhenIdAutoGenerated`.

### Story 2 — `create-theme` command

#### TC-U-08 — `TryCreateTheme` returns generated id and is silent
- **Level**: Unit · **File**: `clio.tests/Command/CreateThemeCommand.Tests.cs` · **Module**: Command
- **Traces**: FR-05, S2-AC-02/08, R-01, R-02, R-06, CM-02
- **Preconditions**: `IApplicationClient` substitute returns `{"success":true}`; `--id` omitted; valid caption/css-class-name/inline css.
- **Steps**: call `TryCreateTheme(options, out id, out err)` with `Logger` set to a substitute.
- **Expected**: returns `true`; `id` is a non-empty UUID matching `^[A-Za-z0-9_-]+$`; that same `id` is the body `id` sent; the substitute logger received **no** `WriteInfo`/`WriteError` (silent — CM-02).

#### TC-U-09 — camelCase serialized body with R-10 escaping
- **Level**: Unit · **File**: `CreateThemeCommand.Tests.cs` · **Module**: Command
- **Traces**: FR-01, S2-AC-05, R-10, ADR D3
- **Preconditions**: `IApplicationClient` substitute capturing the posted body; `cssContent` containing `"`, `\n`, `{`, `<`, and a non-ASCII char.
- **Steps**: `Execute` with valid inputs; capture the body argument to `ExecutePostRequest`.
- **Expected**: body deserializes to `{ id, caption, cssClassName, cssContent, packageUId }` (camelCase keys); when asserting on the raw string, expect `System.Text.Json` escaping (`<` for `<`, `\uXXXX` for non-ASCII, escaped quotes/newlines) — never assert literal `<`/non-ASCII characters (R-10). Prefer deserialize-and-compare.

#### TC-U-10 — `--package-name` resolved → UId sent; omitted → no packageUId key
- **Level**: Unit · **File**: `CreateThemeCommand.Tests.cs` · **Module**: Command
- **Traces**: FR-08, S2-AC-03, AC-03
- **Preconditions**: capturing `IApplicationClient`; `PageSchemaMetadataHelper.QueryPackageUId` exercised through a faked client response that resolves a known package to a UId.
- **Steps**: (a) run with `--package-name <existing>` → assert body `packageUId` == resolved UId; (b) run with `--package-name` omitted → assert body has **no** `packageUId` key (assert key absence, not a zero-GUID value).
- **Expected**: both as above.

#### TC-U-11 — `--package-name` resolution failure → fail fast, no HTTP
- **Level**: Unit · **File**: `CreateThemeCommand.Tests.cs` · **Module**: Command
- **Traces**: S2-AC-04, R-03, AC-ERR
- **Preconditions**: `QueryPackageUId` returns a non-null error (e.g. "Package 'X' not found…").
- **Steps**: `Execute` with `--package-name <missing>`.
- **Expected**: `Logger.WriteError` contains the error; `CommandSuccess == false`; exit code 1; `ExecutePostRequest` to `CreateTheme` **never called** (no `null` UId sent). Name e.g. `Execute_ShouldFailWithoutHttp_WhenPackageNameUnresolved`.

#### TC-U-12 — create response parsing (success / success=false / empty / non-JSON)
- **Level**: Unit · **File**: `CreateThemeCommand.Tests.cs` · **Module**: Command
- **Traces**: FR-09, S2-AC-07, AC-09, R-01, R-02
- **Steps / Expected**:
  - `{"success":true}` → exit 0; CLI path `WriteInfo("Created theme '<id>'")` printed (R-02).
  - `{"success":false,"errorInfo":{"message":"no permission"}}` (license/op missing, AC-09) → exit 1; `WriteError` contains `no permission`.
  - empty body → exit 0 (tolerated).
  - non-JSON body → exit 0 (tolerated).
- Also assert CSS mutual-exclusion / missing-file / FR-10 violation through the command (delegated to TC-I-04/05 for real file I/O) → `Error:` + **no HTTP** (AC-06/07/08).

### Story 3 — `update-theme` + `delete-theme` commands

#### TC-U-13 — update route build (NetCore + `0/` NetFW)
- **Level**: Unit · **File**: `clio.tests/Command/UpdateThemeCommand.Tests.cs` · **Module**: Command
- **Traces**: FR-02, S3-AC-01
- **Steps / Expected**: `Execute` posts to `…/ServiceModel/ThemeService.svc/UpdateTheme` (NetCore) and `…/0/ServiceModel/ThemeService.svc/UpdateTheme` (NetFW), asserted on the captured `ExecutePostRequest` url.

#### TC-U-14 — update body has NO `packageUId` key
- **Level**: Unit · **File**: `UpdateThemeCommand.Tests.cs` · **Module**: Command
- **Traces**: FR-06, S3-AC-02, R-09 (AC-04 unit-assertion), R-10
- **Preconditions**: capturing client; valid id/caption/css-class-name/css.
- **Steps**: `Execute`; capture body.
- **Expected**: body deserializes to exactly `{ id, caption, cssClassName, cssContent }`; the serialized JSON contains **no** `packageUId` property (assert key absence, not just value). Name e.g. `GetRequestData_ShouldOmitPackageUId_WhenUpdating`.

#### TC-U-15 — update CSS resolution + FR-10 validation before HTTP
- **Level**: Unit · **File**: `UpdateThemeCommand.Tests.cs` · **Module**: Command
- **Traces**: FR-04, FR-10, S3-AC-03, R-04, R-05
- **Steps / Expected**: both CSS inputs present, or FR-10 violation → `Error:` + `CommandSuccess=false` + exit 1 + **no** `ExecutePostRequest` (missing-file path → TC-I-06).

#### TC-U-16 — update response parsing + `WriteError` on failure
- **Level**: Unit · **File**: `UpdateThemeCommand.Tests.cs` · **Module**: Command
- **Traces**: FR-09, S3-AC-06, R-07, AC-09
- **Steps / Expected**: `success:false` → `Logger.WriteError(...)` (NOT `WriteInfo`) + exit 1; `success:true` → exit 0; empty / non-JSON → exit 0 (tolerated).

#### TC-U-17 — delete route build + `{ id }` body
- **Level**: Unit · **File**: `clio.tests/Command/DeleteThemeCommand.Tests.cs` · **Module**: Command
- **Traces**: FR-03, FR-07, S3-AC-04, R-10
- **Preconditions**: capturing client; valid `--id`.
- **Steps**: `Execute`; capture url + body.
- **Expected**: posts to `…/ServiceModel/ThemeService.svc/DeleteTheme` (and `0/`-prefixed NetFW); body deserializes to exactly `{ id }` (no other keys); `--id` validated against the FR-10 id rule before sending.

#### TC-U-18 — delete is NOT idempotent (unknown id → failure)
- **Level**: Unit · **File**: `DeleteThemeCommand.Tests.cs` · **Module**: Command
- **Traces**: FR-03, S3-AC-05, OQ-02, R-07
- **Preconditions**: client returns `{"success":false,"errorInfo":{"message":"theme not found"}}`.
- **Steps**: `Execute` with an id the server rejects.
- **Expected**: `Logger.WriteError` contains the message; exit 1 (no existence pre-check; server error is a failure, not a no-op). `success:true`/empty/non-JSON → exit 0. Name e.g. `ProceedResponse_ShouldExitNonZero_WhenDeleteIdUnknown`.

### Story 4 — MCP tools

#### TC-U-19 — env-aware resolution through the generic `EnvironmentOptions` arm (no switch edit)
- **Level**: Unit · **File**: `clio.tests/Command/McpServer/CreateThemeToolTests.cs` (+ Update/Delete) · **Module**: McpServer
- **Traces**: FR-11, S4-AC-06, ADR D5, RR-04
- **Preconditions**: substitute `IToolCommandResolver`; a `Fake{Create,Update,Delete}ThemeCommand` like `FakeListThemesCommand`.
- **Steps**: invoke each `*-by-environment` tool method with a valid environment name.
- **Expected**: the resolver's `Resolve<TCommand>(opts)` is called once with the per-call environment (the *resolved* fake captured options, not the injected default) — proving the options fall through the generic `EnvironmentOptions` arm and a fresh command is resolved per call. Name e.g. `CreateThemeByEnvironment_ShouldResolveFreshCommand_WhenEnvironmentProvided`.

#### TC-U-19a — create arg mapping → `CreateThemeOptions`
- **Level**: Unit · **File**: `CreateThemeToolTests.cs` · **Module**: McpServer
- **Traces**: FR-11, S4-AC-01
- **Steps / Expected**: `CreateThemeByName(environmentName, caption, cssClassName, cssContent, id, packageName)` maps each argument onto the resolved `CreateThemeOptions` (assert via the captured options on the fake resolver). `id`/`packageName` default to `null` when omitted.

#### TC-U-19b — create returns structured result carrying the id
- **Level**: Unit · **File**: `CreateThemeToolTests.cs` · **Module**: McpServer
- **Traces**: FR-11, S4-AC-02, R-01, CM-02
- **Preconditions**: fake `TryCreateTheme` returns `(true, "generated-id")`.
- **Steps**: invoke the create tool.
- **Expected**: result is `CreateThemeResult { Success=true, Id="generated-id", Error=null }`; no command console output leaked (fake is silent). On `(false, "err")` → `Success=false`, `Error` carries the message.

#### TC-U-19c — update/delete go through `InternalExecute` log envelope
- **Level**: Unit · **File**: `UpdateThemeToolTests.cs`, `DeleteThemeToolTests.cs` · **Module**: McpServer
- **Traces**: FR-11, S4-AC-03, R-07, R-09 (AC-ERR split)
- **Steps / Expected**: each tool resolves and runs `InternalExecute<{Update,Delete}ThemeCommand>` and returns a `CommandExecutionResult`; a command `success:false` surfaces as a non-empty `ErrorMessage` in the execution-log envelope with a non-zero exit code.

#### TC-U-19d — FR-12 safety flags + `OpenWorld=false`
- **Level**: Unit · **File**: all three `*ThemeToolTests.cs` · **Module**: McpServer
- **Traces**: FR-12, S4-AC-05, AC-10 (unit half — attribute values, not the live manifest)
- **Steps**: read the `[McpServerTool]` attribute on each tool method via reflection.
- **Expected**: create = `ReadOnly=false, Destructive=false, Idempotent=false`; update = `false, false, true`; delete = `false, true, false`; `OpenWorld=false` on all six methods.

#### TC-U-19e — descriptions route to `get-guidance theming`
- **Level**: Unit · **File**: all three `*ThemeToolTests.cs` · **Module**: McpServer
- **Traces**: FR-12, S4-AC-05
- **Steps / Expected**: each tool method's `[Description]` text contains `get-guidance theming`.

#### TC-U-19f — by-credentials validation guards
- **Level**: Unit · **File**: all three `*ThemeToolTests.cs` · **Module**: McpServer
- **Traces**: FR-11, S4-AC-08, AC-ERR
- **Steps / Expected**: `*-by-credentials` with empty/missing url (or other required arg) returns a graceful failure (create → `CreateThemeResult.Failure(...)`; update/delete → `CommandExecutionResult.ValidateCredentials` failure) and does **not** resolve a command — no unhandled exception.

#### TC-U-19g — MCP path enforces the identical 1 MiB cap
- **Level**: Unit · **File**: `CreateThemeToolTests.cs`, `UpdateThemeToolTests.cs` · **Module**: McpServer
- **Traces**: FR-11, S4-AC-04, R-04
- **Preconditions**: a real (or lightly-faked-to-call-through) command whose `TryCreateTheme`/execute runs `ThemeRequestBuilder` validation.
- **Steps**: invoke create/update tool with `cssContent` of 1 048 577 bytes.
- **Expected**: graceful validation failure (same message as the CLI path) — validation lives in the command/data method, not the options layer, so MCP cannot push >1 MiB while the CLI rejects it.

### Story 5 — guidance flip

#### TC-U-20 — `get-guidance theming` still resolves
- **Level**: Unit · **File**: `clio.tests/Command/McpServer/GuidanceGetToolTests.cs` (extend) · **Module**: McpServer
- **Traces**: FR-15, S5-AC-01, RR-02
- **Steps / Expected**: `get-guidance theming` returns `Success=true` and article URI `docs://mcp/guides/theming` (resolution unbroken — RR-02 guard).

#### TC-U-21 — server-flow section present and routed
- **Level**: Unit · **File**: `GuidanceGetToolTests.cs` (extend) · **Module**: McpServer
- **Traces**: FR-15, S5-AC-02/03, AC-11
- **Steps / Expected**: the resolved guide text contains a "No-code / server flow" section naming `create-theme-by-environment`, `update-theme-by-environment`, `delete-theme-by-environment`, and `list-themes-by-environment`; the "Which flow" line no longer says "not yet available" and routes to that section. Name e.g. `GetGuidance_ShouldDescribeServerFlow_WhenTopicIsTheming`.

#### TC-U-22 — token catalog NOT restated; shared sections survive
- **Level**: Unit · **File**: `GuidanceGetToolTests.cs` (extend) · **Module**: McpServer
- **Traces**: FR-15, S5-AC-05, CM-03, RR-02
- **Steps / Expected**: the guide does **not** embed `--crt-*` token names/values (guards CM-03 / single source of truth); the shared "Source of truth — @creatio-devkit/theming", "List themes", and "Get / set the default theme" sections are still present. Name e.g. `GetGuidance_ShouldNotRestateTokenCatalog_WhenTopicIsTheming`.

### Story 6 — docs (only if an automated gate exists)

#### TC-U-23 — docs-presence gate passes for the three verbs (conditional)
- **Level**: Unit · **File**: existing `ReadmeChecker`-style docs-presence test (if present) · **Module**: Command
- **Traces**: FR-16, S6-AC-06
- **Steps / Expected**: if a docs-presence gate exists, it passes for `create-theme` / `update-theme` / `delete-theme` (help txt + `docs/commands/*.md` present and referenced; no `new-theme` references). If no such gate exists, this reduces to PR doc review (see "Manual / Doc Review" below).

---

## Integration Tests — `clio.tests/` (`[Category("Integration")]`)

> Real temp-file I/O (`--css-content-file`). PR-merge tier. Create the temp file under the OS temp dir, write UTF-8, delete in teardown; OS-portable per the test-style policy.

#### TC-I-01 — `ResolveCss` reads a real UTF-8 file verbatim
- **Level**: Integration · **File**: `clio.tests/Command/ThemeRequestBuilderTests.cs` (Integration cases) · **Module**: Command
- **Traces**: FR-04, S1-AC-06, OQ-06
- **Preconditions**: a temp `.css` file with known UTF-8 content (incl. a non-ASCII char and a newline).
- **Steps**: `ResolveCss(null, <path>, out css, out err)`.
- **Expected**: `Ok=true`, `css` equals the file content byte-for-byte (as decoded UTF-8); teardown deletes the file.

#### TC-I-02 — missing / unreadable file → error, no throw
- **Level**: Integration · **File**: `ThemeRequestBuilderTests.cs` · **Module**: Command
- **Traces**: FR-04, S1-AC-06, AC-07
- **Steps**: `ResolveCss(null, <nonexistent path>, …)`.
- **Expected**: `Ok=false`, user-friendly error, no exception escapes (no bare `catch (Exception)` in the helper).

#### TC-I-03 — empty file → valid empty CSS
- **Level**: Integration · **File**: `ThemeRequestBuilderTests.cs` · **Module**: Command
- **Traces**: FR-04, S1-AC-06, R-05
- **Steps**: `ResolveCss(null, <empty temp file>, …)`.
- **Expected**: `Ok=true`, `css == ""`.

#### TC-I-04 — `create-theme --css-content-file` end-to-end (real file)
- **Level**: Integration · **File**: `clio.tests/Command/CreateThemeCommand.Tests.cs` (Integration cases) · **Module**: Command
- **Traces**: FR-04, S2-AC-06, AC-04 (file input)
- **Preconditions**: temp UTF-8 `.css` file; capturing `IApplicationClient`.
- **Steps**: `Execute` with `--css-content-file <path>` (no `--css-content`).
- **Expected**: the posted body's `cssContent` equals the file content (escaped per R-10); exit 0 on `success:true`.

#### TC-I-05 — `create-theme` missing file → `Error:`, no HTTP
- **Level**: Integration · **File**: `CreateThemeCommand.Tests.cs` (Integration) · **Module**: Command
- **Traces**: FR-04, S2-AC-06, AC-07
- **Steps**: `Execute` with `--css-content-file <nonexistent>`.
- **Expected**: `Error:` written, `CommandSuccess=false`, exit 1, `ExecutePostRequest` **never** called.

#### TC-I-06 — `update-theme --css-content-file` end-to-end + missing file
- **Level**: Integration · **File**: `clio.tests/Command/UpdateThemeCommand.Tests.cs` (Integration) · **Module**: Command
- **Traces**: FR-04, S3-AC-01/03
- **Steps / Expected**: same two checks as TC-I-04/05 but on `update-theme` (real-file read → body `cssContent`, exit 0; missing file → `Error:` + no HTTP).

---

## E2E (MCP) Tests — `clio.mcp.e2e/` (`[Category("E2E")]`)

> **CI status: NOT in CI — manual execution required.** MCP E2E is mandatory per the AGENTS.md MCP maintenance policy (unit mapping tests are insufficient alone), but the suite does not run in CI yet. Each of these is the **only** check that catches an RR-04 silent-no-op registration, because unit tests assert attribute values on the tool classes, not the live tool manifest (R-09).

#### TC-E2E-01 — create tool variants advertised + round-trip
- **Tool(s)**: `create-theme-by-environment`, `create-theme-by-credentials`
- **File**: `clio.mcp.e2e/CreateThemeToolE2ETests.cs`
- **Traces**: FR-11, FR-12, AC-10, R-09
- **Steps**: start the real `clio mcp-server`; list tools; assert both create variants are advertised with `ReadOnly=false, Destructive=false, Idempotent=false, OpenWorld=false` and a description referencing `get-guidance theming`; invoke create against a stand and capture the returned `{ success, id }`.
- **Expected**: both variants present with the FR-12 flags; create returns a structured result carrying the generated id.
- **Manual gate**: add to the PR checklist.

#### TC-E2E-02 — update tool variants advertised (Idempotent=true)
- **Tool(s)**: `update-theme-by-environment`, `update-theme-by-credentials`
- **File**: `clio.mcp.e2e/UpdateThemeToolE2ETests.cs`
- **Traces**: FR-11, FR-12, AC-10
- **Expected**: both advertised with `ReadOnly=false, Destructive=false, Idempotent=true, OpenWorld=false`; description → `get-guidance theming`. **Manual.**

#### TC-E2E-03 — delete tool variants advertised (Destructive=true)
- **Tool(s)**: `delete-theme-by-environment`, `delete-theme-by-credentials`
- **File**: `clio.mcp.e2e/DeleteThemeToolE2ETests.cs`
- **Traces**: FR-11, FR-12, AC-10
- **Expected**: both advertised with `ReadOnly=false, Destructive=true, Idempotent=false, OpenWorld=false`; description → `get-guidance theming`. **Manual.**

#### TC-E2E-04 — full create → update → delete round-trip over MCP
- **Tool(s)**: all six variants
- **File**: `clio.mcp.e2e/` (round-trip)
- **Traces**: FR-11, SM-02, AC-ERR (MCP surface split)
- **Steps**: create (capture id) → update by that id → delete by that id, all via the MCP tools against a real stand with the required license/operation.
- **Expected**: each step returns success on its surface (create → structured `{success:true,id}`; update/delete → clean log envelope, exit 0); a forced failure (e.g. delete a now-unknown id) surfaces as `ErrorMessage` + non-zero exit, not an exception. **Manual.**

#### TC-E2E-05 — `get-guidance theming` discovery against the real server
- **Tool**: `get-guidance` (`theming`)
- **File**: `clio.mcp.e2e/` (extend the existing theming discovery test)
- **Traces**: FR-15, AC-11
- **Expected**: the real server returns the updated guide text including the "No-code / server flow" section. **Manual.**

---

## Manual / Live-Stand Tests (R-09 testability split — not unit-verifiable)

> These assertions depend on a real Creatio environment with `CanCustomizeBranding` + `CanManageThemes`. They are listed here so the dev/QA does not mistake them for unit-coverable items. Record outcomes in the manual runbook + PR.

#### TC-M-01 — AC-04 "overwritten in its current package" (live)
- **Level**: Manual live stand · **Traces**: AC-04, R-09
- **Caveat**: the clio **unit** assertion is only "update body has `{id,caption,cssClassName,cssContent}` and no `packageUId`" (TC-U-14). That the server overwrites the theme **in its current package** is a live-stand observation only.
- **Steps**: create a theme in package P; `update-theme` it; confirm via `list-themes` it is still owned by P with new content.

#### TC-M-02 — AC-10 advertised by the real `clio mcp-server` (live manifest)
- **Level**: Manual live stand / E2E · **Traces**: AC-10, R-09, RR-04
- **Caveat**: unit tests assert the safety-flag *attribute values* on the tool classes (TC-U-19d), **not** the live tool manifest. The live-manifest assertion is satisfied **only** by TC-E2E-01..03 (manual, not in CI). This is the catch for an RR-04 silent-no-op registration.

#### TC-M-03 — R-08 omitted-packageUId server fallback (live)
- **Level**: Manual live stand · **Traces**: AC-03, A-05, R-08
- **Caveat**: that omitting the `packageUId` key makes the server use the `CurrentPackageId` system setting is **NOT unit-verifiable** (per the documented `IThemeService` contract only).
- **Steps**: `create-theme` with `--package-name` omitted against a stand; confirm the theme lands in the `CurrentPackageId` package. If the server distinguishes a missing key from an empty GUID and rejects the omission, the default create path is broken → file a follow-up.

#### TC-M-04 — full create → list → update → delete round-trip (CLI, live, SM-01)
- **Level**: Manual live stand · **Traces**: SM-01, AC-01, AC-05
- **Steps**: `create-theme` (capture echoed id) → `list-themes` (theme appears) → `update-theme` → `delete-theme` → `list-themes` (theme gone). Each exits 0; deleting a now-unknown id exits non-zero with `Error:`.

---

## Regression Guard

Tests that MUST stay green after this feature ships (re-run by the full `Category=Unit` suite — rule 4):

| Test file | Test(s) | Why at risk |
|-----------|---------|-------------|
| `clio.tests/Common/ServiceUrlBuilder.cs` | existing route-build cases (`somepath1`, `0/` prefix) | shared `Common/ServiceUrlBuilder.cs` gets three new routes (RR-03) — adding enum/map entries must not shift existing builds. |
| `clio.tests/Command/ListThemesCommand.Tests.cs` | all (NetFW/NetCore route, parse, empty, failure, non-JSON) | sibling theme command; its private parser must NOT be refactored (PRD non-goal) and its route must stay `GetAvailableThemes`. |
| `clio.tests/Command/McpServer/ClearThemesCacheToolTests.cs` | all | sibling theme MCP tool sharing the `BaseTool` env-aware path and the MCP registration seam (RR-04). |
| `clio.tests/Command/McpServer/ListThemesToolTests.cs` | all | shares the `IToolCommandResolver` + `BaseTool` resolution path the new tools use. |
| `clio.tests/Command/McpServer/GuidanceGetToolTests.cs` | existing `theming` discovery test | shared `ThemingGuidanceResource` / `GuidanceCatalog` edited (RR-02) — resolution must stay green. |
| Full `Category=Unit` suite | all | `BindingsModule.cs` + `Program.cs` + `Common/` touched → DI composition + dispatch could regress any module (rule 4). |

---

## Coverage Estimate

| Layer | New tests | Modified/extended files | Notes |
|-------|-----------|-------------------------|-------|
| Unit | ~23 cases (TC-U-01..23, several multi-`TestCase`) | `ServiceUrlBuilder.cs` (extend), `GuidanceGetToolTests.cs` (extend); new `ThemeRequestBuilderTests`, `ThemeServiceResponseParserTests`, `{Create,Update,Delete}ThemeCommand.Tests`, `{Create,Update,Delete}ThemeToolTests` | every case `[Category("Unit")]`. |
| Integration | 6 cases (TC-I-01..06) | new Integration cases in `ThemeRequestBuilderTests`, `CreateThemeCommand.Tests`, `UpdateThemeCommand.Tests` | real temp-file I/O; PR-merge tier. |
| E2E (MCP) | 5 cases (TC-E2E-01..05) | new `{Create,Update,Delete}ThemeToolE2ETests`; extend theming discovery e2e | **NOT in CI — manual only.** |
| Manual (live stand) | 4 items (TC-M-01..04) | runbook + PR notes | R-09 testability split; not unit-coverable. |

---

## Definition of Done for QA

- [ ] All TC-U-* implemented with `[Category("Unit")]` — NOT `[Category("UnitTests")]`.
- [ ] All TC-I-* implemented with `[Category("Integration")]` (real temp files; OS-portable).
- [ ] Command fixtures use `BaseCommandTests<TOptions>`, resolve the SUT from `Container`, register doubles in `AdditionalRegistrations`, `ClearReceivedCalls` in teardown; MCP fixtures mirror `ListThemesToolTests` (fake command + substitute `IToolCommandResolver`).
- [ ] Body-string assertions expect `System.Text.Json` escaping (R-10) — assert the escaped form or deserialize; never literal `<`/non-ASCII characters.
- [ ] Regression guard tests (table above) green; **full `Category=Unit` suite** run (BindingsModule/Program/Common touched — rule 4).
- [ ] MCP E2E (TC-E2E-01..05) documented and run **manually**; the manual gate is in the PR checklist (it is NOT in CI).
- [ ] R-09 manual items (TC-M-01..04) noted in the PR with their "not unit-verifiable" caveat; live round-trip executed (SM-01).
- [ ] Test naming follows `MethodName_ShouldExpectedBehavior_WhenCondition`; AAA + `because` per assertion + `[Description]` per test.
- [ ] PR includes the new/modified test files in the changed-files list and states the filter command used (e.g. `Validated: dotnet test --filter "Category=Unit"` + targeted module filter).

---

## PR Checklist Gate (MCP E2E manual)

Because `clio.mcp.e2e` is NOT in CI, the PR must confirm manually:

- [ ] Ran `clio.mcp.e2e/{Create,Update,Delete}ThemeToolE2ETests` against a real `clio mcp-server` — all six variants advertised with the FR-12 flags (TC-E2E-01..03).
- [ ] Ran the create→update→delete MCP round-trip (TC-E2E-04) and the `get-guidance theming` discovery (TC-E2E-05).
- [ ] Ran the live-stand CLI round-trip (TC-M-04) and confirmed the omitted-packageUId `CurrentPackageId` fallback (TC-M-03).
- [ ] "MCP reviewed" outcome stated per AGENTS.md MCP maintenance policy.
