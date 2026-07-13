# Story 6: `create-app-section` — route through the resolver, including doubled nested caption-culture and app-info calls (class c1)

**Feature**: mcp-passthrough-tool-parity
**FR coverage**: FR-01, FR-05, FR-05a, FR-10
**PRD**: [prd-mcp-passthrough-tool-parity.md](../prd/prd-mcp-passthrough-tool-parity.md)
**ADR**: [adr-mcp-passthrough-tool-parity.md](../adr/adr-mcp-passthrough-tool-parity.md)
**Status**: ready-for-dev
**Size**: L (full day)

---

## As a

CI pipeline author (AI-Platform gateway operator)

## I want

`create-app-section` (`ApplicationSectionCreateTool`) to execute against the header-supplied tenant on
every call in its graph — including the **two** caption-culture call sites and the **two**
`GetApplicationInfo` call sites (one of them inside the polling loop)

## So that

section creation under passthrough closes the same nested active-tenant leak as `create-app`, at every
duplicated call site `ApplicationSectionCreateCommand` makes

---

## Merge order (shared-file constraint)

Stories 3-9 all modify `clio/Command/McpServer/Tools/ApplicationTool.cs`, so they are **serialized:
3 → 4 → 5 → 6 → 7 → 8 → 9** via `depends_on`. This story starts only after **Story 5** merges.

## Acceptance Criteria

- [x] **AC-01** (PRD AC-02, decision-matrix "Route — full nested graph") — Given authorized passthrough and
  no `environment-name`, when `create-app-section` runs, then the section is created against the header
  tenant — never `Environment name is required.`.
- [x] **AC-02** — **Conditional requiredness (FR-05a) — blocking prerequisite for AC-01.** Given authorized
  passthrough, when `create-app-section` is called with no `environment-name`, then the MCP schema does
  **not** reject the call at pre-tool binding — `[Required]` is removed from `environment-name` on the
  corresponding `ApplicationSectionCreateArgs` record (ADR "CLI flag specification" table), making it
  schema-optional. On non-passthrough transports, runtime requiredness is enforced by the existing
  `IToolCommandResolver.ResolveSettingsAndKey`'s `EnvironmentResolutionException` throw (ADR OQ-03,
  "Resolver-ROUTED tools").
- [x] **AC-03** — **Nested caption-culture, readback site.** Given the same setup, when
  `ApplicationSectionCreateCommand`'s readback caption-culture call (today `:202`) runs, then it uses the
  settings-based `ICaptionCultureResolver.Resolve(EnvironmentSettings, ...)` overload and resolves the
  **header** tenant's culture.
- [x] **AC-04** — **Nested caption-culture, profile-validation site.** Given the same setup, when the
  profile-validation caption-culture call (today `:219`) runs, then it likewise uses the settings-based
  overload against the header tenant. (Both AC-03 and AC-04 must be independently tested — they are
  duplicated call sites, not one call reused twice in code.)
- [x] **AC-05** — **Nested `GetApplicationInfo`, validation site.** Given the same setup, when the
  `:219`-region `applicationInfoService.GetApplicationInfo(environmentName, ...)` call runs, then it uses the
  settings-based overload against the header tenant.
- [x] **AC-06** — **Nested `GetApplicationInfo`, polling site.** Given the same setup, when the polling-loop
  call (today `:737`) runs, then it likewise uses the settings-based overload against the header tenant.
- [x] **AC-07** — **Mixed input (PRD AC-06).** Given authorized passthrough with both the header and an
  explicit `environment-name` naming a different registered environment, when `create-app-section` runs
  (outer call and all four nested call sites), then it is rejected by `HasExplicitCredentialArgs` before any
  Creatio-reaching call anywhere in the graph.
- [x] **AC-08** (PRD AC-09 / SM-03) — Given stdio or registered-environment `mcp-http`, when
  `create-app-section` is called with `environment-name`, then behavior — including all four nested call
  sites — matches the pre-change baseline exactly.
- [ ] **AC-09** (PRD AC-07, concurrency isolation) — Given two concurrent `create-app-section` calls with
  different credentials, when both run, then each resolves a distinct authenticated container with no
  cross-tenant bleed across any of the nested calls (E2E proof owned by Story 15). *Not in this story's
  scope — E2E proof deferred to Story 15 per the row above.*
- [x] **AC-ERR** — **Error semantics respect the ENG-93208 middleware boundary (PRD AC-ERR).**
  (a) A **malformed/unusable** header is out of scope: the middleware returns HTTP 400 **before** any tool
  is invoked (`McpHttpServerCommand.cs:317`) — the tool is never entered and must not add handling for it.
  (b) Given a **valid** header whose target operation fails, when the tool executes, then it returns the
  typed error envelope with `SensitiveErrorTextRedactor`-redacted text.

## Implementation Notes

Depends on Story 2 (`ICaptionCultureResolver` + `IApplicationInfoService` settings-based overloads).

Add the settings-based `IApplicationSectionCreateService` overload (owned by THIS story, ADR slice 6f).
`ApplicationSectionCreateCommand` repeats the caption-culture-by-name call **twice** (`:202` readback,
`:219` profile validation) and the `GetApplicationInfo(environmentName, ...)` nested call **twice more**
(`:219` region and the polling loop at `:737`) — every one of these four is an independent name-based
`ISettingsRepository` touch that a single outer overload does not reach (ADR verification #4). The fix
replaces **all four**, not just the outer client-construction call.

`ApplicationSectionCreateTool` derives from `BaseTool<EnvironmentOptions>(null, logger, commandResolver)` +
`ExecuteWithCleanLog(options, ...)` (`BaseTool.cs:63`).

**Also remove `[Required]` from `environment-name`** on the `ApplicationSectionCreateArgs` record —
schema-optional so a header-only passthrough call reaches the tool instead of being rejected at MCP binding
(PRD A-02 / FR-05a). Non-passthrough requiredness is enforced by the resolver's existing throw.

Key files: `clio/Command/ApplicationSectionCreateCommand.cs`,
`clio/Command/EntitySchemaDesigner/CaptionCultureResolver.cs` (consume Story 2's overload),
`clio/Command/ApplicationInfoService.cs` (consume Story 2's overload),
`clio/Command/McpServer/Tools/ApplicationTool.cs`, `clio/Command/McpServer/Tools/ApplicationToolArgs.cs`.
Pattern to follow: Story 5 (`create-app`) — identical nested-call-graph discipline, doubled call sites here.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | Header-only executes against header tenant (outer call, schema no longer rejects a blank `environment-name`); **four separate, explicit tests** — one per nested call site (readback culture, validation culture, validation app-info, polling app-info) — asserting each is header-aware; mixed-input rejected end-to-end; registered-env/stdio unchanged | `clio.tests/Command/McpServer/ApplicationSectionCreateToolPassthroughTests.cs` |
| Integration `[Category("Integration")]` | none required | — |
| E2E `[Category("E2E")]` | Owned by **Story 15**: this tool carries the PRD FR-08 mandatory "one section tool" multi-tenant case (header-only + header+`environment-name`, including its nested caption-culture path) plus two-tenant isolation and stdio/`-e` no-regression. Manual only — MCP e2e not in CI | `clio.mcp.e2e/` (Story 15) |

Test naming: `MethodName_ShouldBehavior_WhenCondition`

## Definition of Done

- [x] All `CLIO*` diagnostics clean in changed files — including CLIO005 (FR-10)
- [x] Targeted tests green before commit: `dotnet test clio.tests/clio.tests.csproj --filter
  "Category=Unit&(Module=McpServer|Module=Command)" --no-build` (ADR slice 9)
- [x] All new/changed MCP arguments stay kebab-case (relaxing `[Required]` does not rename `environment-name`)
- [x] Unit tests added with `[Category("Unit")]` — never `[Category("UnitTests")]`
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started: 2026-07-10
- Implementation completed: 2026-07-11
- Tests passing: `dotnet test clio.tests/clio.tests.csproj -c Release --filter
  "Category=Unit&(Module=McpServer|Module=Command)" --no-build` — 4141 passed / 0 failed / 30 skipped on
  both net10.0 and net8.0 (2 x 4171 total). One unrelated pre-existing flake
  (`SettingsRepositoryFeatureTests.SetFeature_ShouldUpsertExistingFlag_WhenCalledTwice`, shared on-disk
  settings-file state across parallel tests) reproduced once on net8.0 and passed cleanly on isolated
  rerun — not touched by this story.
- Notes:
  - Added `IApplicationSectionCreateService.CreateSection(EnvironmentSettings, ...)` settings-based
    overload (`clio/Command/ApplicationSectionCreateCommand.cs`). Both public overloads converge on a
    private `CreateSectionCore` taking `resolveCaptionCulture`/`loadApplicationInfo` delegates so the
    name-based path stays byte-identical (AC-08) while the settings-based path threads
    `EnvironmentSettings` through all FOUR nested call sites: readback caption-culture (AC-03),
    profile-validation caption-culture (AC-04), pre-insert validation `GetApplicationInfo` (AC-05), and
    the `LoadCreatedSection` polling-loop `GetApplicationInfo` (AC-06, reached both on the normal
    success path and via `RecoverFromInsertTimeout`).
  - `ApplicationSectionCreateTool` reworked onto `BaseTool<EnvironmentOptions>` with the same
    `ExecuteWithCleanLog(options, ...)` + tenant-resolved-first pattern as Story 5's `ApplicationCreateTool`;
    tenant resolution happens before any Creatio-reaching call so mixed input (AC-07) is rejected end to
    end across the whole nested graph.
  - `ApplicationSectionCreateArgs.EnvironmentName` relaxed to schema-optional (FR-05a): moved after the
    required `application-code`/`caption` parameters (C# optional-after-required rule) and
    `[Required]` removed; `ToolContractGetTool`'s curated `create-app-section` contract entry updated to
    match (removed from `Required`, description now documents the passthrough-vs-transport boundary).
  - New fixture `clio.tests/Command/McpServer/ApplicationSectionCreateToolPassthroughTests.cs` drives a
    REAL `ApplicationSectionCreateService` (not a service substitute) through the tool so all four nested
    call sites are exercised for real; 11 tests cover AC-01/02 (header-only + schema-optional reflection),
    AC-03/04 (readback vs. profile-validation culture — independently asserted against distinct
    `Resolve(settings, override)` call signatures), AC-05 (validation app-info, isolated by forcing a
    later preparation-step failure so the polling site is never reached), AC-06 (polling app-info, proven
    by the returned `application-version` coming from the second/`afterInfo` read), AC-07 (mixed input),
    AC-08 (registered-env/stdio unchanged), AC-02 runtime-requiredness, AC-ERR(b) redacted envelope, and
    FR-05 tenant-lock-key derivation.
  - Added 3 settings-based-overload tests directly to `ApplicationSectionCreateServiceTests.cs`
    (null-argument guard, all-four-nested-sites-use-settings-overloads, and timeout-recovery polling via
    the settings overload — the last one captures the generated section id from the insert payload so
    the post-timeout verification-select genuinely matches instead of short-circuiting on a fixed literal).
  - Migrated all pre-existing `ApplicationSectionCreateTool`/`ApplicationSectionCreateService`-touching
    tests in `ApplicationToolTests.cs`, `CaptionCultureArgMappingToolTests.cs`,
    `CreateAppSectionCommandTests.cs`, and `ToolContractGetToolTests.cs` to the new constructor
    signature / settings-based mock plumbing / reordered `ApplicationSectionCreateArgs` positional args.
  - Gotcha discovered: `IApplicationClient.ExecutePostRequest` is a SINGLE method with a default
    `requestTimeout` parameter (not two overloads) — because the tool always passes explicit
    `BackgroundInsertTimeoutMs`/`BackgroundReadbackTimeoutMs` overrides, the section-readback-select and
    icon-background-update calls run with an EXPLICIT (non-default) timeout, so their NSubstitute stubs
    must configure the 3-arg call shape with `Arg.Any<int>()` — a 2-arg stub only matches the implicit
    `Timeout.Infinite` default and silently returns `null` for the actual (explicit-timeout) call,
    producing a downstream `NullReferenceException` that the tool's catch-all reports as an opaque
    "Object reference not set" error instead of the intended failure.
