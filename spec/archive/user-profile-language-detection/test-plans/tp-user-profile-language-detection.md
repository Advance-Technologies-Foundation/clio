# Test Plan: User Profile Language Detection for Entity Creation

**Feature**: user-profile-language-detection
**Jira**: ENG-91044
**Stories**: [1](../stories/story-user-profile-language-detection-1.md), [2](../stories/story-user-profile-language-detection-2.md), [3](../stories/story-user-profile-language-detection-3.md), [4a](../stories/story-user-profile-language-detection-4a.md), [4b](../stories/story-user-profile-language-detection-4b.md), [5](../stories/story-user-profile-language-detection-5.md), [6](../stories/story-user-profile-language-detection-6.md), [7](../stories/story-user-profile-language-detection-7.md)
**PRD**: [prd-user-profile-language-detection.md](../prd/prd-user-profile-language-detection.md)
**ADR**: [adr-user-profile-language-detection.md](../adr/adr-user-profile-language-detection.md) (Revision 3 — authoritative)
**Author**: QA Planner Agent
**Status**: Draft
**Created**: 2026-06-09

---

## Scope

### In scope
- Server-side profile-culture resolution from `GetApplicationInfo` → `sysValues.userCulture.displayValue`, including B-1 validation (malformed / empty / missing / `userCulture`-absent-but-`primaryCulture`-present → `Failed`, no silent substitution).
- Singleton, env-URI-keyed culture cache (`ICurrentUserCultureCache`) — at-most-once resolution per environment per TTL (sequential), `FakeTimeProvider` TTL expiry (Mi-6).
- Async resolver contract `Task<CultureResolution> ResolveAsync(CancellationToken)` (M-6); sync bridge only at the CLI verb.
- Effective-culture precedence (`--caption-culture` > resolved profile > `en-US`) in every **In** creator (4a entity-schema, 4b column WRITE, 5 page/client-unit/resource/schema-helper, 6 section/app); never `CultureInfo.CurrentCulture` in creation paths (M-1).
- `NormalizeTitleLocalizations` signature change and all 8 call sites incl. `UpdateEntitySchemaCommand.cs:171` and `EntitySchemaTool.cs:78,462` (M-2 / NEW-1).
- M-4 gating: `--caption-culture` skips resolution; supplied-map-has-key softens a `Failed` resolution to non-fatal `en-US`; hard-abort only when neither.
- `get-user-culture` CLI verb (+ `profile-language` alias) and read-only MCP tool (structured `{culture, resolvedFrom, success, reason}` signal).
- MCP guidance (instructions + `app-modeling` resource + 4 prompt families): detect-once / reuse / ask-on-failure (FR-07/08/09, AC-07).
- AC-06 parity/regression for each In creator (4a, 5, 6 per NEW-4) and the `en-US` byte-identical contract.
- AC-08 grep with the pinned allow-list.
- `en-US` retained as mandatory map entry and fallback (FR-05/AC-03); `NormalizeLocalizationMap` unchanged.

### Out of scope (with reason)
- **Column READ/display paths** `RemoteEntitySchemaColumnManager.GetColumnProperties` (L114) / `GetSchemaProperties` (L176): stay on host locale (Mi-3). Tested only to assert they are NOT changed.
- **`ApplicationInfoService.GetPreferredCultureNames` (L496)** `GetCurrentCultureName()`: READ probe order, stays host locale (NEW-2). Pinned out of AC-08.
- **`add-item` / `create-user-task`**: caller-controlled `--culture`, not host-locale-derived (Decision 4). No behavior change, no new tests.
- **process-model / data-binding `en-US` literals**: not entity captions (Decision 4, PRD Non-goal). No change.
- **Same-URI-different-user cache collision** (NEW-5): explicitly out of scope; no test asserts per-user keying.
- **Strict single-probe under concurrency** (NEW-3): explicitly NOT asserted; only sequential at-most-once is asserted.
- Translation / auto-localization of caption text (PRD Non-goal); changing the Creatio profile language (read-only).

---

## Risk Assessment

| # | Risk | Likelihood | Impact | Mitigation (test) |
|---|------|-----------|--------|-------------------|
| R-1 | Malformed/empty/missing `displayValue` written as a caption key (B-1) | Med | High | TC-U-03..05 force `Failed("userCulture-invalid")` / `Failed("userCulture-missing")`; never throws into creation path |
| R-2 | `userCulture` absent → silent substitution of `primaryCulture` (system culture) | Med | High | TC-U-06 asserts `Failed("userCulture-missing")`, NSubstitute response has `primaryCulture` present and `userCulture` absent |
| R-3 | Creation path reads `CultureInfo.CurrentCulture` (M-1) → CI/host-locale-dependent captions | Med | High | TC-U-12/15/19/24/28 assert effective culture never sourced from `CurrentCulture`; AC-08 grep TC-I-01 |
| R-4 | `NormalizeTitleLocalizations` signature change misses a call site (M-2/NEW-1) → silent `en-US` on `UpdateEntitySchemaCommand`/`EntitySchemaTool` | High | High | TC-U-13 (helper arg), TC-U-16 (`UpdateEntitySchemaCommand`), TC-U-17 (`EntitySchemaTool` both calls); TC-I-02 grep all 8 callers |
| R-5 | `GetApplicationInfo` failure aborts previously-working scripted/CI flows (M-4 regression) | Med | High | TC-U-20/21 skip-on-override + non-fatal-on-usable-map; TC-U-08 (verb hard-abort only when no override) |
| R-6 | Singleton cache cold per `Create()` (precedent flaw) → AC-05 unmet | Med | Med | TC-U-09 two `Create()` calls share singleton store → at-most-once probe (sequential) |
| R-7 | Over-asserting single probe under concurrency makes tests flaky (NEW-3) | Low | Med | No concurrency single-probe assertion; documented in Out of scope |
| R-8 | Regression in existing entity/page/section creation when profile already `en-US` (AC-06) | Med | High | TC-U-22 (4a), TC-U-26 (5), TC-U-30 (6) byte-identical parity snapshots; regression-guard suite |
| R-9 | AC-08 grep allow-list drift (READ paths flagged or new violation added) | Med | Med | TC-I-01 grep In files; TC-I-03 asserts allow-list lines retained |
| R-10 | New raw `HttpClient` introduced (SM-01 counter) | Low | High | TC-U-02 asserts resolution goes through `IApplicationClient`; TC-I-04 grep no new `HttpClient` |
| R-11 | MCP tool silently falls back to host locale instead of `success:false` (FR-06/AC-04) | Med | High | TC-U-32..35 four reason classes; TC-U-36 branch-on-`Success`-first (NEW-6) |
| R-12 | MCP guidance incomplete (< 4/4 prompt families) (SM-03/AC-07) | Med | Med | TC-U-37..42 per family; TC-E2E-02 manual e2e |
| R-13 | `Cultures` schema-array (Mi-2) confused with caption key | Low | Med | TC-U-14 dedicated assertion separate from caption tests |
| R-14 | MCP E2E not in CI → guidance/tool regressions ship undetected | High | Med | TC-E2E-01/02 documented manual; PR checklist gate (see Regression Guard) |

---

## Test Data

| Name | Definition | Used by |
|------|-----------|---------|
| `PROFILE_UK` | `GetApplicationInfo` response with `sysValues.userCulture.displayValue = "uk-UA"`, `value = "<guid>"` | TC-U-01, AC-01/AC-02 happy paths |
| `PROFILE_EN` | `userCulture.displayValue = "en-US"` | TC-U-22/26/30 parity (AC-06) |
| `MALFORMED_DV` | `userCulture.displayValue = "xx-INVALID-zz"` (unparseable by `CultureInfo.GetCultureInfo`) | TC-U-03 |
| `EMPTY_DV` | `userCulture.displayValue = ""` / whitespace | TC-U-04 |
| `MISSING_DV` | `userCulture` node present, `displayValue` field absent/null | TC-U-05 |
| `USERCULTURE_ABSENT` | `sysValues.userCulture` absent, `sysValues.primaryCulture.displayValue = "en-US"` present | TC-U-06 (Mi-1) |
| `UNREACHABLE` | `IApplicationClient` throws / returns error (connection) | TC-U-07 |
| `UNAUTHORIZED` | `GetApplicationInfo` returns 401/unauthorized | TC-U-07 |
| `MAP_UK_PRESENT` | localization map already containing `uk-UA` key | OQ-04 effective-key path |
| `MAP_UK_ABSENT` | localization map with only `en-US` key | OQ-04 fall-back-to-`en-US` path, M-4 non-fatal |
| `LANGUAGE_LABEL_TRAP` | response where `primaryLanguage.displayValue = "English (United States)"` (human label, must NOT be read) | negative assertion in TC-U-01 |

---

## Unit Tests (`clio.tests/`)

### Story 1 — Resolver + Cache (`clio.tests/Command/EntitySchemaDesigner/CurrentUserCultureResolverTests.cs`)

| TC | Name | AC | Notes |
|----|------|----|-------|
| TC-U-01 | `ResolveAsync_ShouldReturnResolvedUkUa_WhenUserCultureDisplayValueIsUkUa` | AC-01 | reads `userCulture.displayValue`, NOT `primaryLanguage`/`primaryCulture`; returns normalized `CultureInfo.Name` |
| TC-U-02 | `ResolveAsync_ShouldUseApplicationClientOnly_WhenProbingGetApplicationInfo` | SM-01 | asserts `IApplicationClient.ExecutePostRequest` received; no cliogate, no raw `HttpClient` |
| TC-U-03 | `ResolveAsync_ShouldReturnFailedUserCultureInvalid_WhenDisplayValueIsMalformed` | AC-B1-INVALID | `MALFORMED_DV`; never throws |
| TC-U-04 | `ResolveAsync_ShouldReturnFailedUserCultureMissing_WhenDisplayValueIsEmpty` | AC-B1-MISSING | `EMPTY_DV` |
| TC-U-05 | `ResolveAsync_ShouldReturnFailedUserCultureMissing_WhenDisplayValueFieldAbsent` | AC-B1-MISSING | `MISSING_DV` |
| TC-U-06 | `ResolveAsync_ShouldReturnFailedAndNotSubstitutePrimaryCulture_WhenUserCultureAbsent` | AC-Mi1 | `USERCULTURE_ABSENT`; asserts result is `Failed("userCulture-missing")` AND culture != system `primaryCulture` |
| TC-U-07 | `ResolveAsync_ShouldReturnFailed_WhenEndpointUnreachableOrUnauthorized` | AC-ERR | `UNREACHABLE`/`UNAUTHORIZED`; reason `unreachable`/`unauthorized`; never throws |
| TC-U-09 | `ResolveAsync_ShouldProbeAtMostOnce_WhenCalledTwiceWithinTtlAcrossTwoCreates` | AC-05 | two `factory.Create(settings)` share singleton cache; `ExecutePostRequest` received once (sequential) |
| TC-U-10 | `ResolveAsync_ShouldReprobe_WhenCacheEntryExpiredViaFakeTimeProvider` | AC-05/Mi-6 | `FakeTimeProvider` advances past TTL; second probe occurs |
| TC-U-11 | `ResolveAsync_ShouldReturnNormalizedName_WhenDisplayValueHasUnusualCasing` | AC-01 | e.g. `"uk-ua"` → `"uk-UA"` |

> Sample (TC-U-06, the highest-risk B-1/Mi-1 case):
> ```csharp
> [Test, Category("Unit")]
> [Description("ResolveAsync returns Failed and does not substitute primaryCulture when userCulture is absent (Mi-1).")]
> public async Task ResolveAsync_ShouldReturnFailedAndNotSubstitutePrimaryCulture_WhenUserCultureAbsent()
> {
>     // Arrange
>     var client = Substitute.For<IApplicationClient>();
>     client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>())
>           .Returns(USERCULTURE_ABSENT); // userCulture missing, primaryCulture.displayValue = "en-US"
>     var resolver = _factory.Create(_settings); // factory wired with the substitute client + singleton cache
>
>     // Act
>     CultureResolution result = await resolver.ResolveAsync(CancellationToken.None);
>
>     // Assert
>     result.Success.Should().BeFalse(because: "absent userCulture must not silently fall back to system culture (Mi-1/FR-06)");
>     result.FailureReason.Should().Be("userCulture-missing", because: "the missing logged-in user culture is the documented reason code");
> }
> ```

### Story 2 — CLI verb (`clio.tests/Command/GetUserCultureCommandTests.cs`, `BaseCommandTests<GetUserCultureCommandOptions>`)

| TC | Name | AC | Notes |
|----|------|----|-------|
| TC-U-08a | `Execute_ShouldPrintResolvedCulture_WhenResolutionSucceeds` | AC-01 (signal) | resolver substitute returns `Resolved("uk-UA")`; exits zero |
| TC-U-08 | `Execute_ShouldPrintErrorAndReturnNonZero_WhenResolutionFails` | AC-ERR | `Failed`; prints `Error: {message}` no stack trace; non-zero (verb has no override → always hard-abort, M-4) |
| TC-U-08b | `Execute_ShouldBehaveIdentically_WhenInvokedViaProfileLanguageAlias` | AC-ALIAS | alias `profile-language` maps to same command |

### Story 4a — Entity-schema creator (`clio.tests/Command/RemoteEntitySchemaCreatorTests.cs`) + helper (`EntitySchemaDesignerSupportTests.cs`)

| TC | Name | AC | Notes |
|----|------|----|-------|
| TC-U-12 | `Create_ShouldUseEffectiveCulture_WhenProfileResolved` | AC-02 | `PROFILE_UK` → caption culture `uk-UA`; never `CurrentCulture` (M-1) |
| TC-U-13 | `NormalizeTitleLocalizations_ShouldUseEffectiveCultureName_WhenSupplied` (+ `..._ShouldFallBackToEnUs_WhenEffectiveCultureNameIsNull`) | AC-M2 | null → `en-US`, never `CurrentCulture` (EntitySchemaDesignerSupportTests) |
| TC-U-14 | `Create_ShouldSetCulturesArrayToEffectiveCulture_WhenSchemaCreated` | AC-Mi2 | schema-level `Cultures` array (L536) = effective culture; separate from caption assertions |
| TC-U-20 | `Create_ShouldSkipResolution_WhenCaptionCultureSupplied` | AC-M4-SKIP | `--caption-culture` → resolver factory never invoked / `GetApplicationInfo` not probed |
| TC-U-21 | `Create_ShouldProceedNonFatally_WhenResolutionFailsButMapHasKey` | AC-M4-NONFATAL | `Failed` + `MAP_UK_ABSENT`(en-US present) → degrades to `en-US`, no abort |
| TC-U-22 | `Create_ShouldProduceIdenticalPayload_WhenProfileCultureIsEnUs` | AC-06 | `PROFILE_EN` snapshot byte-identical to pre-change caption/description/title-localizations |
| TC-U-23 | `Create_ShouldRetainEnUsInLocalizationMaps_WhenProfileIsUkUa` | AC-03/FR-05 | `en-US` present in title- and description-localizations |
| TC-U-16 | `Execute_ShouldPassEffectiveCultureToNormalizeTitleLocalizations_WhenColumnCaptionUpdated` | NEW-1 | `UpdateEntitySchemaCommandTests` (BaseCommandTests) — L171 caller |
| TC-U-17 | `Handle_ShouldPassEffectiveCultureInBothNormalizeCalls_WhenSchemaCreatedOrUpdated` | NEW-1 | `EntitySchemaToolTests` — L78 and L462 both pass effective culture (MCP policy) |

> Sample (TC-U-20, M-4 skip — guards against breaking scripted/CI flows):
> ```csharp
> [Test, Category("Unit")]
> [Description("Create skips the GetApplicationInfo round-trip entirely when --caption-culture is supplied (M-4).")]
> public void Create_ShouldSkipResolution_WhenCaptionCultureSupplied()
> {
>     // Arrange
>     var factory = Substitute.For<ICurrentUserCultureResolverFactory>();
>     var sut = ... ; // with --caption-culture = "fr-FR"
>
>     // Act
>     sut.Create(requestWithCaptionCultureFrFr);
>
>     // Assert
>     factory.DidNotReceive().Create(Arg.Any<EnvironmentSettings>())
>            .Should(); // resolver never built
>     // effective caption culture == "fr-FR"
> }
> ```

### Story 4b — Column manager (`clio.tests/Command/RemoteEntitySchemaColumnManagerTests.cs`)

| TC | Name | AC | Notes |
|----|------|----|-------|
| TC-U-15 | `SetLocalizableValue_ShouldUseEffectiveCulture_WhenProfileResolved` | AC-02 | WRITE path L318/L327 uses effective culture, never `CurrentCulture` |
| TC-U-18 | `GetColumnProperties_ShouldUseHostLocale_WhenReadingColumns` (+ `GetSchemaProperties_ShouldUseHostLocale_...`) | AC-Mi3-READ | L114/L176 explicitly still `GetCurrentCultureName()` |
| TC-U-19 | `SetLocalizableValue_ShouldRespectCaptionCulturePrecedenceAndM4Gating_WhenSupplied` | AC-M4 | override skip / map-has-key non-fatal |
| TC-U-15b | `NormalizeTitleLocalizations_ShouldReceiveEffectiveCulture_WhenColumnCaptionWritten` | NEW-1 | L241/L310 callers pass effective culture |

### Story 5 — Page / client-unit / resource / schema-helper

| TC | Name | AC | File |
|----|------|----|------|
| TC-U-24 | `Create_ShouldUseEffectiveCulture_WhenProfileResolved` | AC-02 | `PageCreateCommandTests` (BaseCommandTests) |
| TC-U-25 | `Create_ShouldSkipResolutionAndStayNonFatal_WhenCaptionCultureOrMapKeyPresent` | AC-M4-SKIP/NONFATAL | `PageCreateCommandTests` |
| TC-U-26 | `Create_ShouldProduceIdenticalPayload_WhenProfileCultureIsEnUs` | AC-06 (NEW-4) | parity snapshot for THIS path; `PageCreateCommandTests` |
| TC-U-27 | `Create_ShouldUseEffectiveCultureAndFallBackToEnUs_WhenClientUnitSchemaCreated` | AC-02/AC-08 | `ClientUnitSchemaCreateTests` — no hardcoded `en-US` literal except fallback |
| TC-U-28 | `CleanAndMerge_ShouldHonorCultureNameArgAndFallBackToEnUs_WhenCreatingLocalizableEntry` | AC-02 | `ResourceStringHelperTests` |
| TC-U-29 | `ApplySchemaMetadata_ShouldHonorCultureNameArgAndFallBackToEnUs_WhenApplyingMetadata` | AC-02 | `SchemaDesignerHelperTests` |

### Story 6 — Section / application

| TC | Name | AC | File |
|----|------|----|------|
| TC-U-30 | `Execute_ShouldProduceIdenticalCaption_WhenProfileCultureIsEnUs` | AC-06 (NEW-4) | `ApplicationSectionCreateCommandTests` parity snapshot |
| TC-U-31 | `Execute_ShouldUseEffectiveCulture_WhenProfileResolved` | AC-02 | never `CurrentCulture`; `ResolveLocalizedCaption` (L423) keeps `en-US` precedence but uses effective culture when in map |
| TC-U-31b | `Execute_ShouldSkipOrStayNonFatal_WhenCaptionCultureOrMapKeyPresent` | AC-M4 | M-4 gating |

### Story 3 — MCP tool (`clio.tests/Command/McpServer/GetUserCultureToolTests.cs`)

| TC | Name | AC | Notes |
|----|------|----|-------|
| TC-U-32 | `Handle_ShouldReturnSuccessSignal_WhenResolutionSucceeds` | AC-01 (signal) | `{ culture:"uk-UA", success:true }` |
| TC-U-33 | `Handle_ShouldReturnFailureSignalUnreachable_WhenEndpointUnreachable` | AC-04 | `{ success:false, reason:"unreachable" }` |
| TC-U-34 | `Handle_ShouldReturnFailureSignalUnauthorized_WhenUnauthorized` | AC-04 | `reason:"unauthorized"` |
| TC-U-35 | `Handle_ShouldReturnFailureSignalUserCultureInvalidAndMissing_WhenValidationFails` | AC-04 | `userCulture-invalid` and `userCulture-missing` reason classes |
| TC-U-36 | `Handle_ShouldBranchOnSuccessBeforeReadingCulture_WhenResolutionFailed` | NEW-6 | failure path never emits the fallback culture as if resolved; no silent host-locale/`en-US` |
| TC-U-36b | `Handle_ShouldServeFromCache_WhenCalledTwiceWithinTtl` | AC-CACHE | sequential at-most-once (no concurrency assertion) |

### Story 7 — MCP guidance (`clio.tests/Command/McpServer/*`)

| TC | Name | AC | File |
|----|------|----|------|
| TC-U-37 | `GetInstructions_ShouldContainDetectOnceReuseAskOnFailureGuidance_WhenServerInitializes` | FR-07/AC-07 | `McpServerInstructionsTests` |
| TC-U-38 | `GetResource_ShouldContainDetectOnceGuidance_WhenAppModelingRequested` | FR-08/AC-07 | `AppModelingGuidanceResourceTests` |
| TC-U-39 | `GetPrompt_ShouldContainAskOnFailureGuidance_WhenEntityPromptRequested` | FR-09/AC-07 | `EntitySchemaPromptTests` |
| TC-U-40 | `GetPrompt_ShouldContainAskOnFailureGuidance_WhenPagePromptRequested` | FR-09/AC-07 | `PagePromptTests` |
| TC-U-41 | `GetPrompt_ShouldContainAskOnFailureGuidance_WhenSectionPromptRequested` | FR-09/AC-07 | section prompt tests |
| TC-U-42 | `GetPrompt_ShouldContainAskOnFailureGuidance_WhenApplicationPromptRequested` | FR-09/AC-07 | `ApplicationPromptTests` |
| TC-U-43 | `Guidance_ShouldForbidSilentHostLocaleFallback_WhenCultureResolutionFails` | FR-06/AC-04 | asserts the "do NOT fall back to host locale / silent en-US" text exists |

---

## Integration Tests (`clio.tests/`, `[Category("Integration")]`)

These are grep/contract assertions over the source tree (file-system I/O → Integration tier, not Unit).

| TC | Name | AC | Steps / Expected |
|----|------|----|------------------|
| TC-I-01 | `Grep_ShouldFindNoCurrentCultureOrHardcodedEnUsCaption_WhenScanningInFiles` | AC-08 | **Setup**: solution checked out. **Steps**: grep `CultureInfo.CurrentCulture`, `GetCurrentCultureName`, and hardcoded `"en-US"` (excluding the `DefaultCultureName` constant declaration) in the In files: `RemoteEntitySchemaCreator.cs`, `EntitySchemaDesignerSupport.cs` (creation-path lines), `ClientUnitSchemaCreate.cs`, `PageCreateOptions.cs`, `ResourceStringHelper.cs`, `SchemaDesignerHelper.cs`, `ApplicationSectionCreateCommand.cs`, `UpdateEntitySchemaCommand.cs`, `EntitySchemaTool.cs`. **Expected**: zero matches. |
| TC-I-02 | `Grep_ShouldFindAllEightNormalizeTitleLocalizationsCallersPassCulture_WhenScanning` | M-2/NEW-1 | the 8 callers (`RemoteEntitySchemaCreator:109,231`, `RemoteEntitySchemaColumnManager:241,310`, `UpdateEntitySchemaCommand:171`, `EntitySchemaTool:78,462`) each pass an `effectiveCultureName` argument (no caller relies on the `= null` default) |
| TC-I-03 | `Grep_ShouldRetainAllowListedGetCurrentCultureName_WhenScanningReadPaths` | AC-08/NEW-2/Mi-3 | **Expected**: `GetCurrentCultureName` still present at `RemoteEntitySchemaColumnManager.cs:114,176`, `ApplicationInfoService.cs:496`, and the `EntitySchemaDesignerSupport.GetCurrentCultureName()` definition — and NOWHERE else in the In files |
| TC-I-04 | `Grep_ShouldFindNoNewHttpClientUsage_WhenScanningResolver` | SM-01 counter | resolver/factory use only `IApplicationClient`; zero `new HttpClient`/`HttpClient` field in the new files |
| TC-I-05 | `Docs_ShouldDocumentCaptionCultureAndGetUserCulture_WhenVerbsChanged` | FR-11/FR-12 | `help/en/get-user-culture.txt`, `docs/commands/get-user-culture.md`, `Commands.md`, the In-verb docs, and `docs/McpCapabilityMap.md` contain `--caption-culture` / `get-user-culture` entries |

---

## E2E Tests (`clio.mcp.e2e/`)

> ⚠️ **`clio.mcp.e2e` is NOT in CI — manual execution only** (per project-context.md). Every E2E case below must be run manually before merge and recorded in the PR checklist.

| TC | Name | AC | Tool / Expected |
|----|------|----|-----------------|
| TC-E2E-01 | `Tool_ShouldExposeGetUserCultureAndReturnSignal_WhenServerRunning` | AC-01/AC-04 | **Tool**: `get-user-culture`. Real `mcp-server` exposes it; returns `{culture, resolvedFrom, success, reason}`. File: `clio.mcp.e2e/GetUserCultureToolE2ETests.cs`. **⚠️ NOT in CI — manual.** |
| TC-E2E-02 | `Guidance_ShouldExposeDetectOnceAcrossAllFamilies_WhenServerRunning` | AC-07/SM-03 | real `mcp-server`: instructions + `app-modeling` resource + entity/page/section/application prompts (4/4) all expose detect-once / reuse / ask-on-failure. File: `clio.mcp.e2e/GetUserCultureToolE2ETests.cs` (+ existing `McpGuidanceResourceE2ETests.cs`). **⚠️ NOT in CI — manual.** |
| TC-E2E-03 | `EntitySchema_ShouldCreateWithEffectiveCulture_WhenProfileResolved` | AC-02 | existing entity-schema e2e extended; create/update via MCP applies effective culture. **⚠️ NOT in CI — manual.** |

---

## Coverage matrix — AC → tests → story

| AC (PRD) | Stories | Test cases |
|----------|---------|-----------|
| AC-01 (resolve `uk-UA` from `GetApplicationInfo`) | 1, 2, 3 | TC-U-01, TC-U-02, TC-U-08a, TC-U-32, TC-E2E-01 |
| AC-02 (creators use resolved culture) | 4a, 4b, 5, 6 | TC-U-12, TC-U-15, TC-U-24, TC-U-27, TC-U-28, TC-U-29, TC-U-31, TC-E2E-03 |
| AC-03 (`en-US` present in maps, fallback) | 4a, 5, 6 | TC-U-23, TC-U-27, TC-U-28, TC-U-29 |
| AC-04 (failure → ask user, no silent default) | 3, 7 | TC-U-33, TC-U-34, TC-U-35, TC-U-36, TC-U-43, TC-E2E-01/02 |
| AC-05 (resolve at most once per session) | 1, 3, 7 | TC-U-09, TC-U-10, TC-U-36b |
| AC-06 (regression-safe parity) | 4a, 5, 6 | TC-U-22, TC-U-26, TC-U-30 |
| AC-07 (4/4 guidance families) | 7 | TC-U-37..42, TC-E2E-02 |
| AC-08 (grep: no `CurrentCulture`/hardcoded `en-US`) | 4a, 4b, 5, 6 | TC-I-01, TC-I-02, TC-I-03 |
| AC-ERR (CLI `Error:` + non-zero) | 1, 2 | TC-U-07, TC-U-08 |
| B-1 / Mi-1 (validation, no substitution) | 1 | TC-U-03, TC-U-04, TC-U-05, TC-U-06 |
| M-1 (no `CurrentCulture` in creation) | 4a, 4b, 5, 6 | TC-U-12, TC-U-15, TC-U-24, TC-U-31, TC-I-01 |
| M-2 / NEW-1 (signature + 8 callers) | 4a, 4b | TC-U-13, TC-U-15b, TC-U-16, TC-U-17, TC-I-02 |
| M-4 (skip / non-fatal / hard-abort gating) | 2, 4a, 4b, 5, 6 | TC-U-08, TC-U-20, TC-U-21, TC-U-19, TC-U-25, TC-U-31b |
| M-5 (singleton env-URI cache) | 1, 3 | TC-U-09, TC-U-36b |
| M-6 (async `ResolveAsync`) | 1 | exercised by all Story-1 `await` TCs; sync bridge only in TC-U-08* |
| Mi-2 (`Cultures` array) | 4a | TC-U-14 |
| Mi-3 (READ paths host locale) | 4b | TC-U-18, TC-I-03 |
| Mi-6 (`FakeTimeProvider` TTL) | 1 | TC-U-10 |
| NEW-3 (no strict single-probe under concurrency) | 1, 3 | enforced by omission — see Out of scope |
| NEW-4 (AC-06 per In creator) | 4a, 5, 6 | TC-U-22, TC-U-26, TC-U-30 |
| NEW-6 (branch on `Success` first) | 3 | TC-U-36 |
| SM-01 (`IApplicationClient` only) | 1 | TC-U-02, TC-I-04 |
| SM-03 (4/4 families) | 7 | TC-U-37..42, TC-E2E-02 |
| FR-11/FR-12 (docs + capability map) | 2, 3, 4a, 5, 6 | TC-I-05 |

---

## Regression Guard

Existing entity-creation flows that share the touched handlers/helpers. Each MUST stay green; where the flow previously relied on `en-US`/host-`en-US`, the AC-06 parity tests lock byte-identical output.

| Test file | Why at risk | Guard |
|-----------|-------------|-------|
| `clio.tests/Command/RemoteEntitySchemaCreatorTests.cs` | `NormalizeTitleLocalizations` signature + L107/L536 change | TC-U-22 parity snapshot (`PROFILE_EN`) |
| `clio.tests/Command/EntitySchemaDesignerSupportTests.cs` (new/existing) | helper signature change at L173/L188 | TC-U-13 null → `en-US` |
| `clio.tests/Command/RemoteEntitySchemaColumnManagerTests.cs` | WRITE path change; READ paths must NOT change | TC-U-15 + TC-U-18 (READ host-locale assertion) |
| `clio.tests/Command/UpdateEntitySchemaCommandTests.cs` + `UpdateEntitySchemaCommand.BatchExecution.Tests.cs` | L171 caller now passes culture | TC-U-16; existing batch tests must stay green |
| `clio.tests/Command/McpServer/EntitySchemaToolTests.cs` + `CreateEntitySchemaToolTests.cs` + `UpdateEntitySchemaToolTests.cs` | L78/L462 callers; MCP mapping | TC-U-17; existing tool tests green |
| `clio.tests/Command/PageCreateCommandTests.cs` + `PageBundleBuilderTests.cs` | `PageCreateOptions` L213/L229 literal replacement | TC-U-26 parity |
| `clio.tests/Command/McpServer/PageToolsTests.cs` | page MCP surface | existing tests green |
| `clio.tests/Command/ClientUnitSchemaCreate*` (new) | L154/L160 literal replacement | TC-U-27 |
| `clio.tests/Command/ResourceStringHelperTests.cs` | L71 signature change | TC-U-28 |
| `clio.tests/Command/ApplicationSectionCreateServiceTests.cs` + `ApplicationSectionUpdateServiceTests.cs` + `UpdateAppSectionCommandTests.cs` | section caption build / `ResolveLocalizedCaption` L423 | TC-U-30 parity |
| `clio.tests/Command/ApplicationInfoServiceTests.cs` | L496 `GetCurrentCultureName` must NOT change (NEW-2) | TC-I-03 allow-list |
| `clio.tests/Command/McpServer/PlatformVersionResolverTests.cs` | resolver/cache pattern mirrors this; shared DI/`TimeProvider` registration | run after `BindingsModule` change |
| `clio.tests/Command/McpServer/ApplicationToolTests.cs` | section/app caption emission via tool | existing tests green |

**Full-suite trigger**: `BindingsModule.cs` changes (Story 1 DI registration) → per CLAUDE.md smart-regression rule 4, run the full `Category=Unit` suite once Story 1 lands, not just module-targeted filters.

---

## Coverage Estimate

| Layer | New tests | Modified existing | Notes |
|-------|-----------|-------------------|-------|
| Unit | ~45 (TC-U-01..43 + variants) | ~12 fixtures touched (regression-guard list) | `[Category("Unit")]` only |
| Integration | 5 (TC-I-01..05) | 0 | grep/doc contract assertions |
| E2E | 3 (TC-E2E-01..03) | extends existing entity-schema + guidance e2e | **Manual only — NOT in CI** |

---

## Definition of Done for QA

- [ ] All TC-U-* implemented with `[Category("Unit")]` — NEVER `[Category("UnitTests")]`
- [ ] All TC-I-* implemented with `[Category("Integration")]`
- [ ] All TC-E2E-* implemented with `[Category("E2E")]` in `clio.mcp.e2e` and run **manually** (added to PR checklist — not in CI)
- [ ] Command tests use `BaseCommandTests<TOptions>`; resolver factory substitute registered in `AdditionalRegistrations`; `ClearReceivedCalls` in teardown
- [ ] Every test: AAA structure + a `because` on every assertion + `[Description]` attribute (AGENTS.md test-style policy)
- [ ] Test naming follows `MethodName_ShouldExpectedBehavior_WhenCondition`
- [ ] NUnit 4 + FluentAssertions + NSubstitute; `FakeTimeProvider` for TTL (TC-U-10)
- [ ] B-1/Mi-1 negative cases (TC-U-03..06) assert `Failed` with the exact reason code and NO silent substitution
- [ ] AC-06 parity snapshots (TC-U-22/26/30) assert byte-identical `en-US` output for each In creator (NEW-4)
- [ ] AC-08 grep (TC-I-01..03) passes with the pinned allow-list only
- [ ] NEW-3 honored: no test asserts a strict single-probe count under concurrency
- [ ] Regression-guard fixtures stay green; full `Category=Unit` suite run after the `BindingsModule` change
- [ ] PR includes the test files in the changed-files list and references the relevant story file(s)

## PR checklist gate (MCP E2E — manual)

- [ ] TC-E2E-01 (`get-user-culture` tool) executed manually — result attached
- [ ] TC-E2E-02 (4/4 guidance families) executed manually — result attached
- [ ] TC-E2E-03 (entity-schema create with effective culture) executed manually — result attached
