# Story 3: `list-apps` — route through the resolver (class c1)

**Feature**: mcp-passthrough-tool-parity
**FR coverage**: FR-01, FR-05, FR-05a, FR-10
**PRD**: [prd-mcp-passthrough-tool-parity.md](../prd/prd-mcp-passthrough-tool-parity.md)
**ADR**: [adr-mcp-passthrough-tool-parity.md](../adr/adr-mcp-passthrough-tool-parity.md)
**Status**: ready-for-dev
**Size**: M (half day)

---

## As a

CI pipeline author (AI-Platform gateway operator)

## I want

`list-apps` (`ApplicationGetListTool`) to execute against the header-supplied tenant under authorized
credential passthrough

## So that

the PRD's flagship example tool stops throwing the opaque `Environment name is required. (Parameter
'environmentName')` and becomes usable over the one transport the gateway targets

---

## Merge order (shared-file constraint)

All seven c1 tool classes are co-located in `clio/Command/McpServer/Tools/ApplicationTool.cs` and the args
records in `ApplicationToolArgs.cs`. Stories 3-9 are therefore **serialized: 3 → 4 → 5 → 6 → 7 → 8 → 9**
(encoded via `depends_on` in `spec/sprint-status.yaml`) so no two open PRs touch the same file concurrently.
This story is **first** in the chain.

## Acceptance Criteria

- [ ] **AC-01** (PRD AC-01, decision-matrix "Route") — Given authorized passthrough and zero registered
  environments, when `list-apps` is called with a valid `X-Integration-Credentials` header and no
  `environment-name`, then it returns the tenant's applications — never
  `Environment name is required. (Parameter 'environmentName')`.
- [ ] **AC-02** — **Conditional requiredness (FR-05a) — blocking prerequisite for AC-01.** Given authorized
  passthrough, when `list-apps` is called with no `environment-name`, then the MCP schema does **not** reject
  the call at pre-tool binding — `[Required]` is removed from `environment-name` on the corresponding
  `ApplicationGetListArgs` record (today `ApplicationToolArgs.cs:11,21`, ADR "CLI flag specification" table),
  making it schema-optional. On non-passthrough transports, runtime requiredness is enforced by the existing
  `IToolCommandResolver.ResolveSettingsAndKey`'s `EnvironmentResolutionException` throw when no
  environment/URI is resolvable (ADR OQ-03, "Resolver-ROUTED tools" — no new validation layer for this
  group). Without this AC, AC-01's header-only call is rejected before the tool method ever runs.
- [ ] **AC-03** — **Mixed input (PRD AC-06).** Given authorized passthrough with both the header and an
  explicit `environment-name` naming a different registered environment, when `list-apps` runs, then it is
  rejected by the existing `HasExplicitCredentialArgs` check before any Creatio-reaching call — it never
  uses the named environment's stored credentials.
- [ ] **AC-04** (PRD AC-09 / SM-03) — Given `clio mcp` (stdio) or `clio mcp-http -e <env>` with a registered
  environment, when `list-apps` is called with `environment-name`, then behavior matches the pre-change
  baseline exactly.
- [ ] **AC-05** (PRD AC-07, concurrency isolation) — Given two concurrent `list-apps` calls with different
  credentials, when both run, then each resolves a distinct authenticated container via
  `IToolCommandResolver` with no cross-tenant session/log bleed (E2E proof owned by Story 15).
- [ ] **AC-ERR** — **Error semantics respect the ENG-93208 middleware boundary (PRD AC-ERR).**
  (a) A **malformed/unusable** header is out of scope: the middleware returns HTTP 400 **before** any tool
  is invoked (`McpHttpServerCommand.cs:317`) — the tool is never entered and must not add handling for it.
  (b) Given a **valid** header whose target operation fails (unreachable tenant, auth failure, resolver
  error), when the tool executes, then it returns the typed error envelope (`ApplicationToolResponses`-shaped
  or `CallToolResult.IsError=true`) with `SensitiveErrorTextRedactor`-redacted text — no
  `accessToken`/`login`/`password` leaks.

## Implementation Notes

Depends on Story 2 for the **shared** overloads (`ICaptionCultureResolver`, `IApplicationInfoService`) —
but the `IApplicationListService` settings-based overload is **owned by THIS story** (ADR slice 6c); Story 2
deliberately does not add it:

```csharp
public interface IApplicationListService {
    ApplicationListResult GetApplications(string environmentName, string? id, string? code);              // unchanged
    ApplicationListResult GetApplications(EnvironmentSettings environmentSettings, string? id, string? code); // NEW — added here
}
```

`ApplicationGetListTool` derives from `BaseTool<EnvironmentOptions>(null, logger, commandResolver)`
(established pattern — `CheckThemingAccessTool.cs:20`, `GetCreatioInfoTool.cs:20`) and wraps the body in the
**options-aware** `ExecuteWithCleanLog(options, () => {...})` overload
(`clio/Command/McpServer/Tools/BaseTool.cs:63`) — NOT the zero-arg one, which falls back to
`McpToolExecutionLock.SharedFallbackKey` and does not key the lock per tenant. This is what closes the
FR-05 tenant-lock/in-flight-guard requirement:

```csharp
public sealed class ApplicationGetListTool(ILogger logger, IToolCommandResolver commandResolver,
    IApplicationListService applicationListService)
    : BaseTool<EnvironmentOptions>(null, logger, commandResolver) {
    public ApplicationListResponse ApplicationGetList(ApplicationGetListArgs args) {
        EnvironmentOptions options = new() { Environment = args.EnvironmentName };
        return ExecuteWithCleanLog(options, () => {
            try {
                EnvironmentSettings settings = commandResolver.Resolve<EnvironmentSettings>(options);
                var applications = applicationListService.GetApplications(settings, args.Id, args.Code);
                return ApplicationToolHelper.CreateListResponse(/* map applications */);
            } catch (Exception ex) {
                return ApplicationToolHelper.CreateListErrorResponse(SensitiveErrorTextRedactor.Redact(ex.Message));
            }
        });
    }
}
```

**Also remove `[Required]` from `environment-name`** on the `ApplicationGetListArgs` record (ADR "CLI flag
specification" table lists all 7 Application args records) — schema-optional so a header-only passthrough
call reaches the tool method instead of being rejected at MCP binding before `ExecuteWithCleanLog` ever runs.
This is not optional polish: AC-01 cannot pass without it (PRD A-02 / FR-05a).

Behavior by transport (unchanged from today): stdio/registered-env resolves via the unchanged registry
branch; header-only resolves against the ephemeral header settings; header + explicit `environment-name` is
rejected by the existing `HasExplicitCredentialArgs` check with no new code.

Key files: `clio/Command/ApplicationListService.cs`,
`clio/Command/McpServer/Tools/ApplicationTool.cs`, `clio/Command/McpServer/Tools/ApplicationToolArgs.cs`.
Pattern to follow: `GetCreatioInfoTool` (`describe-environment`) — the only tool already proven multi-tenant
(`McpHttpMultiTenantE2ETests.cs:33`).

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | Header-only executes against header tenant (schema no longer rejects a blank `environment-name`); mixed-input rejected (AC-03); registered-env/stdio unchanged (including the non-passthrough required-arg throw) | `clio.tests/Command/McpServer/ApplicationGetListToolPassthroughTests.cs` |
| Integration `[Category("Integration")]` | none required — service call is mocked at the `IApplicationClient` boundary in unit tests | — |
| E2E `[Category("E2E")]` | Owned by **Story 15**: header-only and header+`environment-name` for `list-apps` (`McpHttpMultiTenantE2ETests`), two-tenant isolation (`McpHttpConcurrencyIsolationE2ETests`), stdio + `-e` no-regression (`McpHttpNoRegressionE2ETests`). E2E is the layer that actually exercises the MCP-schema binding (`[Required]` removal) end to end. Manual only — MCP e2e not in CI | `clio.mcp.e2e/` (Story 15) |

Test naming: `MethodName_ShouldBehavior_WhenCondition`

## Definition of Done

- [ ] All `CLIO*` diagnostics clean in changed files — including CLIO005 for the new
  `IApplicationListService` overload wiring (FR-10)
- [ ] Targeted tests green before commit: `dotnet test clio.tests/clio.tests.csproj --filter
  "Category=Unit&(Module=McpServer|Module=Command)" --no-build` (ADR slice 9)
- [ ] All new/changed MCP arguments stay kebab-case (relaxing `[Required]` does not rename `environment-name`)
- [ ] Unit tests added with `[Category("Unit")]` — never `[Category("UnitTests")]`
- [ ] PR description references this story file

## Dev Agent Record

{Left blank — filled by dev agent during implementation}
- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
