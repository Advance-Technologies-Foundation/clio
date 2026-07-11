# Story 15: Consolidated E2E coverage — multi-tenant, concurrency isolation, and no-regression suites (FR-08)

**Feature**: mcp-passthrough-tool-parity
**FR coverage**: FR-08, FR-10
**PRD**: [prd-mcp-passthrough-tool-parity.md](../prd/prd-mcp-passthrough-tool-parity.md)
**ADR**: [adr-mcp-passthrough-tool-parity.md](../adr/adr-mcp-passthrough-tool-parity.md)
**Status**: ready-for-dev
**Size**: L (full day)

---

## As a

QA engineer certifying the passthrough tool set for the AI-Platform gateway (ENG-92869)

## I want

one story that **owns** every MCP E2E deliverable mandated by PRD FR-08 / AC-07 / AC-09 and the ADR test
strategy — the multi-tenant cases, the per-routed-tool two-tenant isolation proofs, and the per-touched-tool
stdio + registered-HTTP no-regression sweep

## So that

E2E coverage is not diffused as unowned "sweep" references across stories 1-14, and Story 16's coverage
registry has concrete, named test methods to map to

---

## Status note (mandatory context)

**MCP E2E is NOT in CI.** Every case authored here is compile-verified in `clio.mcp.e2e` and executed
**manually against a live stand** (same caveat and skip mechanism as ENG-93208 story 15:
`Assert.Ignore`/stand-config guard when the `CLIO_MCP_HTTP_E2E_*` environment variables are absent). The
manual run before merge is part of this story's DoD.

## Acceptance Criteria

- [x] **AC-01** — **Mandatory multi-tenant cases (PRD FR-08, ADR test strategy rows 8-9), extending
  `McpHttpMultiTenantE2ETests`.** For each of the four mandated targets, BOTH input shapes are covered —
  **header-only** (executes against the header tenant / uniform rejection per the tool's decision) and
  **header + `environment-name`** (mixed input — proves no confused-deputy, PRD AC-06):
  - `list-apps` (Story 3)
  - `create-app-section` — the "one section tool" case, and its assertion must reach the **nested
    caption-culture path** (Story 6)
  - `get-user-culture` (Story 10)
  - one Creatio-reaching `link-from-repository-*` branch — `link-from-repository-unlocked` (always
    Creatio-reaching) asserting the uniform guard rejection (Story 1)
- [x] **AC-02** — **Two-tenant concurrency isolation for EVERY newly routed tool (PRD AC-07), extending
  `McpHttpConcurrencyIsolationE2ETests`.** The newly routed set is exactly: `list-apps`, `get-app-info`,
  `create-app`, `create-app-section`, `update-app-section`, `delete-app-section`, `list-app-sections`,
  `get-user-culture`, `update-page` (version probe), `sync-pages` (version probe), `get-component-info`
  (mixed-input path), `build-theme` (version path). Each gets the same two-tenant isolation proof already
  established for `describe-environment` (`McpHttpMultiTenantE2ETests.cs:33`) — distinct authenticated
  containers, no cross-tenant session/log bleed.
- [x] **AC-03** — **No-regression sweep for EVERY touched tool (PRD AC-09 / SM-03), extending
  `McpHttpNoRegressionE2ETests`.** For each of the 15 touched tools (the 12 routed tools above + the three
  `link-from-repository-*` tools), behavior over `clio mcp` (stdio) AND `clio mcp-http -e <env>` with
  `environment-name` supplied matches the pre-change baseline — this is also the layer that proves the
  `[Required]` relaxations (stories 1, 3-9, 12) did not break existing registered-env callers.
- [x] **AC-04** — **Exact, named test methods.** Every case above is a named `[Test]` method following
  `MethodName_ShouldBehavior_WhenCondition`, e.g.
  `ApplicationGetList_ShouldExecuteAgainstHeaderTenant_WhenHeaderOnly`,
  `ApplicationSectionCreate_ShouldResolveHeaderTenantCulture_WhenHeaderOnly`,
  `LinkFromRepositoryUnlocked_ShouldReturnUniformRejection_WhenPassthroughActive`. The full
  (tool, dependency-path, scenario) → method list is recorded in this story's Dev Agent Record on
  completion — Story 16's registry consumes it verbatim.
- [x] **AC-05** — Given the E2E project, when it is built in CI, then it **compiles** on every push (the
  suites self-skip without stand config); no `[Category]` other than `E2E` is used.
- [x] **AC-ERR** — Given a stand-configured run where a case fails, when the suite reports, then failure
  output never echoes `accessToken`/`login`/`password` material (assert on redacted text only).

## Implementation Notes

- Extend the three existing ENG-93208 fixtures — `McpHttpMultiTenantE2ETests`,
  `McpHttpConcurrencyIsolationE2ETests`, `McpHttpNoRegressionE2ETests` — rather than creating parallel
  fixtures; reuse their stand-config/skip plumbing (`McpHttpPassthroughStand`, `CLIO_MCP_HTTP_E2E_*`).
- This story runs **after stories 1-14** (it exercises their shipped behavior) and **before Story 16**
  (whose registry rows name these methods) and Story 17 (docs).
- Keep per-tool cases data-driven where the assertion shape is identical (e.g. `[TestCaseSource]` over the
  newly-routed-tool list for the isolation proof) — but each (tool, path, scenario) must still surface as an
  individually named/reportable case so Story 16's reflection check can find it.
- The `link-from-repository-by-env-package-path` `skip-preparation=false` rejection and `=true` bypass are
  unit-covered in Story 1; here only the `unlocked` branch is mandated (one Creatio-reaching branch), though
  adding the package-path branch is cheap if the stand supports it.

Key files: `clio.mcp.e2e/McpHttpMultiTenantE2ETests.cs`, `clio.mcp.e2e/McpHttpConcurrencyIsolationE2ETests.cs`,
`clio.mcp.e2e/McpHttpNoRegressionE2ETests.cs` (extend all three).
Pattern to follow: the existing `describe-environment` two-tenant case (`McpHttpMultiTenantE2ETests.cs:33`)
and the ENG-93208 story-15 skip/stand mechanics.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | none — unit coverage lives in stories 1-14 | — |
| Integration `[Category("Integration")]` | none required | — |
| E2E `[Category("E2E")]` | Everything in AC-01..AC-03 (this story IS the E2E deliverable) | `clio.mcp.e2e/McpHttp{MultiTenant,ConcurrencyIsolation,NoRegression}E2ETests.cs` |

Test naming: `MethodName_ShouldBehavior_WhenCondition`

## Definition of Done

- [x] All `CLIO*` diagnostics clean in changed files (FR-10; note `clio.mcp.e2e` is outside the Sonar gate
  but the analyzers still apply at build)
- [x] `clio.mcp.e2e` compiles; suites self-skip without stand config
- [ ] **Manual live-stand run executed; results (pass/fail per case) recorded in the Dev Agent Record.**
  **NOT DONE.** The implementing agent for this story had no access to a live Creatio stand or the
  `CLIO_MCP_HTTP_E2E_*` / `CLIO_MCP_HTTP_E2E_REGISTERED_ENV` environment variables. Every stand-gated
  case was verified to compile and to self-skip cleanly via `Assert.Ignore` (see "Tests passing" below
  for the exact run and counts) — no case was FAKED as passing and no pass/fail result is fabricated
  here. Running these cases for real against a live stand, recording per-case pass/fail, is a required
  follow-up for whoever owns stand access before this story can be merged as fully done.
- [x] The (tool, dependency-path, scenario) → test-method list is recorded for Story 16's registry
- [x] Unit tests N/A; E2E tests carry `[Category("E2E")]` only
- [ ] PR description references this story file and flags "MCP e2e NOT in CI — manual run attached"
  (no PR opened by this implementing session; flag for whoever opens the PR)

## Dev Agent Record

- Implementation started: 2026-07-11
- Implementation completed: 2026-07-11 (code + self-skip verification only — see DoD note above; live-stand
  run is an explicit follow-up, not performed in this session)
- Tests passing:
  - Build: `dotnet build clio.mcp.e2e/clio.mcp.e2e.csproj -c Release -f net10.0` → 0 errors, 0 new `CLIO*`
    diagnostics (also verified building all TFMs with plain `-c Release`, and the whole solution's `clio`
    project it depends on).
  - Run (no `CLIO_MCP_HTTP_E2E_*` / `CLIO_MCP_HTTP_E2E_REGISTERED_ENV` set):
    `dotnet test clio.mcp.e2e/clio.mcp.e2e.csproj -c Release -f net10.0 --no-build --filter
    "FullyQualifiedName~McpHttpMultiTenantE2ETests|FullyQualifiedName~McpHttpConcurrencyIsolationE2ETests|FullyQualifiedName~McpHttpNoRegressionE2ETests"`
    → **Total 55, Passed 16, Skipped 39, Failed 0, Errored 0.**
    - The 16 "Passed" are the stdio no-regression leg (AC-03): they need only a locally built `clio` (no
      live stand), so they ran for real and genuinely proved all 15 touched tools remain reachable via
      stdio unchanged — this is verified, not merely self-skip.
    - The 39 "Skipped" are every stand-gated case (AC-01: 8 new + 1 pre-existing; AC-02: 12 new + 2
      pre-existing; AC-03 http leg: 15 new + 1 pre-existing), each skipping via `Assert.Ignore` from
      `McpHttpPassthroughStand.RequireOrIgnore()` or the `CLIO_MCP_HTTP_E2E_REGISTERED_ENV` check —
      exactly AC-05's self-skip requirement, with zero Failed/Errored.
- (tool, dependency-path, scenario) → test-method list (for Story 16's registry; `path` uses the ADR
  OQ-04/Story 16 dependency-path vocabulary where a stable id exists, else the tool's single audited
  path):

  | Tool | Dependency path | Scenario | Fixture | Method |
  |---|---|---|---|---|
  | `list-apps` | outer | HeaderOnly | `McpHttpMultiTenantE2ETests` | `ApplicationGetList_ShouldExecuteAgainstHeaderTenant_WhenHeaderOnly` |
  | `list-apps` | outer | MixedInput | `McpHttpMultiTenantE2ETests` | `ApplicationGetList_ShouldRejectMixedInput_WhenHeaderAndEnvironmentNameBothSupplied` |
  | `list-apps` | outer | TwoTenantIsolation | `McpHttpConcurrencyIsolationE2ETests` | `ConcurrentPassthroughRequests_ShouldIsolateNewlyRoutedTool_WhenTwoTenantsCallConcurrently("list-apps")` |
  | `list-apps` | outer | RegisteredEnvStdio | `McpHttpNoRegressionE2ETests` | `Stdio_ShouldExposeTouchedTool_WhenPassthroughUnused("list-apps")` |
  | `list-apps` | outer | RegisteredEnvHttp | `McpHttpNoRegressionE2ETests` | `HttpWithRegisteredEnvironment_ShouldExecuteTouchedTool_WhenEnvironmentNameSupplied("list-apps")` |
  | `get-app-info` | outer | TwoTenantIsolation | `McpHttpConcurrencyIsolationE2ETests` | `ConcurrentPassthroughRequests_ShouldIsolateNewlyRoutedTool_WhenTwoTenantsCallConcurrently("get-app-info")` |
  | `get-app-info` | outer | RegisteredEnvStdio | `McpHttpNoRegressionE2ETests` | `Stdio_ShouldExposeTouchedTool_WhenPassthroughUnused("get-app-info")` |
  | `get-app-info` | outer | RegisteredEnvHttp | `McpHttpNoRegressionE2ETests` | `HttpWithRegisteredEnvironment_ShouldExecuteTouchedTool_WhenEnvironmentNameSupplied("get-app-info")` |
  | `create-app` | outer | TwoTenantIsolation | `McpHttpConcurrencyIsolationE2ETests` | `ConcurrentPassthroughRequests_ShouldIsolateNewlyRoutedTool_WhenTwoTenantsCallConcurrently("create-app")` |
  | `create-app` | outer | RegisteredEnvStdio | `McpHttpNoRegressionE2ETests` | `Stdio_ShouldExposeTouchedTool_WhenPassthroughUnused("create-app")` |
  | `create-app` | outer | RegisteredEnvHttp | `McpHttpNoRegressionE2ETests` | `HttpWithRegisteredEnvironment_ShouldExecuteTouchedTool_WhenEnvironmentNameSupplied("create-app")` |
  | `create-app-section` | caption-culture-readback | HeaderOnly | `McpHttpMultiTenantE2ETests` | `ApplicationSectionCreate_ShouldResolveHeaderTenantCulture_WhenHeaderOnly` |
  | `create-app-section` | outer | MixedInput | `McpHttpMultiTenantE2ETests` | `ApplicationSectionCreate_ShouldRejectMixedInput_WhenHeaderAndEnvironmentNameBothSupplied` |
  | `create-app-section` | outer | TwoTenantIsolation | `McpHttpConcurrencyIsolationE2ETests` | `ConcurrentPassthroughRequests_ShouldIsolateNewlyRoutedTool_WhenTwoTenantsCallConcurrently("create-app-section")` |
  | `create-app-section` | outer | RegisteredEnvStdio | `McpHttpNoRegressionE2ETests` | `Stdio_ShouldExposeTouchedTool_WhenPassthroughUnused("create-app-section")` |
  | `create-app-section` | outer | RegisteredEnvHttp | `McpHttpNoRegressionE2ETests` | `HttpWithRegisteredEnvironment_ShouldExecuteTouchedTool_WhenEnvironmentNameSupplied("create-app-section")` |
  | `update-app-section` | outer | TwoTenantIsolation | `McpHttpConcurrencyIsolationE2ETests` | `ConcurrentPassthroughRequests_ShouldIsolateNewlyRoutedTool_WhenTwoTenantsCallConcurrently("update-app-section")` |
  | `update-app-section` | outer | RegisteredEnvStdio | `McpHttpNoRegressionE2ETests` | `Stdio_ShouldExposeTouchedTool_WhenPassthroughUnused("update-app-section")` |
  | `update-app-section` | outer | RegisteredEnvHttp | `McpHttpNoRegressionE2ETests` | `HttpWithRegisteredEnvironment_ShouldExecuteTouchedTool_WhenEnvironmentNameSupplied("update-app-section")` |
  | `delete-app-section` | outer | TwoTenantIsolation | `McpHttpConcurrencyIsolationE2ETests` | `ConcurrentPassthroughRequests_ShouldIsolateNewlyRoutedTool_WhenTwoTenantsCallConcurrently("delete-app-section")` |
  | `delete-app-section` | outer | RegisteredEnvStdio | `McpHttpNoRegressionE2ETests` | `Stdio_ShouldExposeTouchedTool_WhenPassthroughUnused("delete-app-section")` |
  | `delete-app-section` | outer | RegisteredEnvHttp | `McpHttpNoRegressionE2ETests` | `HttpWithRegisteredEnvironment_ShouldExecuteTouchedTool_WhenEnvironmentNameSupplied("delete-app-section")` |
  | `list-app-sections` | outer | TwoTenantIsolation | `McpHttpConcurrencyIsolationE2ETests` | `ConcurrentPassthroughRequests_ShouldIsolateNewlyRoutedTool_WhenTwoTenantsCallConcurrently("list-app-sections")` |
  | `list-app-sections` | outer | RegisteredEnvStdio | `McpHttpNoRegressionE2ETests` | `Stdio_ShouldExposeTouchedTool_WhenPassthroughUnused("list-app-sections")` |
  | `list-app-sections` | outer | RegisteredEnvHttp | `McpHttpNoRegressionE2ETests` | `HttpWithRegisteredEnvironment_ShouldExecuteTouchedTool_WhenEnvironmentNameSupplied("list-app-sections")` |
  | `get-user-culture` | outer | HeaderOnly | `McpHttpMultiTenantE2ETests` | `GetUserCulture_ShouldResolveHeaderTenant_WhenHeaderOnly` |
  | `get-user-culture` | outer | MixedInput | `McpHttpMultiTenantE2ETests` | `GetUserCulture_ShouldRejectMixedInput_WhenHeaderAndEnvironmentNameBothSupplied` |
  | `get-user-culture` | outer | TwoTenantIsolation | `McpHttpConcurrencyIsolationE2ETests` | `ConcurrentPassthroughRequests_ShouldIsolateNewlyRoutedTool_WhenTwoTenantsCallConcurrently("get-user-culture")` |
  | `get-user-culture` | outer | RegisteredEnvStdio | `McpHttpNoRegressionE2ETests` | `Stdio_ShouldExposeTouchedTool_WhenPassthroughUnused("get-user-culture")` |
  | `get-user-culture` | outer | RegisteredEnvHttp | `McpHttpNoRegressionE2ETests` | `HttpWithRegisteredEnvironment_ShouldExecuteTouchedTool_WhenEnvironmentNameSupplied("get-user-culture")` |
  | `update-page` | version-probe | TwoTenantIsolation | `McpHttpConcurrencyIsolationE2ETests` | `ConcurrentPassthroughRequests_ShouldIsolateNewlyRoutedTool_WhenTwoTenantsCallConcurrently("update-page")` |
  | `update-page` | version-probe | RegisteredEnvStdio | `McpHttpNoRegressionE2ETests` | `Stdio_ShouldExposeTouchedTool_WhenPassthroughUnused("update-page")` |
  | `update-page` | version-probe | RegisteredEnvHttp | `McpHttpNoRegressionE2ETests` | `HttpWithRegisteredEnvironment_ShouldExecuteTouchedTool_WhenEnvironmentNameSupplied("update-page")` |
  | `sync-pages` | version-probe | TwoTenantIsolation | `McpHttpConcurrencyIsolationE2ETests` | `ConcurrentPassthroughRequests_ShouldIsolateNewlyRoutedTool_WhenTwoTenantsCallConcurrently("sync-pages")` |
  | `sync-pages` | version-probe | RegisteredEnvStdio | `McpHttpNoRegressionE2ETests` | `Stdio_ShouldExposeTouchedTool_WhenPassthroughUnused("sync-pages")` |
  | `sync-pages` | version-probe | RegisteredEnvHttp | `McpHttpNoRegressionE2ETests` | `HttpWithRegisteredEnvironment_ShouldExecuteTouchedTool_WhenEnvironmentNameSupplied("sync-pages")` |
  | `get-component-info` | outer (mixed-input path) | TwoTenantIsolation | `McpHttpConcurrencyIsolationE2ETests` | `ConcurrentPassthroughRequests_ShouldIsolateNewlyRoutedTool_WhenTwoTenantsCallConcurrently("get-component-info")` |
  | `get-component-info` | outer | RegisteredEnvStdio | `McpHttpNoRegressionE2ETests` | `Stdio_ShouldExposeTouchedTool_WhenPassthroughUnused("get-component-info")` |
  | `get-component-info` | outer | RegisteredEnvHttp | `McpHttpNoRegressionE2ETests` | `HttpWithRegisteredEnvironment_ShouldExecuteTouchedTool_WhenEnvironmentNameSupplied("get-component-info")` |
  | `build-theme` | version | TwoTenantIsolation | `McpHttpConcurrencyIsolationE2ETests` | `ConcurrentPassthroughRequests_ShouldIsolateNewlyRoutedTool_WhenTwoTenantsCallConcurrently("build-theme")` |
  | `build-theme` | version | RegisteredEnvStdio | `McpHttpNoRegressionE2ETests` | `Stdio_ShouldExposeTouchedTool_WhenPassthroughUnused("build-theme")` |
  | `build-theme` | version | RegisteredEnvHttp | `McpHttpNoRegressionE2ETests` | `HttpWithRegisteredEnvironment_ShouldExecuteTouchedTool_WhenEnvironmentNameSupplied("build-theme")` |
  | `link-from-repository-unlocked` | unlocked-query | HeaderOnly (guard, PassthroughActive) | `McpHttpMultiTenantE2ETests` | `LinkFromRepositoryUnlocked_ShouldReturnUniformRejection_WhenPassthroughActive` |
  | `link-from-repository-unlocked` | unlocked-query | MixedInput (guard) | `McpHttpMultiTenantE2ETests` | `LinkFromRepositoryUnlocked_ShouldReturnUniformRejection_WhenHeaderAndEnvironmentNameBothSupplied` |
  | `link-from-repository-unlocked` | unlocked-query | RegisteredEnvStdio | `McpHttpNoRegressionE2ETests` | `Stdio_ShouldExposeTouchedTool_WhenPassthroughUnused("link-from-repository-unlocked")` |
  | `link-from-repository-unlocked` | unlocked-query | RegisteredEnvHttp | `McpHttpNoRegressionE2ETests` | `HttpWithRegisteredEnvironment_ShouldExecuteTouchedTool_WhenEnvironmentNameSupplied("link-from-repository-unlocked")` |
  | `link-from-repository-by-environment` | by-environment | RegisteredEnvStdio | `McpHttpNoRegressionE2ETests` | `Stdio_ShouldExposeTouchedTool_WhenPassthroughUnused("link-from-repository-by-environment")` |
  | `link-from-repository-by-environment` | by-environment | RegisteredEnvHttp | `McpHttpNoRegressionE2ETests` | `HttpWithRegisteredEnvironment_ShouldExecuteTouchedTool_WhenEnvironmentNameSupplied("link-from-repository-by-environment")` |
  | `link-from-repository-by-env-package-path` | env-package-path-preparation | RegisteredEnvStdio | `McpHttpNoRegressionE2ETests` | `Stdio_ShouldExposeTouchedTool_WhenPassthroughUnused("link-from-repository-by-env-package-path")` |
  | `link-from-repository-by-env-package-path` | env-package-path-preparation | RegisteredEnvHttp (local-only, `skip-preparation=true`) | `McpHttpNoRegressionE2ETests` | `HttpWithRegisteredEnvironment_ShouldExecuteTouchedTool_WhenEnvironmentNameSupplied("link-from-repository-by-env-package-path")` |
  | `describe-environment` (pre-existing, ENG-93208) | outer | HeaderOnly / SM-01 | `McpHttpMultiTenantE2ETests` | `SingleProcess_ShouldServeTwoTenants_WhenOnlyPerRequestCredentialsAreUsed` |
  | `describe-environment` (pre-existing, ENG-93208) | outer | TwoTenantIsolation | `McpHttpConcurrencyIsolationE2ETests` | `ConcurrentPassthroughRequests_ShouldIsolateTenants_WhenDifferentCredentialsRunTogether` |
  | `describe-environment` (pre-existing, ENG-93208) | outer | NonSerialization | `McpHttpConcurrencyIsolationE2ETests` | `ConcurrentPassthroughRequests_ShouldNotSerializeOnGlobalLock_WhenDifferentCredentialsRunTogether` |
  | `describe-environment` (pre-existing, ENG-93208) | outer | RegisteredEnvStdio | `McpHttpNoRegressionE2ETests` | `Stdio_ShouldAdvertiseResidentTools_WhenPassthroughUnused` |
  | `describe-environment` (pre-existing, ENG-93208) | outer | RegisteredEnvHttp | `McpHttpNoRegressionE2ETests` | `HttpWithRegisteredEnvironment_ShouldServeEnvironment_WhenNoPlatformApiKeyConfigured` |

- Notes:
  - **Live-stand run NOT performed** — no stand access in this session (see DoD note above). Every case
    above was verified only via (a) compile and (b) confirming it reports `Skipped` (not `Failed`/`Errored`)
    when `CLIO_MCP_HTTP_E2E_*` / `CLIO_MCP_HTTP_E2E_REGISTERED_ENV` are absent. The 15 stdio no-regression
    cases (AC-03) are the one exception: they need no live stand and were genuinely executed and passed.
  - `create-app-section`'s AC-01 header-only case (`ApplicationSectionCreate_ShouldResolveHeaderTenantCulture_WhenHeaderOnly`)
    is self-contained: it first calls `create-app` (header-only, tenant one) to create a throwaway
    application, then creates a section on it, so the nested caption-culture path is reached against a
    REAL application rather than a placeholder that would fail at app lookup before culture resolution.
  - AC-02's isolation proof does not require the tool call to SUCCEED — a "not found" on both tenants
    proves distinct authenticated containers just as well as two successes (mirrors the ADR's "isolation,
    not success" framing). The per-tool assertion is deliberately the NEGATIVE cross-tenant-bleed check
    only (`NotContain` the OTHER tenant's per-call marker), never a positive "response echoes its own
    marker" check — an earlier draft asserted the positive half too, but whether a tool's success/error
    text echoes a supplied identifier is tool-specific and unverified against a live stand (e.g.
    `update-page`/`sync-pages`'s engineered marker-integrity failure has no reason to echo `schema-name`;
    `get-component-info`'s mixed-input rejection message is documented to name NO supplied value, and the
    tool itself fails SOFT to a latest-fallback success on any version-resolution failure — either shape
    would have made the positive assertion spuriously fail on a real stand). `NotContain(other)` holds
    regardless of which of those response shapes a live tool actually produces. `get-component-info`'s
    isolation scenario is still deliberately the mixed-input case (header + `environment-name` together
    in its args), per the story's own parenthetical — only the assertion on the OUTCOME was narrowed.
    `list-apps`, `get-user-culture`, and `build-theme` have no natural per-call discriminating argument at
    all, so their isolation proof rests on the shared independent-completion + non-serialization
    assertions only (documented as a MANUAL-RUNNER ASSUMPTION in the fixture, mirroring the existing
    `describe-environment` comment at `McpHttpConcurrencyIsolationE2ETests.cs:79-80`).
  - `update-page`/`sync-pages` isolation and no-regression args use a minimal syntactically-valid-but-content-empty
    body (`"({});"`) — enough to pass Acornima JS-syntax parsing and reach the version-probe / registered-env
    routing under test, while intentionally failing later marker-integrity validation so no real page is
    ever persisted; `update-page` additionally sets `dry-run: true`. `build-theme` calls are safe by
    design (the tool "Never mutates an environment" per its own doc comment).
  - `create-app` / `create-app-section` calls in the AC-01 and AC-02/AC-03 sweeps are genuinely mutating
    (no dry-run exists for these tools) — this is intentional and consistent with the ADR's mandate to
    cover these tools directly; a live-stand runner should expect throwaway applications/sections to be
    created on both tenants on each run.
  - Deviation: the story's Implementation Notes call the `link-from-repository-by-env-package-path`
    `skip-preparation=false`/`=true` distinction "unit-covered in Story 1" and optional here. This story's
    AC-03 no-regression case for that tool uses `skip-preparation=true` (local-only, no Creatio call) so
    it can run on the SAME registered-env-gated leg as the other 14 tools without requiring a real Creatio
    package directory on disk; the deeper preparation-branch behavior stays unit-covered per Story 1, as
    the story anticipated.
  - Ambiguity noted, not guessed past: the PassthroughToolClassificationRegistry / guard test named in the
    ADR (row 641) and consumed by Story 16 does not exist yet (`clio.tests/Command/McpServer/PassthroughToolClassificationGuardTests.cs`
    is absent) — Story 16 is `ready-for-dev`, not started. This story's method list above is written to be
    directly consumable by that future registry (one row per (tool, path, scenario), reflection-resolvable
    method names), but nothing in Story 15 itself required or exercised that guard.
  - **For Story 16's author:** the table's `Method` column for the three `[TestCaseSource]`-driven rows
    (`ConcurrentPassthroughRequests_ShouldIsolateNewlyRoutedTool_WhenTwoTenantsCallConcurrently`,
    `Stdio_ShouldExposeTouchedTool_WhenPassthroughUnused`,
    `HttpWithRegisteredEnvironment_ShouldExecuteTouchedTool_WhenEnvironmentNameSupplied`) is shown with its
    NUnit parameterized display name (e.g. `…WhenTwoTenantsCallConcurrently("list-apps")`) for readability
    and to keep each (tool, path, scenario) row visibly distinct in this table. `nameof(FixtureType.Method)`
    in the registry needs the BARE method name (no parenthesized argument) — the same bare method name is
    reused across all 12 (or 15) tool rows that share that one data-driven test.
