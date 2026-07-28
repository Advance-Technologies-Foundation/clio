# Story 11: Incubation feature-flag gate on the passthrough behavior

**Feature**: mcp-http-credential-passthrough
**FR coverage**: OQ-03 (incubation gating); supports FR-10 (verb stays available)
**PRD**: [prd-mcp-http-credential-passthrough.md](../prd/prd-mcp-http-credential-passthrough.md)
**ADR**: [adr-mcp-http-credential-passthrough.md](../adr/adr-mcp-http-credential-passthrough.md) (step 11; OQ-03)
**Jira**: [ENG-93208](https://creatio.atlassian.net/browse/ENG-93208)
**Status**: ready-for-dev
**Size**: S (< 2h)
**Depends on**: Story 1 (spike), Story 2 (spike), Story 5 (api-key gate / passthrough-mode wiring)

---

## As a

platform admin

## I want

the passthrough **behavior** gated behind an incubation feature flag checked at middleware-wiring time — **not** a `[FeatureToggle]` on the `mcp-http` verb

## So that

the multi-tenant leg is doubly-gated (incubation flag AND api-key gate) while ENG-92869 stabilizes, without hiding the shipped `mcp-http` verb (which would regress FR-10/AC-10)

---

## Acceptance Criteria

- [x] **AC-01** — Given the incubation flag `mcp-http-credential-passthrough` is **disabled** (default), when `McpHttpServerCommand.Run` wires middleware, then the passthrough middleware/credential leg is **not** wired, and the verb, stdio, and `-e <env>` remain fully available and behave as 8.1.0.72 (maps OQ-03/FR-10; AC-10).
- [x] **AC-02** — Given the flag is **enabled**, when `Run` wires middleware, then the passthrough leg is wired (still additionally gated by the api-key gate from Story 5) (maps OQ-03).
- [x] **AC-03 (negative)** — Given the design, when reviewed, then there is **no** `[FeatureToggle]` on `McpHttpServerCommandOptions` (that would hide the verb entirely and regress FR-10) (maps OQ-03).
- [x] **AC-04** — Given both gates, when passthrough is honored, then it requires **both** the incubation flag enabled AND a matching api-key (doubly-gated) (maps OQ-03/FR-09).

## Implementation Notes

From ADR step 11 (OQ-03 correction):

- Check `IFeatureToggleService`/`ISettingsRepository.IsFeatureEnabled("mcp-http-credential-passthrough")` inside `McpHttpServerCommand.Run` at middleware-wiring time. Do **NOT** put `[FeatureToggle]` on the options class.
- Passthrough is doubly-gated: incubation flag **and** the api-key gate (Story 5). The verb, stdio, `-e <env>` remain always-available.
- Lift the flag when ENG-92869 stabilizes (documented follow-up).

Key files: `clio/Command/McpServer/McpHttpServerCommand.cs` (`Run` wiring), feature-toggle service usage.
Pattern to follow: existing `IFeatureToggleService` / feature-flag checks in the codebase (behavior-level, not verb-level).

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | flag off → passthrough middleware not wired, verb/stdio/-e unaffected; flag on → wired; doubly-gated (flag+key) | `clio.tests/Command/McpServer/PassthroughIncubationGateTests.cs` |

Test naming `MethodName_ShouldBehavior_WhenCondition`; AAA + `because` + `[Description]`; NSubstitute.
Targeted run: `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=McpServer"`

## Definition of Done

- [x] Code compiles with no new `CLIO*` warnings in modified files (CLIO001–CLIO005 clean)
- [x] No `[FeatureToggle]` on `McpHttpServerCommandOptions` (verb stays available)
- [x] No new CLI flags (any touched kebab-case, CLIO001)
- [x] Feature-toggle check resolved via DI (`BindingsModule`) — no MediatR; no raw `HttpClient`
- [x] MCP surface + docs reviewed (FR-15) — incubation note in Story 14; state outcome
- [x] Unit tests `[Category("Unit")]`; AAA + `because` + `[Description]`
- [x] Targeted `dotnet test --filter "Category=Unit&Module=McpServer"` green before commit
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started: 2026-07-09
- Implementation completed: 2026-07-09
- Tests passing: `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=McpServer" -f net10.0 --no-build` → Failed: 0, Passed: 1877, Skipped: 1.
- Notes:
  - **Feature-flag API used:** `Clio.Command.IFeatureToggleService.IsFeatureEnabled(string)`
    (impl `FeatureToggleService`, backed by `ISettingsRepository.IsFeatureEnabled`, which reads
    the `features` object in clio's `appsettings.json` with an `OrdinalIgnoreCase` comparer — so
    the key match is case-insensitive per the feature-toggle contract). `IFeatureToggleService`
    is already registered transient in the shared `BindingsModule` (line 313, present in the HTTP
    host graph) and depends only on the singleton `ISettingsRepository`; no new DI registration
    was needed. Resolved from `app.Services` after `builder.Build()`, alongside the existing
    `IPlatformApiKeyGate` resolution — no scoped captive, passes `ValidateOnBuild`/`ValidateScopes`.
  - **Wiring-gate point:** `McpHttpServerCommand.Run`, at middleware-wiring time. The two
    passthrough `app.Use(...)` additions (`EnforcePlatformApiKeyGate` + `CaptureCredentialContext`)
    are wrapped in `if (ShouldEnablePassthrough(featureToggleService))`. `UseHostFiltering`,
    `ValidateOrigin`, and `MapMcp` remain unconditional. Extraction is the minimal, behavior-
    preserving `internal static bool ShouldEnablePassthrough(IFeatureToggleService)`.
  - **NO `[FeatureToggle]` on the options class:** confirmed — `McpHttpServerCommandOptions`
    carries only `[Verb("mcp-http", ...)]` and its `[Option]`s; the incubation gate is a
    behavior-level flag key (`CredentialPassthroughFeatureName = "mcp-http-credential-passthrough"`),
    not a verb attribute, so the verb / stdio / `-e <env>` stay always-available (FR-10).
  - **Doubly-gated confirmed:** gate 1 (wiring) = incubation flag via `ShouldEnablePassthrough`;
    gate 2 (request-time) = Story 5 `IPlatformApiKeyGate` on the wired leg. Honoring a passed
    credential requires BOTH (test `Passthrough_ShouldBeHonored_OnlyWhenFlagEnabledAndKeyConfigured`).
  - **Consequence (spec-correct, flagged for architect):** with the flag OFF the api-key gate
    middleware is also unwired, so even if an operator sets `--platform-api-key` there is no 401 —
    the header path is fully inert (exactly "behaves as 8.1.0.72", which had no gate). The api-key
    gate is dormant until the incubation flag flips.
  - MCP surface / docs: no verb, flag, or tool change — MCP reviewed, no update required; docs
    incubation note deferred to Story 14 per this story's DoD.
