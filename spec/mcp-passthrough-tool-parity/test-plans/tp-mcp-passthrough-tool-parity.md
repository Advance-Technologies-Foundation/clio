# Test Plan: MCP `mcp-http` Credential-Passthrough Tool Parity

**Feature**: mcp-passthrough-tool-parity
**Jira**: ENG-93347 (sub-task of ENG-92790; builds on ENG-93208; blocks ENG-92869)
**PRD**: [prd-mcp-passthrough-tool-parity.md](../prd/prd-mcp-passthrough-tool-parity.md)
**ADR**: [adr-mcp-passthrough-tool-parity.md](../adr/adr-mcp-passthrough-tool-parity.md)
**Stories**: [story-mcp-passthrough-tool-parity-1](../stories/story-mcp-passthrough-tool-parity-1.md) … [-17](../stories/story-mcp-passthrough-tool-parity-17.md) (17 stories, tracked in `spec/sprint-status.yaml`)
**Author**: QA Planner Agent
**Status**: Draft
**Created**: 2026-07-11

---

## Branching constraint (restated — affects where tests run)

All test code in this plan compiles **only** against `claude/clio-mcp-multi-tenant-73a807` (the ENG-93208
umbrella branch), never `master` — the seam types the tests exercise (`CredentialContext`,
`ICredentialContextAccessor`, `IToolCommandResolver.Resolve`'s passthrough branch,
`HasExplicitCredentialArgs`) do not exist on `master`. Baseline comparisons ("pre-change behavior") mean
**the umbrella branch before this feature's commits**, not `master`.

---

## Scope

### In scope

- **Unit coverage for every named broken tool and every audited dependency path** (PRD FR-08): the 7
  class-c1 Application tools (`list-apps`, `get-app-info`, `create-app`, `create-app-section`,
  `update-app-section`, `delete-app-section`, `list-app-sections`) including their **nested**
  caption-culture / app-info / find-application-id paths; class-c2 `get-user-culture` (active-tenant leak);
  class-c3 `link-from-repository-*` fail-fast via `ICredentialPassthroughToolGuard` (including the
  `skip-preparation=false` package-path branch); the four matrix tools (`update-page`, `sync-pages` incl.
  blank-name early-return removal, `get-component-info` mixed-input path, `build-theme` Pattern A/B
  redesign).
- **Per-scenario coverage of the three passthrough input shapes** for every routed/guarded path:
  header-only, mixed input (header + explicit `environment-name` — confused-deputy, PRD AC-06),
  registered-env/stdio no-regression (PRD AC-09 / SM-03).
- **Shared c1 dependency contracts** (Story 2): settings-based `ICaptionCultureResolver` +
  `IApplicationInfoService` overloads never touch `ISettingsRepository`; name-based overloads observably
  unchanged.
- **FR-06/OQ-04 guard** (Story 16): classification registry completeness against
  `McpFeatureToggleFilter.GetAttributedTypes`, plus exact **(tool, dependency-path, scenario) → named test
  method** mapping-presence verification.
- **Consolidated MCP E2E** (Story 15): multi-tenant, two-tenant concurrency isolation for all 12 newly
  routed tools, and a stdio + `mcp-http -e <env>` no-regression sweep for all 15 touched tools —
  **manual live-stand execution; NOT in CI**.
- **Per-slice FR-10 validation** carried by every code story (1–16): clean `CLIO*` diagnostics incl.
  CLIO005, targeted `dotnet test --filter "Category=Unit&Module=McpServer"` (widened per story's DoD).

### Out of scope

- **ENG-93208 middleware error semantics** — no-header → forward as registered/default path
  (`McpHttpServerCommand.cs:272`); malformed header → HTTP 400 **before** any tool
  (`McpHttpServerCommand.cs:317`, already covered by `CredentialPassthroughMiddlewareTests.cs`). Fixed
  ground per PRD Non-goals / AC-ERR; tools must NOT add handling and this plan adds no tests there.
- **Class (a)/(b) tools and out-of-scope tools** (telemetry, guidance, local infra, `list-creatio-builds`,
  …) — no behavior change; they appear only as `NotApplicable` registry rows (TC-U-86) and in the existing
  regression suites.
- **Latency/perf assertions** — explicitly excluded by PRD OQ-05 / ADR OQ-05 (SM-03 is functional-only).
- **Roslyn-analyzer variant of the FR-06 guard** — the ADR chose allowlist + mapping (OQ-04); no analyzer
  fixture tests are planned.
- **New CLI verbs/flags** — none exist in this feature; CLI-side coverage is limited to proving existing
  CLI paths unchanged (Story 14 AC-05, Story 2 AC-04).

---

## Test conventions (binding for every case below)

| Rule | Value |
|------|-------|
| Frameworks | NUnit **4.5.1** (runner), FluentAssertions **7.2.0** (assertions), NSubstitute **5.3.0** (mocks) |
| Categories | `[Category("Unit")]` / `[Category("Integration")]` / `[Category("E2E")]` — **NEVER** `[Category("UnitTests")]` or any other string; no uncategorized tests |
| Module trait | Every new fixture carries `[Property("Module", "McpServer")]` (or `"Command"` for fixtures under `clio.tests/Command/` root) so the smart-regression filters select it — pattern: `GetUserCultureToolTests.cs:14-15` |
| Naming | `MethodName_ShouldExpectedBehavior_WhenCondition` — no exceptions |
| Structure | Explicit `// Arrange` / `// Act` / `// Assert`; **every** assertion carries `because:`; **every** test method carries `[Description("...")]` |
| Fixture base | `BaseCommandTests<TOptions>` for command-class tests (e.g. `BuildThemeCommandTests` extensions); MCP tool classes are **not** `Command<TOptions>`, so tool fixtures are plain `[TestFixture]` with explicit category + module trait; resolve SUTs from DI where a container fixture exists, clear substitute received calls in teardown |
| Cross-OS | All unit tests must run on macOS/Linux/Windows — no OS-specific paths; E2E stand config via `CLIO_MCP_HTTP_E2E_*` env vars with `Assert.Ignore` self-skip |
| Secret hygiene | No assertion may echo `accessToken`/`login`/`password`; error-path assertions match `SensitiveErrorTextRedactor`-redacted text (reuse `CredentialPassthroughSecretHygieneTests` helpers) |

---

## Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|-----------|
| Regression in `ApplicationToolTests.cs` (95 KB) — all 7 c1 tools rewired to `BaseTool<EnvironmentOptions>` ctors + `ExecuteWithCleanLog(options,…)`, `[Required]` removed on 7 args records | **High** | **High** | Serialized merge chain 3→4→5→6→7→8→9; every c1 story runs `Category=Unit&(Module=McpServer|Module=Command)` before commit; existing `ApplicationToolTests` in the regression-guard set (below) must pass with only construction-site updates, never assertion changes |
| Nested-path fix ships incomplete (only the outer call routed — the exact Rev-1 defect ADR verification #4 caught) | Med | **High** | One **separate, named** unit test per nested call site (TC-U-31/32, TC-U-38–41, TC-U-46/47, TC-U-52, TC-U-57); Story 16 registry requires a row per dependency-path, so a missing nested test fails the guard |
| `get-component-info` header-only compliance regressed by the mixed-input fix (PRD: "do not fix it into a regression") | Med | High | Dedicated regression case TC-U-72; existing `ComponentInfoToolTests.cs:606` must pass **unchanged** (Story 13 DoD) |
| `sync-pages` probe fix silently non-functional (blank-name short-circuit left in — the Rev-1 ADR bug) | Med | High | TC-U-67 asserts the resolver **was invoked** (`Received(1)`), not merely "returns a version" — a returns-only test does not satisfy Story 12 AC-01 |
| `[Required]` relaxation breaks existing MCP clients relying on pre-tool binding rejection (compatibility-sensitive, ADR Consequences) | Med | Med | Per-args-record attribute-shape unit tests (TC-U-20/26/30/37/45/51/56, TC-U-69, TC-U-11) + non-passthrough `EnvironmentResolutionException` runtime-requiredness tests + E2E no-regression sweep (TC-E-21…35) |
| `build-theme` CLI path drifts (new overloads leak into the CLI branch) | Low | High | TC-U-82: existing `BuildThemeCommandTests` pass **unmodified**; TC-U-77: direct-construction `BuildThemeToolTests.cs:49` passes **unchanged** (optional resolver param) |
| CLIO005 dead-DI on `ICredentialPassthroughToolGuard` if registration and consumer split across commits | Med | Med | Story 1 lands guard + consumer + tests in one slice; DoD requires CLIO005-clean build |
| `BindingsModule.cs` touched (Stories 1, 10) — DI composition root affects all modules | Med | High | Repo full-suite trigger rule 4: run the **full** `Category=Unit` suite for those stories, not just the module filter |
| Shared `CaptionCultureResolver` overload (Story 2) breaks CLI/stdio callers of the name-based overload | Low | High | TC-U-14/18 parity tests with NSubstitute `Received`-sequence checks; existing `CaptionCultureResolverTests`, `EntitySchemaCaptionCultureResolverTests`, `CaptionCultureScriptGuardTests` in regression guard |
| `Link4RepoCommand` behavior accidentally changed (Story 1 must be tool-level only) | Low | High | Existing `Link4RepoCommand.Tests/.PreparationTests/.UnlockedTests` must pass unchanged; TC-U-07 asserts the NEW tool-level error, proving the command's validator was not repurposed |
| MCP E2E **not in CI** — the deepest coverage (multi-tenant, isolation, binding-layer `[Required]` behavior) is manual | **High** | Med | Manual live-stand execution is a **hard gate** in Story 15 DoD; results per case recorded in the Dev Agent Record; PR checklist item "MCP e2e NOT in CI — manual run attached"; suites must still compile in CI and self-skip |
| Story 16 registry drifts vs. actual test methods (rename breaks `nameof` mapping) | Low | Low | `nameof(...)` references fail at compile time on rename; TC-U-83/84 fail on any discovery or mapping gap |
| Concurrency/lock regression — c1 tools bypass the per-tenant lock by using the zero-arg `ExecuteWithCleanLog` | Med | Med | TC-U-23 (reference pattern test on `list-apps`) asserts the options-aware overload / `GetTenantKey(options)` lock ceremony; ADR pre-implementation checklist item; E2E isolation proofs TC-E-09…20 |

### Existing test files at risk (grep-verified in the current tree)

`clio.tests/Command/McpServer/`: `ApplicationToolTests.cs`, `ApplicationCreateEnrichmentServiceTests.cs`,
`GetUserCultureToolTests.cs`, `LinkFromRepositoryToolTests.cs`, `PageSyncToolTests.cs`,
`PageSyncToolBaselineTests.cs`, `PageUpdateToolBaselineTests.cs`, `PageUpdateToolRunProcessTests.cs`,
`ComponentInfoToolTests.cs`, `BuildThemeToolTests.cs`, `PlatformVersionResolverTests.cs`,
`ToolCommandResolverTests.cs`, `ToolCommandResolverCacheKeyTests.cs`, `ToolCommandResolverNoWriteTests.cs`,
`CredentialPassthrough{ClientIdentity,DiRegistration,Di,Middleware,SecretHygiene}Tests.cs`.
`clio.tests/Command/`: `ApplicationInfoServiceTests.cs`, `ApplicationCreateServiceTests.cs`,
`ApplicationSectionCreateServiceTests.cs`, `ApplicationSectionUpdateServiceTests.cs`,
`ApplicationSectionDeleteServiceTests.cs`, `CaptionCultureResolverTests.cs`,
`EntitySchemaCaptionCultureResolverTests.cs`, `CaptionCultureScriptGuardTests.cs`,
`BuildThemeCommandTests.cs`, `Link4RepoCommand.{Tests,PreparationTests,UnlockedTests}.cs`.
`clio.mcp.e2e/`: `McpHttpMultiTenantE2ETests.cs`, `McpHttpConcurrencyIsolationE2ETests.cs`,
`McpHttpNoRegressionE2ETests.cs`.

> **File-location notes for implementers (source-verified deltas vs. story text):**
> 1. Story 11 says "`PageUpdateToolTests.cs` (extend)" — **no fixture of that name exists**; the existing
>    fixtures are `PageUpdateToolBaselineTests.cs` / `PageUpdateToolRunProcessTests.cs`. Create
>    `PageUpdateToolPassthroughTests.cs` (consistent with the other new `*PassthroughTests` fixtures) and
>    point Story 16's registry rows there.
> 2. Story 2 names `clio.tests/Command/EntitySchemaDesigner/CaptionCultureResolverTests.cs` — the fixture
>    actually lives at `clio.tests/Command/CaptionCultureResolverTests.cs`. Extend it in place; do not
>    create a duplicate directory.

---

## Unit Tests (`clio.tests/`) — TC-U inventory

All cases: `[Category("Unit")]`, module trait per table, NSubstitute mocks only (Creatio mocked at the
`IApplicationClient` / service-interface boundary), AAA + `because` + `[Description]`.

### Story 1 — c3 guard + `link-from-repository-*` fail-fast
Fixtures: `clio.tests/Command/McpServer/LinkFromRepositoryToolPassthroughTests.cs` (new),
`clio.tests/Command/McpServer/CredentialPassthroughToolGuardTests.cs` (new). Module: `McpServer`.

| TC | Test (naming per convention) | Asserts | Story AC / PRD |
|----|------------------------------|---------|----------------|
| TC-U-01 | `LinkFromRepositoryByEnvironment_ShouldReturnUniformPassthroughRejection_WhenHeaderOnly` | Uniform "not supported under credential passthrough" error naming tool + alternative; **no** Creatio-reaching call (`ISysSettingsManager`/`IPackageLockManager` substitutes `Received(0)`); never the generic validator message | S1 AC-01 / FR-04 |
| TC-U-02 | `LinkFromRepositoryUnlocked_ShouldReturnUniformPassthroughRejection_WhenHeaderOnly` | Same as TC-U-01 for the always-Creatio-reaching `unlocked` branch (`_applicationPackageListProvider.GetPackages` `Received(0)`) | S1 AC-01 |
| TC-U-03 | `LinkFromRepository_ShouldRejectBeforeAnyCreatioCall_WhenHeaderAndEnvironmentNameBothPresent` (`[TestCase]` over by-environment / unlocked) | Mixed input rejected before any Creatio call; named registered env's stored creds never used | S1 AC-02 / PRD AC-06 |
| TC-U-04 | `LinkFromRepositoryByEnvPackagePath_ShouldReturnUniformRejection_WhenSkipPreparationFalseAndHeaderOnly` | Guard fires on the `!SkipPreparation` (Creatio-reaching preparation) branch; preparation substitutes `Received(0)` | S1 AC-03 |
| TC-U-05 | `LinkFromRepositoryByEnvPackagePath_ShouldReject_WhenSkipPreparationFalseAndMixedInput` | Same branch, mixed input → rejection, no preparation call | S1 AC-04 |
| TC-U-06 | `LinkFromRepositoryByEnvPackagePath_ShouldNotBeRejected_WhenSkipPreparationTrueUnderPassthrough` | Guard does NOT fire (local-only mode); call proceeds to `InternalExecute` | S1 AC-05 |
| TC-U-07 | `LinkFromRepository_ShouldReturnExplicitRequiredError_WhenNonPassthroughAndEnvironmentNameBlank` (`[TestCase]` over the two name-based methods) | The NEW tool-level `"environment-name is required for <tool> outside credential passthrough."` — explicitly NOT `Link4RepoOptionsValidator`'s "Either path to creatio directory or environment name must be provided" | S1 AC-06 / ADR OQ-03 |
| TC-U-08 | `LinkFromRepository_ShouldMatchBaseline_WhenRegisteredEnvironmentNameSupplied` (`[TestCase]` over all 3 methods) | Non-passthrough with env-name → same `InternalExecute(options)` dispatch/options mapping as pre-change | S1 AC-07 / PRD AC-09 |
| TC-U-09 | `RejectIfPassthroughUnsupported_ShouldReturnNull_WhenPassthroughInactive` | `BaseTool` helper returns `null` when guard absent or `IsPassthroughActive == false` | S1 guard isolation |
| TC-U-10 | `BuildUnsupportedMessage_ShouldNameToolAndAlternativeWithoutSecrets_WhenPassthroughActive` | Message shape: contains tool name + alternative guidance; contains no `accessToken`/`login`/`password` material | S1 AC-ERR / FR-04 |
| TC-U-11 | `LinkFromRepositoryArgs_ShouldRelaxRequiredOnEnvironmentNameOnly_WhenSchemaInspected` | Reflection: `[Required]` removed from `environment-name` on the two name-based methods; `envPkgPath` **still** `[Required]` | S1 AC-05 / FR-05a |

### Story 2 — shared c1 settings-based overloads
Fixtures: `clio.tests/Command/CaptionCultureResolverTests.cs` (extend),
`clio.tests/Command/ApplicationInfoServiceTests.cs` (extend). Module: `Command`.

| TC | Test | Asserts | Story AC |
|----|------|---------|----------|
| TC-U-12 | `Resolve_ShouldNeverTouchSettingsRepository_WhenSettingsOverloadUsed` | `ISettingsRepository.GetEnvironment`/`FindEnvironment` `Received(0)`; culture resolved via `ICurrentUserCultureResolverFactory.Create(settings)` | S2 AC-01 |
| TC-U-13 | `Resolve_ShouldThrowArgumentNullException_WhenSettingsNull` | Guard clause throws **before** any factory invocation (`CreateEnvironmentClient` `Received(0)`) | S2 AC-ERR |
| TC-U-14 | `Resolve_ShouldBehaveUnchanged_WhenOptionsOverloadUsed` | Name-based overload: identical result + identical `Received`-call sequence as baseline; all pre-existing tests pass unmodified | S2 AC-04 |
| TC-U-15 | `GetApplicationInfo_ShouldNeverTouchSettingsRepository_WhenSettingsOverloadUsed` | Settings overload resolves against supplied settings; no name-based repo lookup | S2 AC-02 |
| TC-U-16 | `FindApplicationId_ShouldNeverCallFindEnvironment_WhenSettingsOverloadUsed` | Behavior identical to name-based overload minus the `FindEnvironment` call | S2 AC-03 |
| TC-U-17 | `GetApplicationInfo_ShouldThrowArgumentNullException_WhenSettingsNull` (+ `FindApplicationId` twin) | Null-guard fires before factory invocation — one named test per overload | S2 AC-ERR |
| TC-U-18 | `ApplicationInfoService_ShouldBehaveUnchanged_WhenNameBasedOverloadsUsed` | Parity for both existing name-based members (`Received`-sequence check); existing named tests pass unmodified | S2 AC-04 |

### Stories 3–9 — c1 Application tools (serialized 3→9)
Fixtures (all new, Module `McpServer`): `ApplicationGetListToolPassthroughTests.cs`,
`ApplicationGetInfoToolPassthroughTests.cs`, `ApplicationCreateToolPassthroughTests.cs`,
`ApplicationSectionCreateToolPassthroughTests.cs`, `ApplicationSectionUpdateToolPassthroughTests.cs`,
`ApplicationSectionDeleteToolPassthroughTests.cs`, `ApplicationSectionGetListToolPassthroughTests.cs`
under `clio.tests/Command/McpServer/`.

Representative header-only case (Story 3 — the pattern all seven share):

```csharp
[Test]
[Description("PRD AC-01: under authorized passthrough with no environment-name, list-apps executes " +
             "against the header tenant instead of throwing 'Environment name is required.'")]
public void ApplicationGetList_ShouldExecuteAgainstHeaderTenant_WhenHeaderOnly() {
    // Arrange
    EnvironmentSettings headerSettings = CreateEphemeralHeaderSettings();
    _commandResolver.Resolve<EnvironmentSettings>(Arg.Is<EnvironmentOptions>(o =>
        string.IsNullOrWhiteSpace(o.Environment))).Returns(headerSettings);
    _applicationListService.GetApplications(headerSettings, Arg.Any<string>(), Arg.Any<string>())
        .Returns(ExpectedApplications);
    ApplicationGetListTool sut = new(_logger, _commandResolver, _applicationListService);

    // Act
    ApplicationListResponse result = sut.ApplicationGetList(new ApplicationGetListArgs());

    // Assert
    result.Success.Should().BeTrue(because: "a header-only passthrough call must route to the header tenant");
    _applicationListService.Received(1).GetApplications(headerSettings, Arg.Any<string>(), Arg.Any<string>());
    _applicationListService.DidNotReceive().GetApplications(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    // because: the name-based overload is the header-blind bypass this feature removes (ADR OQ-01 c1)
}
```

| TC | Story | Test | Asserts |
|----|-------|------|---------|
| TC-U-19 | 3 | `ApplicationGetList_ShouldExecuteAgainstHeaderTenant_WhenHeaderOnly` | Settings-based `GetApplications` called with resolver-produced settings; name-based overload `DidNotReceive`; never `ArgumentException("Environment name is required.")` |
| TC-U-20 | 3 | `ApplicationGetListArgs_ShouldNotCarryRequiredOnEnvironmentName_WhenSchemaInspected` | Reflection: `[Required]` removed (FR-05a) — blocking prerequisite for TC-U-19 |
| TC-U-21 | 3 | `ApplicationGetList_ShouldRejectCall_WhenHeaderAndEnvironmentNameBothPresent` | Resolver throws (`HasExplicitCredentialArgs`); service never called; named env's stored creds never used |
| TC-U-22 | 3 | `ApplicationGetList_ShouldBehaveUnchanged_WhenRegisteredEnvironmentStdio` | Registry-branch resolution unchanged; non-passthrough blank name surfaces `EnvironmentResolutionException` (runtime requiredness, ADR OQ-03) |
| TC-U-23 | 3 | `ApplicationGetList_ShouldUsePerTenantExecutionLock_WhenExecuted` | The **options-aware** `ExecuteWithCleanLog(options, …)` path: `GetTenantKey(options)` consulted, `MarkInUse`/`MarkAvailable` around the body — NOT `SharedFallbackKey` (FR-05; reference test for the c1 pattern, guarded across tools 4–9 by identical wiring + Story 16 registry) |
| TC-U-24 | 3 | `ApplicationGetList_ShouldReturnRedactedTypedError_WhenTenantOperationFails` | Typed `ApplicationToolResponses` error envelope; `SensitiveErrorTextRedactor`-redacted; no secret material (AC-ERR) |
| TC-U-25 | 4 | `ApplicationGetInfo_ShouldExecuteAgainstHeaderTenant_WhenHeaderOnly` | Settings-based `GetApplicationInfo(EnvironmentSettings,…)` used; name-based `DidNotReceive` |
| TC-U-26 | 4 | `ApplicationGetInfoArgs_ShouldNotCarryRequiredOnEnvironmentName_WhenSchemaInspected` | `[Required]` removed |
| TC-U-27 | 4 | `ApplicationGetInfo_ShouldRejectCall_WhenHeaderAndEnvironmentNameBothPresent` | Mixed-input rejection before any Creatio call |
| TC-U-28 | 4 | `ApplicationGetInfo_ShouldBehaveUnchanged_WhenRegisteredEnvironmentStdio` | Baseline parity + non-passthrough requiredness throw |
| TC-U-29 | 5 | `ApplicationCreate_ShouldExecuteAgainstHeaderTenant_WhenHeaderOnly` | Outer settings-based `CreateApplication(EnvironmentSettings,…)` used |
| TC-U-30 | 5 | `ApplicationCreateArgs_ShouldNotCarryRequiredOnEnvironmentName_WhenSchemaInspected` | `[Required]` removed |
| TC-U-31 | 5 | `CreateApplication_ShouldResolveHeaderTenantCulture_WhenHeaderOnly` | **Nested caption-culture site** (`ApplicationCreateService.cs:88`): settings-based `ICaptionCultureResolver.Resolve(EnvironmentSettings,…)` `Received(1)`; options-based overload + `ISettingsRepository.GetEnvironment` `Received(0)` — the active-tenant culture leak stays closed (crux of Story 5, ADR verification #4) |
| TC-U-32 | 5 | `CreateApplication_ShouldPollHeaderTenant_WhenTimeoutPollingRuns` | **Nested polling/readback site** (`:471,:484`): settings-based `GetApplicationInfo(EnvironmentSettings,…)` used in the loop; name-based `Received(0)` |
| TC-U-33 | 5 | `ApplicationCreate_ShouldLeaveEnrichmentPathUntouched_WhenExecuted` | Enrichment still resolves `IDataForgeContextService` via `commandResolver.Resolve` per request (class-b compliant, must not be modified) |
| TC-U-34 | 5 | `ApplicationCreate_ShouldRejectCall_WhenHeaderAndEnvironmentNameBothPresent` | Mixed input rejected before ANY call in the graph (outer + nested substitutes all `Received(0)`) |
| TC-U-35 | 5 | `ApplicationCreate_ShouldBehaveUnchanged_WhenRegisteredEnvironmentStdio` | Baseline parity including nested calls |
| TC-U-36 | 6 | `ApplicationSectionCreate_ShouldExecuteAgainstHeaderTenant_WhenHeaderOnly` | Outer settings-based overload used |
| TC-U-37 | 6 | `ApplicationSectionCreateArgs_ShouldNotCarryRequiredOnEnvironmentName_WhenSchemaInspected` | `[Required]` removed |
| TC-U-38 | 6 | `ApplicationSectionCreate_ShouldResolveHeaderTenantCulture_WhenReadbackCultureSiteRuns` | Nested caption-culture **readback** site (`:202`) header-aware — independent test |
| TC-U-39 | 6 | `ApplicationSectionCreate_ShouldResolveHeaderTenantCulture_WhenProfileValidationCultureSiteRuns` | Nested caption-culture **profile-validation** site (`:219`) header-aware — independent test (duplicated call sites, per Story 6 AC-04) |
| TC-U-40 | 6 | `ApplicationSectionCreate_ShouldUseHeaderTenantAppInfo_WhenValidationSiteRuns` | Nested `GetApplicationInfo` validation site header-aware |
| TC-U-41 | 6 | `ApplicationSectionCreate_ShouldUseHeaderTenantAppInfo_WhenPollingSiteRuns` | Nested `GetApplicationInfo` polling-loop site (`:737`) header-aware |
| TC-U-42 | 6 | `ApplicationSectionCreate_ShouldRejectCall_WhenHeaderAndEnvironmentNameBothPresent` | Mixed input rejected across outer + all four nested sites |
| TC-U-43 | 6 | `ApplicationSectionCreate_ShouldBehaveUnchanged_WhenRegisteredEnvironmentStdio` | Baseline parity across all four nested sites |
| TC-U-44 | 7 | `ApplicationSectionUpdate_ShouldExecuteAgainstHeaderTenant_WhenHeaderOnly` | Outer settings-based overload used |
| TC-U-45 | 7 | `ApplicationSectionUpdateArgs_ShouldNotCarryRequiredOnEnvironmentName_WhenSchemaInspected` | `[Required]` removed |
| TC-U-46 | 7 | `ApplicationSectionUpdate_ShouldResolveHeaderTenantCulture_WhenNestedCultureSiteRuns` | Nested caption-culture site (`:87`) header-aware |
| TC-U-47 | 7 | `ApplicationSectionUpdate_ShouldUseHeaderTenantAppInfo_WhenNestedAppInfoSiteRuns` | Nested `GetApplicationInfo` site (`:93`) header-aware |
| TC-U-48 | 7 | `ApplicationSectionUpdate_ShouldRejectCall_WhenHeaderAndEnvironmentNameBothPresent` | Mixed input rejected (outer + both nested) |
| TC-U-49 | 7 | `ApplicationSectionUpdate_ShouldBehaveUnchanged_WhenRegisteredEnvironmentStdio` | Baseline parity |
| TC-U-50 | 8 | `ApplicationSectionDelete_ShouldExecuteAgainstHeaderTenant_WhenHeaderOnly` | Outer settings-based overload used |
| TC-U-51 | 8 | `ApplicationSectionDeleteArgs_ShouldNotCarryRequiredOnEnvironmentName_WhenSchemaInspected` | `[Required]` removed |
| TC-U-52 | 8 | `ApplicationSectionDelete_ShouldFindApplicationIdOnHeaderTenant_WhenNestedLookupRuns` | Nested `FindApplicationId(EnvironmentSettings,…)` (`:76`) header-aware; name-based `Received(0)` |
| TC-U-53 | 8 | `ApplicationSectionDelete_ShouldRejectCall_WhenHeaderAndEnvironmentNameBothPresent` | Mixed input rejected |
| TC-U-54 | 8 | `ApplicationSectionDelete_ShouldBehaveUnchanged_WhenRegisteredEnvironmentStdio` | Baseline parity; destructive/idempotent MCP metadata attributes **unchanged** (Story 8 note) |
| TC-U-55 | 9 | `ApplicationSectionGetList_ShouldExecuteAgainstHeaderTenant_WhenHeaderOnly` | Outer settings-based overload used |
| TC-U-56 | 9 | `ApplicationSectionGetListArgs_ShouldNotCarryRequiredOnEnvironmentName_WhenSchemaInspected` | `[Required]` removed |
| TC-U-57 | 9 | `ApplicationSectionGetList_ShouldFindApplicationIdOnHeaderTenant_WhenNestedLookupRuns` | Nested `FindApplicationId` (`:74`) header-aware |
| TC-U-58 | 9 | `ApplicationSectionGetList_ShouldRejectCall_WhenHeaderAndEnvironmentNameBothPresent` | Mixed input rejected |
| TC-U-59 | 9 | `ApplicationSectionGetList_ShouldBehaveUnchanged_WhenRegisteredEnvironmentStdio` | Baseline parity |

### Story 10 — `get-user-culture` (c2 leak)
Fixture: `clio.tests/Command/McpServer/GetUserCultureToolPassthroughTests.cs` (new). Module: `McpServer`.

| TC | Test | Asserts |
|----|------|---------|
| TC-U-60 | `GetUserCulture_ShouldResolveHeaderTenant_WhenHeaderOnly` | `commandResolver.Resolve<EnvironmentSettings>` used; `settingsRepository.GetEnvironment` `Received(0)` |
| TC-U-61 | `GetUserCulture_ShouldNeverReadActiveEnvironment_WhenHeaderOnlyAndActiveEnvironmentConfigured` | **The leak condition itself**: fixture configures an active environment (`ActiveEnvironmentKey` set); active env's stored creds/culture never touched — `FindEnvironment(null)` fallback provably dead (PRD Security mode ii / S10 AC-02) |
| TC-U-62 | `GetUserCulture_ShouldRejectCall_WhenHeaderAndExplicitArgsBothPresent` | Mixed input (`environment-name` / `uri` / `login` / `password`) rejected by `HasExplicitCredentialArgs` before any Creatio call |
| TC-U-63 | `GetUserCulture_ShouldBehaveUnchanged_WhenExplicitArgsOrRegisteredEnvironmentOnStdio` | Explicit-arg + registered-env behavior byte-for-byte baseline (existing `GetUserCultureToolTests.cs` passes with only ctor-wiring updates) |

### Stories 11–14 — matrix tools
Fixtures: `PageUpdateToolPassthroughTests.cs` (new — see file-location note), `PageSyncToolTests.cs`
(extend), `ComponentInfoToolTests.cs` (extend — **do not remove** the `:606` coverage),
`BuildThemeToolTests.cs` + `clio.tests/Command/BuildThemeCommandTests.cs` (extend both). Module:
`McpServer` (`Command` for the command fixture).

The `sync-pages` probe-reached case — the single most regression-prone assertion in this feature:

```csharp
[Test]
[Description("Story 12 AC-01: header-only sync-pages must REACH the version probe's resolver — the " +
             "blank-environmentName short-circuit at PageSyncTool.cs:93 is removed; a test asserting " +
             "only 'returns a version' does not prove this.")]
public void ResolvePlatformVersionAsync_ShouldInvokeResolver_WhenHeaderOnlyWithBlankEnvironmentName() {
    // Arrange
    _commandResolver.Resolve<EnvironmentSettings>(Arg.Any<EnvironmentOptions>())
        .Returns(CreateEphemeralHeaderSettings());
    PageSyncTool sut = CreateSut();

    // Act
    sut.SyncPages(new PageSyncArgs { /* EnvironmentName deliberately blank */ });

    // Assert
    _commandResolver.Received(1).Resolve<EnvironmentSettings>(Arg.Any<EnvironmentOptions>());
    // because: the pre-change guard clause short-circuited on a blank name and silently degraded to
    // 'latest' without ever consulting the credential context (ADR "Matrix tools", sync-pages row)
}
```

| TC | Story | Test | Asserts |
|----|-------|------|---------|
| TC-U-64 | 11 | `ResolvePlatformVersionAsync_ShouldResolveHeaderTenant_WhenHeaderOnly` | Probe calls `commandResolver.Resolve<EnvironmentSettings>`; `settingsRepository.GetEnvironment` `Received(0)`; no blank-name early return exists in this tool — probe reached on every input shape |
| TC-U-65 | 11 | `ResolvePlatformVersionAsync_ShouldRejectBeforeNamedTenantLookup_WhenHeaderAndEnvironmentNameBothPresent` | Mixed input: resolver rejection fires before any named-tenant lookup/probe |
| TC-U-66 | 11 | `PageUpdate_ShouldBehaveUnchanged_WhenRegisteredEnvironmentStdio` | Probe **and** the already-compliant write path (`:64`) baseline-identical |
| TC-U-67 | 12 | `ResolvePlatformVersionAsync_ShouldInvokeResolver_WhenHeaderOnlyWithBlankEnvironmentName` | **Probe REACHED** — `Received(1)` on the resolver, not short-circuited (code above) |
| TC-U-68 | 12 | `ResolvePlatformVersionAsync_ShouldFallBackToLatestOnlyWhenResolverThrows_WhenHeaderOnly` | Header tenant version when resolve succeeds; `latest` fail-soft **only** on resolver throw — never a silent always-latest degrade |
| TC-U-69 | 12 | `PageSyncArgs_ShouldNotCarryRequiredOnEnvironmentName_WhenSchemaInspected` | `[Required]` removed from `PageSyncArgs.EnvironmentName` (`:951`) (FR-05a / PRD AC-05) |
| TC-U-70 | 12 | `ResolvePlatformVersionAsync_ShouldRejectBeforeNamedTenantProbe_WhenHeaderAndEnvironmentNameBothPresent` | Ordering fixed: header-aware/rejecting path runs **before** the named-env probe (pre-change bug: probe ran before the `:283` rejection) |
| TC-U-71 | 12 | `PageSync_ShouldBehaveUnchanged_WhenRegisteredEnvironmentStdio` | Baseline parity + non-passthrough blank name → `EnvironmentResolutionException` |
| TC-U-72 | 13 | `GetComponentInfo_ShouldKeepLatestFallback_WhenHeaderOnlyWithNoEnvironmentOrUri` | **Regression guard**: `CreateNoActiveEnvironmentFallback` + loud `latest-fallback` flag byte-for-byte unchanged; existing `:606` test untouched (PRD: "do not fix it into a regression") |
| TC-U-73 | 13 | `ResolveEnvironmentSettings_ShouldUseResolver_WhenEnvironmentNameOrUriSupplied` | `hasEnvironment` branch swapped to `commandResolver.Resolve<EnvironmentSettings>`; root `GetEnvironment` `Received(0)`; mixed input rejected before any named-tenant probe |
| TC-U-74 | 13 | `GetComponentInfo_ShouldBehaveUnchanged_WhenRegisteredEnvironmentStdio` | Baseline parity for both `environment-name` and `uri` variants |
| TC-U-75 | 14 | `BuildTheme_ShouldResolveVersionAgainstHeaderTenant_WhenHeaderOnlyAndResolverSupplied` | Tool resolves settings; command's new overload uses `_resolverFactory.Create(resolvedSettings)`; no newest-template guess |
| TC-U-76 | 14 | `BuildTheme_ShouldFallSoftToLatestFallback_WhenHeaderAndEnvironmentNameBothPresent` | Resolver throws (mixed-input rejection) → caught → `resolvedSettings = null` → `LatestFallback`; **no** Creatio-reaching call on the rejected path (S14 AC-03 / PRD AC-06) |
| TC-U-77 | 14 | `BuildTheme_ShouldKeepLatestFallback_WhenNoResolverSupplied` | Existing direct-construction shape (`BuildThemeToolTests.cs:49`, optional ctor param) compiles and behaves unchanged — the existing test itself must pass **unmodified** |
| TC-U-78 | 14 | `BuildTheme_ShouldResolveRegisteredEnvironment_WhenNonPassthroughEnvironmentNameSupplied` | Registry lookup via the tool's `Resolve<EnvironmentSettings>`; the `ResolveSettingsAndKey.Fill` step proven a no-op for `build-theme`'s arg surface (ADR Consequences — explicitly tested, not assumed; S14 AC-06) |
| TC-U-79 | 14 | `BuildTheme_ShouldSkipSettingsResolution_WhenExplicitVersionSupplied` | `--version` wins; resolver `Received(0)`; `--version`/`--environment-name` mutual exclusion unchanged (S14 AC-07) |
| TC-U-80 | 14 | `TryBuildTheme_ShouldUseResolverFactoryDirectly_WhenResolvedSettingsSupplied` | Command overload: `_resolverFactory.Create(settings)`; `ISettingsRepository` `Received(0)` |
| TC-U-81 | 14 | `TryBuildTheme_ShouldFallBackToLatestFallback_WhenResolvedSettingsNull` | Null-settings branch = the CLI's existing offline default, byte-for-byte |
| TC-U-82 | 14 | `TryBuildTheme_ShouldBehaveUnchanged_WhenCliOverloadsUsed` | Existing CLI-facing overloads/ctor untouched; **all existing `BuildThemeCommandTests` pass without modification** (S14 AC-05) |

### Story 16 — FR-06/OQ-04 classification-registry guard
Fixture: `clio.tests/Command/McpServer/PassthroughToolClassificationGuardTests.cs` (new). Module: `McpServer`.

| TC | Test | Asserts |
|----|------|---------|
| TC-U-83 | `ClassificationRegistry_ShouldExactlyMatchDiscoveredToolSet_WhenToolAssemblyScanned` | `McpFeatureToggleFilter.GetAttributedTypes(assembly, typeof(McpServerToolTypeAttribute))` result set **exactly equals** `Classification.Keys` — a new unclassified tool fails immediately (discovery-completeness; PRD AC-08 / SM-02) |
| TC-U-84 | `CoverageRegistry_ShouldNameExistingTestMethodPerToolPathScenario_WhenMappingVerified` | For every non-`NotEnvironmentSensitive` entry: a `Coverage` row exists per audited **dependency-path × scenario** (routed/guarded: `HeaderOnly` + `MixedInput` + `RegisteredEnvStdio`; fail-fast: guard-rejection + unchanged-behavior); reflection over `FixtureType.GetMethod(MethodName)` returns non-null `MethodInfo` carrying `[Test]`/`[TestCase]`. Path granularity: `update-page` has separate `write` / `version-probe` rows; `create-app-section` has `outer` / `caption-culture-readback` / `caption-culture-validation` / `app-info-validation` / `app-info-polling`; each `link-from-repository-*` branch is its own path |
| TC-U-85 | `CoverageRegistry_ShouldFail_WhenFixtureHasOnlyUnrelatedTests` | Anti-coarseness proof: a fixture containing an unrelated `[Test]` but no row naming the exact required method fails — the "any test in fixture X" design stays rejected (S16 AC-04) |
| TC-U-86 | `ClassificationRegistry_ShouldNotTripOnOutOfScopeTools_WhenNotApplicableRowsPresent` | Every PRD out-of-scope tool (incl. `list-creatio-builds`, the FR-06 legitimate-local-`ISettingsRepository` false-positive case) present with a single `NotApplicable` row and passes |
| TC-U-87 | `CoverageRegistry_ShouldFailWithClearMessage_WhenMappedMethodDoesNotExist` | A row naming a nonexistent/non-test method fails with an assertion message identifying tool/path/scenario — never a silent pass or a bare reflection exception (S16 AC-ERR) |

**Unit total: 87 test cases** (TC-U-01 … TC-U-87) across 14 new + 6 extended fixtures.

---

## Integration Tests (`clio.tests/`, `[Category("Integration")]`) — TC-I inventory

**None planned — deliberate.** Every story (1–16) declares "Integration: none required": all Creatio
access is mocked at the `IApplicationClient` / service-interface / resolver boundary in unit tests, and no
story introduces new file-system/DB/IIS I/O. The behavior that genuinely needs a live boundary (MCP
schema binding of the `[Required]` relaxations, real two-tenant auth) is *by design* not reachable at the
Integration tier — it lives in the E2E layer below. Adding synthetic Integration cases here would duplicate
unit coverage without adding signal. If implementation surfaces unexpected I/O (e.g. a new
`appsettings.json` interaction in the guard), add TC-I cases at that slice and update this plan.

---

## E2E Tests (`clio.mcp.e2e/`, `[Category("E2E")]`) — TC-E inventory (Story 15 owns ALL of these)

> **⚠️ CI status: MCP E2E is NOT in CI.** Every case below is compile-verified on each push (the suites
> self-skip via `Assert.Ignore` when `CLIO_MCP_HTTP_E2E_*` stand variables are absent) but **executes only
> in a manual run against a live stand**. The manual run is a hard gate in Story 15's DoD; per-case results
> go into its Dev Agent Record; the PR must carry the checklist line **"MCP e2e NOT in CI — manual run
> attached."** Story 16's registry consumes these exact method names — record the
> (tool, dependency-path, scenario) → method list on completion.

All cases extend the three existing ENG-93208 fixtures (never parallel fixtures), reusing
`McpHttpPassthroughStand` skip/config plumbing. Data-driven `[TestCaseSource]` is allowed where the
assertion shape is identical, but **each (tool, path, scenario) must surface as an individually named,
reportable case** so Story 16's reflection check can find it.

### Multi-tenant cases — extend `McpHttpMultiTenantE2ETests` (PRD FR-08 mandated set)

| TC | Tool / path | Input shape | Expected |
|----|-------------|------------|----------|
| TC-E-01 | `list-apps` (outer) | header-only | Returns the **header** tenant's applications; never `Environment name is required.` |
| TC-E-02 | `list-apps` | header + `environment-name` (different registered env) | Rejected before any named-env call — no confused-deputy (PRD AC-06) |
| TC-E-03 | `create-app-section` — the "one section tool" case; assertion must reach the **nested caption-culture path** | header-only | Section created on the header tenant; readback culture = **header** tenant's culture (proves the nested leak closed live) |
| TC-E-04 | `create-app-section` | header + `environment-name` | Rejected before any named-env call (outer and nested) |
| TC-E-05 | `get-user-culture` | header-only (active env configured on the stand) | Header tenant's culture; the configured active environment is **never** read (Security mode ii closed live) |
| TC-E-06 | `get-user-culture` | header + `environment-name` | Rejected before any Creatio call |
| TC-E-07 | `link-from-repository-unlocked` (always Creatio-reaching branch) | header-only | The **uniform** "not supported under credential passthrough" rejection naming tool + alternative — typed envelope / `IsError=true`, no secrets |
| TC-E-08 | `link-from-repository-unlocked` | header + `environment-name` | Same uniform rejection; named env's stored creds never used |

### Two-tenant concurrency isolation — extend `McpHttpConcurrencyIsolationE2ETests` (PRD AC-07)

Each of the **12 newly routed tools** gets the same two-concurrent-different-credential proof already
established for `describe-environment` (`McpHttpMultiTenantE2ETests.cs:33`): distinct authenticated
containers per request, correct per-tenant results, no cross-tenant session/log bleed.

| TC | Tool (routed path under test) |
|----|-------------------------------|
| TC-E-09 | `list-apps` |
| TC-E-10 | `get-app-info` |
| TC-E-11 | `create-app` (incl. nested culture/polling) |
| TC-E-12 | `create-app-section` (incl. nested sites) |
| TC-E-13 | `update-app-section` |
| TC-E-14 | `delete-app-section` |
| TC-E-15 | `list-app-sections` |
| TC-E-16 | `get-user-culture` |
| TC-E-17 | `update-page` (version-probe path) |
| TC-E-18 | `sync-pages` (version-probe path) |
| TC-E-19 | `get-component-info` (mixed-input path) |
| TC-E-20 | `build-theme` (version path) |

### No-regression sweep — extend `McpHttpNoRegressionE2ETests` (PRD AC-09 / SM-03)

Each of the **15 touched tools** (12 routed above + `link-from-repository-by-environment`,
`link-from-repository-by-env-package-path`, `link-from-repository-unlocked`) is exercised over **both**
`clio mcp` (stdio) **and** `clio mcp-http -e <env>` with `environment-name` supplied, asserting behavior
matches the pre-change baseline. This is also the only layer that proves the `[Required]` relaxations
(stories 1, 3–9, 12) did not break existing registered-env callers at the real MCP binding layer.

| TC | Tool | TC | Tool | TC | Tool |
|----|------|----|------|----|------|
| TC-E-21 | `list-apps` | TC-E-26 | `delete-app-section` | TC-E-31 | `get-component-info` |
| TC-E-22 | `get-app-info` | TC-E-27 | `list-app-sections` | TC-E-32 | `build-theme` |
| TC-E-23 | `create-app` | TC-E-28 | `get-user-culture` | TC-E-33 | `link-from-repository-by-environment` |
| TC-E-24 | `create-app-section` | TC-E-29 | `update-page` | TC-E-34 | `link-from-repository-by-env-package-path` |
| TC-E-25 | `update-app-section` | TC-E-30 | `sync-pages` | TC-E-35 | `link-from-repository-unlocked` |

### E2E harness invariants (asserted across all fixtures, not separate TCs)

- Suites **compile in CI on every push** and self-skip cleanly without stand config (S15 AC-05); the only
  category used is `E2E`.
- Failure output **never echoes** `accessToken`/`login`/`password` — assertions match redacted text only
  (S15 AC-ERR).

**E2E total: 35 named cases** (8 multi-tenant + 12 isolation + 15 no-regression) + 2 harness invariants.
Naming examples (consumed verbatim by Story 16): `ApplicationGetList_ShouldExecuteAgainstHeaderTenant_WhenHeaderOnly`,
`ApplicationSectionCreate_ShouldResolveHeaderTenantCulture_WhenHeaderOnly`,
`LinkFromRepositoryUnlocked_ShouldReturnUniformRejection_WhenPassthroughActive`.

---

## Regression Guard

Tests/suites that MUST pass after this feature ships (beyond the new TCs above):

| Test file | What must stay green | Why at risk |
|-----------|----------------------|-------------|
| `clio.tests/Command/McpServer/ApplicationToolTests.cs` | Entire fixture (95 KB) — only construction-site updates allowed, no assertion changes | All 7 c1 tools rewired to `BaseTool` ctors + `ExecuteWithCleanLog(options,…)`; args records lose `[Required]` |
| `clio.tests/Command/McpServer/ComponentInfoToolTests.cs` | The `:606` header-only `latest-fallback` compliance test **unchanged** | Story 13's mixed-input swap sits one branch away from the compliant path |
| `clio.tests/Command/McpServer/BuildThemeToolTests.cs` | The `:49` direct-construction test **unchanged** (optional resolver param) | Story 14 adds a ctor parameter |
| `clio.tests/Command/BuildThemeCommandTests.cs` | Entire fixture **unmodified** | Story 14 AC-05: CLI overloads byte-for-byte unchanged |
| `clio.tests/Command/McpServer/PageSyncToolTests.cs`, `PageSyncToolBaselineTests.cs` | All existing cases | Blank-name short-circuit removal changes probe control flow; any existing test relying on the `null` early return must be re-examined, not silently rewritten |
| `clio.tests/Command/McpServer/PageUpdateToolBaselineTests.cs`, `PageUpdateToolRunProcessTests.cs` | All existing cases | Story 11 swaps the probe's settings source |
| `clio.tests/Command/McpServer/GetUserCultureToolTests.cs` | All existing cases (ctor-wiring updates only) | Story 10 adds `IToolCommandResolver` dependency |
| `clio.tests/Command/McpServer/LinkFromRepositoryToolTests.cs` | All existing cases | Story 1 inserts guard-first + explicit requiredness into all three methods |
| `clio.tests/Command/Link4RepoCommand.{Tests,PreparationTests,UnlockedTests}.cs` | All — the **command** must be untouched by Story 1 | Fail-fast is tool-level only; command/validator behavior must not change |
| `clio.tests/Command/ApplicationInfoServiceTests.cs`, `ApplicationCreateServiceTests.cs`, `ApplicationSectionCreateServiceTests.cs`, `ApplicationSectionUpdateServiceTests.cs`, `ApplicationSectionDeleteServiceTests.cs` | All existing name-based-overload cases without modification (Story 2/3–9 AC "observable parity") | New settings-based overloads added beside unchanged name-based bodies |
| `clio.tests/Command/CaptionCultureResolverTests.cs`, `EntitySchemaCaptionCultureResolverTests.cs`, `CaptionCultureScriptGuardTests.cs` | All existing cases | Story 2's settings-based overload lands in the same class |
| `clio.tests/Command/McpServer/ApplicationCreateEnrichmentServiceTests.cs` | All — enrichment path is class-(b) compliant and must not be touched (Story 5 AC-05) | Adjacent to Story 5's changes |
| `clio.tests/Command/McpServer/ToolCommandResolver{,CacheKey,NoWrite}Tests.cs` | All — the resolver seam itself is a no-change dependency (PRD A-01) | Every routed tool now leans on it harder |
| `clio.tests/Command/McpServer/CredentialPassthrough{ClientIdentity,DiRegistration,Di,Middleware,SecretHygiene}Tests.cs` | All — ENG-93208 ground truth (middleware semantics are out of scope and must not drift) | Same subsystem; Story 1/10 touch `BindingsModule.cs` |
| `clio.tests/Command/McpServer/PlatformVersionResolverTests.cs` | All existing cases | `build-theme`/probe fixes route new callers through `PlatformVersionResolverFactory` |
| `clio.mcp.e2e/McpHttpMultiTenantE2ETests.cs` (`describe-environment` case at `:33`) | The existing two-tenant proof stays green on the manual run | Fixtures extended in place by Story 15 |
| Full unit suite (`Category=Unit`) | Green for Stories 1 and 10 specifically | Both touch `clio/BindingsModule.cs` (repo full-suite trigger rule 4) |

**Regression guard: 17 file groups / ~25 files.**

---

## Traceability — stories → test cases → FR/AC

| Story | Unit TCs | E2E TCs (Story 15) | FR / PRD AC |
|-------|----------|--------------------|-------------|
| 1 (c3 guard) | TC-U-01…11 | TC-E-07/08, TC-E-33…35 | FR-04, FR-05a, FR-07 / AC-06, AC-ERR |
| 2 (shared deps) | TC-U-12…18 | indirect via consumers | FR-01 (enabling), FR-05 |
| 3 (`list-apps`) | TC-U-19…24 | TC-E-01/02, TC-E-09, TC-E-21 | FR-01, FR-05, FR-05a / AC-01, AC-06, AC-07, AC-09, AC-ERR |
| 4 (`get-app-info`) | TC-U-25…28 | TC-E-10, TC-E-22 | FR-01, FR-05, FR-05a / AC-02, AC-06, AC-09 |
| 5 (`create-app`) | TC-U-29…35 | TC-E-11, TC-E-23 | FR-01, FR-05, FR-05a / AC-02, AC-06, AC-07, AC-09 |
| 6 (`create-app-section`) | TC-U-36…43 | TC-E-03/04, TC-E-12, TC-E-24 | FR-01, FR-05, FR-05a / AC-02, AC-06, AC-07, AC-09 |
| 7 (`update-app-section`) | TC-U-44…49 | TC-E-13, TC-E-25 | FR-01, FR-05, FR-05a / AC-02, AC-06, AC-09 |
| 8 (`delete-app-section`) | TC-U-50…54 | TC-E-14, TC-E-26 | FR-01, FR-05, FR-05a / AC-02, AC-06, AC-09 |
| 9 (`list-app-sections`) | TC-U-55…59 | TC-E-15, TC-E-27 | FR-01, FR-05, FR-05a / AC-02, AC-06, AC-09 |
| 10 (`get-user-culture`) | TC-U-60…63 | TC-E-05/06, TC-E-16, TC-E-28 | FR-02 / AC-03, AC-06, AC-09 |
| 11 (`update-page`) | TC-U-64…66 | TC-E-17, TC-E-29 | FR-03 / AC-04, AC-06, AC-09 |
| 12 (`sync-pages`) | TC-U-67…71 | TC-E-18, TC-E-30 | FR-03, FR-05a / AC-04, AC-05, AC-06, AC-09 |
| 13 (`get-component-info`) | TC-U-72…74 | TC-E-19, TC-E-31 | FR-03 / AC-04, AC-06, AC-09 |
| 14 (`build-theme`) | TC-U-75…82 | TC-E-20, TC-E-32 | FR-03 / AC-04, AC-06, AC-09 |
| 15 (consolidated E2E) | — | **TC-E-01…35 (owner)** | FR-08 / AC-07, AC-09 |
| 16 (registry guard) | TC-U-83…87 | consumes E2E method names | FR-06, FR-07 / AC-08 |
| 17 (docs) | none (doc-baseline export test if present) | — | FR-09 / AC-10 |

PRD AC coverage check: AC-01 (TC-U-19/TC-E-01) · AC-02 (TC-U-25/29/36/44/50/55) · AC-03 (TC-U-60–63,
TC-E-05) · AC-04 (TC-U-64–82, TC-E-17–20) · AC-05 (TC-U-67–71) · AC-06 (every `MixedInput` TC + TC-E-02/04/
06/08) · AC-07 (TC-E-09–20) · AC-08 (TC-U-83–87) · AC-09 (every `RegisteredEnvStdio` TC + TC-E-21–35) ·
AC-10 (Story 17 checklist) · AC-ERR (TC-U-10/24 + envelope assertions throughout). **No PRD AC is
uncovered.**

---

## Execution strategy — what runs where

| Layer | Runs in CI? | When / how |
|-------|-------------|-----------|
| Unit (TC-U-01…87) | **Yes** — every push | Per-slice before commit (FR-10, every code story): `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=McpServer" --no-build`; widen to `"Category=Unit&(Module=McpServer|Module=Command)"` for stories 2–9, 14; **full** `Category=Unit` suite for stories 1 and 10 (`BindingsModule.cs` trigger). Include the exact filter command in the commit/PR description |
| CLIO analyzers (FR-10) | **Yes** — build errors | Clean `CLIO*` incl. CLIO005 in every changed file, verified per slice (local clean build before push) |
| Integration | n/a | No cases planned (see TC-I rationale) |
| E2E compile + self-skip | **Yes** (compile only) | `clio.mcp.e2e` builds on every push; suites `Assert.Ignore` without `CLIO_MCP_HTTP_E2E_*` |
| E2E execution (TC-E-01…35) | **NO — manual live-stand only** | Story 15 pre-merge gate: manual run against a two-tenant stand; per-case results recorded in the Dev Agent Record; the (tool, dependency-path, scenario) → method list handed to Story 16; PR flags "MCP e2e NOT in CI — manual run attached". Post-merge, the registry guard (TC-U-83/84) keeps the mapping honest in CI even though the E2E bodies stay manual |
| Story 16 guard | **Yes** — every push touching `clio/Command/McpServer/**` | Runs as ordinary unit tests; permanent drift protection for FR-06 |

Order of authoring follows the sprint tracker: Story 1 ∥ Story 2 → 3→4→5→6→7→8→9 (serialized,
shared `ApplicationTool.cs`) ∥ 10 ∥ 11–14 (parallel) → 15 (E2E) → 16 (registry) → 17 (docs).

---

## Coverage Estimate

| Layer | New tests | Modified/extended fixtures | Notes |
|-------|-----------|---------------------------|-------|
| Unit | 87 | 6 extended (`CaptionCultureResolverTests`, `ApplicationInfoServiceTests`, `PageSyncToolTests`, `ComponentInfoToolTests`, `BuildThemeToolTests`, `BuildThemeCommandTests`) + 14 new fixtures | Every broken tool and every audited dependency-path has named per-scenario cases (FR-08); Story 16's guard makes the mapping self-enforcing |
| Integration | 0 | 0 | Deliberate — no new I/O surface (rationale above) |
| E2E | 35 | 3 fixtures extended in place | **Manual live-stand only — NOT in CI**; compile + self-skip verified in CI |
| Regression guard | — | ~25 existing files must stay green | Incl. full-unit-suite runs for stories 1 & 10 |

---

## Definition of Done for QA

- [ ] All TC-U-01…87 implemented with `[Category("Unit")]` — **NOT** `[Category("UnitTests")]` — and the
      correct `[Property("Module", …)]` trait
- [ ] Every test follows `MethodName_ShouldExpectedBehavior_WhenCondition`, AAA structure, `because:` on
      every assertion, `[Description]` on every test method; cross-OS runnable
- [ ] No TC-I cases required; if implementation introduces new I/O, this plan is amended at that slice
- [ ] All TC-E-01…35 implemented in `clio.mcp.e2e` with `[Category("E2E")]` only; suites compile in CI and
      self-skip without stand config
- [ ] **Manual live-stand E2E run executed and recorded** (Story 15 Dev Agent Record: per-case pass/fail +
      the (tool, dependency-path, scenario) → method list); PR carries "MCP e2e NOT in CI — manual run
      attached"
- [ ] Story 16 registry rows exist for every (tool, dependency-path, scenario) tuple in this plan and
      TC-U-83…87 are green — a missing test cannot silently pass
- [ ] All regression-guard suites green; existing `ComponentInfoToolTests.cs:606`,
      `BuildThemeToolTests.cs:49`, all `BuildThemeCommandTests`, and all name-based-overload service tests
      pass **without modification**
- [ ] Per-slice FR-10 validation performed and cited in each commit/PR: `CLIO*` diagnostics clean (incl.
      CLIO005), targeted `dotnet test --filter "Category=Unit&Module=McpServer"` (widened/full-suite per
      story DoD)
- [ ] Nested-path tests (TC-U-31/32, 38–41, 46/47, 52, 57) are separate, named tests — outer-call coverage
      alone does not close any c1 story
- [ ] Mixed-input (confused-deputy) coverage exists for **every** routed/guarded path (PRD AC-06) — unit +
      the four mandated E2E mixed cases
- [ ] No test asserts on or echoes `accessToken`/`login`/`password`; error assertions use redacted text
- [ ] File-location notes resolved (create `PageUpdateToolPassthroughTests.cs`; extend
      `clio.tests/Command/CaptionCultureResolverTests.cs` in place) and Story 16 registry points at the
      actual files
