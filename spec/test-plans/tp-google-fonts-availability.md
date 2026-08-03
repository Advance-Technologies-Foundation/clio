# Test plan: google-fonts-availability (ENG-93985)

**Story**: [story-google-fonts-availability-1.md](../stories/story-google-fonts-availability-1.md)
**ADR**: [adr-ENG-93985-google-fonts-availability.md](../adr/adr-ENG-93985-google-fonts-availability.md)

## Strategy

The probe is an advisory network step whose verdict changes the emitted CSS, so the plan pins three
things separately: the **verdict mapping** (what each HTTP outcome means), the **consequence** (import
emitted or suppressed, and which warning), and the **placement** (validated before probing, probed
outside the lock, probed once). The live endpoint is an undocumented contract, so it is re-verified by
an explicit canary rather than trusted implicitly in CI.

## Coverage by acceptance criterion

| AC | Verified by | Fixture |
| --- | --- | --- |
| AC-1 (404 → no import + warning) | `Execute_ShouldWarnAndSuppressImport_WhenFamilyNotInGoogleFonts`, `Execute_ShouldSuppressOnlyTheUnpublishedFamily_WhenFamiliesMix`, `BuildTheme_ShouldReturnSuppressionWarning_WhenFamilyNotInCatalog` | `BuildThemeCommandTests`, `BuildThemeToolTests` |
| AC-2 (unverifiable → import kept + warning) | `Execute_ShouldKeepImportAndWarn_WhenGoogleFontsUnreachable`, `BuildTheme_ShouldReturnUnverifiedWarning_WhenCatalogUnreachable`, `Execute_ShouldIsolateFaultedProbeToItsFamily_WhenSiblingVerdictIsDefinitive`, `Execute_ShouldIsolateFaultedHeadingProbe_WhenBodyVerdictIsPublished` | `BuildThemeCommandTests`, `BuildThemeToolTests` |
| AC-3 (JSON-gated 200; 3xx/5xx/204 inconclusive) | `LookupAsync_ShouldReportInCatalog_WhenMetadataFound`, `LookupAsync_ShouldReportUnverified_WhenSuccessIsNotJson`, `LookupAsync_ShouldReportUnverified_ForUnexpectedStatus` (500, 403, 302, 301, 204) | `GoogleFontsCatalogTests` |
| AC-4 (argument removed and rejected) | `BuildTheme_ShouldReturnFailure_WhenRemovedLocalFontFamiliesArgSupplied` (3 spellings), `BuildThemeArgs_ShouldBindKebabAndRouteCamelToExtensionData_WhenDeserializedFromRawJson` | `BuildThemeToolTests` |
| AC-5 (`OpenWorld = true`) | `BuildThemeTool_Should_DeclareBuildSafetyFlags_WhenInspectingMcpServerToolAttribute` | `BuildThemeToolTests` |
| AC-6 (name contract, no request for invalid input) | `ValidateFamily_ShouldRejectOversizedFamily`, `LookupAsync_ShouldReportUnverifiedWithoutRequest_ForInvalidFamily` (grammar + over-length), `Execute_ShouldFailWithoutProbing_WhenFamilyIsInvalid`, `BuildTheme_ShouldReturnFailure_WhenFontFamilyIsMalformed` | `FontImportBuilderTests`, `GoogleFontsCatalogTests`, `BuildThemeCommandTests`, `BuildThemeToolTests` |
| AC-7 — placement | **`BuildTheme_ShouldProbe_BeforeTakingTheSharedExecutionLock`** — the lock-placement canary: it holds `McpToolExecutionLock.GetLock(SharedFallbackKey)` on the test thread and asserts the probe still fires. Every other AC-7 test asserts count or precedence and would stay green if the probe call moved back inside `ExecuteWithCleanLog`, so this one is the only guard against that regression — do not delete it in a refactor. | `BuildThemeToolTests` |
| AC-7 — count (verdicts threaded in, no re-probe inside the lock) | `BuildTheme_ShouldProbeEachFamilyOnce_WhenBuilding`, `BuildTheme_ShouldProbeOnce_WhenWritingToWorkspacePackage`, `Execute_ShouldProbeOnce_WhenHeadingAndBodyShareTheFamily`, `Execute_ShouldNotProbe_WhenNoCustomFontRequested`, `Execute_ShouldNotProbe_ForDefaultFontFamily` | `BuildThemeToolTests`, `BuildThemeCommandTests` |
| AC-7 — shape validation before the probe, and the narrowed second clause | `BuildTheme_ShouldFailWithoutProbing_WhenCssClassNameIsInvalid`, `BuildTheme_ShouldFailWithoutProbing_WhenBothVersionAndEnvironmentProvided`, and `BuildTheme_ShouldStillProbe_WhenWorkspaceIsNotAClioWorkspace` — which pins the ACCEPTED behaviour that colour/workspace/package failures are detected after the probe | `BuildThemeToolTests` |
| AC-8 (one contract on every surface) | `ThemingGuidanceResource_Should_Keep_NonGoogleFamily_Confirmation_Gated`, `ThemingGuidanceResource_Should_Describe_The_Unverified_FailOpen`, `ThemingGuidanceResource_Should_State_The_FamilyName_Contract`, `BuildThemeArgs_ShouldDocumentTheFamilyNameContract` (both font arguments), and the probe-disclosure assertions in `BuildThemeTool_Should_DeclareBuildSafetyFlags_WhenInspectingMcpServerToolAttribute` | `ThemingGuidanceResourceTests`, `BuildThemeToolTests` |

## Supporting invariants

| Invariant | Verified by |
| --- | --- |
| Definitive verdicts memoized for the full TTL; Unverified only for a short transient window | `LookupAsync_ShouldServeSecondLookupFromCache_ForDefinitiveVerdict`, `LookupAsync_ShouldServeFromCacheWithinTheTransientWindow_ThenReprobe_AfterUnverifiedVerdict`, `LookupAsync_ShouldKeepDefinitiveVerdict_BeyondTheTransientWindow` |
| One process-wide memo across independently built containers | `AvailabilityCache_ShouldBeShared_AcrossIndependentlyBuiltContainers` |
| Capacity bound holds under concurrent stores | `Store_ShouldStayNearCapacity_UnderConcurrentStores` |
| TTL expiry re-probes and replaces, never accumulates | `LookupAsync_ShouldReprobeAndReplaceExpiredEntry_OnNextLookup` |
| Cache is hard-bounded, reclaims by sweeping, still refreshes held keys | `Store_ShouldStopGrowing_AtCapacity`, `Store_ShouldSweepExpiredEntries_WhenAtCapacity`, `Store_ShouldRefreshExistingKey_WhenAtCapacity` |
| Ordinal (case-sensitive) keys end to end | `LookupAsync_ShouldProbeSeparately_ForCaseVariantFamilies`, `Execute_ShouldTreatCaseVariantsAsDistinctFamilies`, `Build_ShouldKeepImport_ForCaseVariantSuppressedEntry` |
| One canonical spelling for probe, cache key and token | `LookupAsync_ShouldCollapseWhitespace_BeforeProbingAndCaching`, `Execute_ShouldCollapseWhitespace_InRequestedFamilies`, `BuildTheme_ShouldProbeAndSuppressCanonicalSpelling_WhenFamilyIsPadded` |
| Caller cancellation propagates; only the probe budget maps to Unverified | `LookupAsync_ShouldPropagateCancellation_WhenCallerCancels` |
| DI: singleton memo, transient catalog, probe-client guards | `GoogleFontsDiRegistrationTests` (both tests) |
| Suppressed family still validated and still applied through the token | `Build_ShouldSkipImport_ForSuppressedFamily`, `Build_ShouldStillReject_InvalidFamilyWithSuppressedImport` |

## Manual / explicit

- **Live endpoint canary** — `GoogleFontsCatalogEndpointCanaryTests` (`[Explicit]`,
  `[Category("Integration")]`). Re-run when the contract is in doubt; it asserts published,
  multi-word, unpublished and wrong-case outcomes against fonts.google.com.
- **MCP e2e** — `clio.mcp.e2e/BuildThemeToolE2ETests` covers live suppression and the removed-argument
  rejection through the real MCP server. Not in CI; needs outbound network (with fonts.google.com
  unreachable the suppression assertion fails by design, since the probe degrades to fail-open).

## Not covered by automation

- The exact wording of the warning messages themselves (the tests match on stable fragments, not full text).
- The prose surfaces with no assertion: `clio/help/en/build-theme.txt`, `clio/docs/commands/build-theme.md`,
  `docs/McpCapabilityMap.md` and the toolkit `SKILL.md` (reviewed, not asserted). The MCP guidance
  resource and the MCP argument descriptions ARE pinned — see the AC-8 row.
- Middlebox behaviour that answers 404 for blocked hosts — recorded as an accepted risk in the ADR.
