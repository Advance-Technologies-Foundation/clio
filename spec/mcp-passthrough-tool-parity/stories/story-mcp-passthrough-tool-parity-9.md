# Story 9: `list-app-sections` — route through the resolver, including nested `FindApplicationId` (class c1)

**Feature**: mcp-passthrough-tool-parity
**FR coverage**: FR-01, FR-05, FR-05a, FR-10
**PRD**: [prd-mcp-passthrough-tool-parity.md](../prd/prd-mcp-passthrough-tool-parity.md)
**ADR**: [adr-mcp-passthrough-tool-parity.md](../adr/adr-mcp-passthrough-tool-parity.md)
**Status**: ready-for-dev
**Size**: S (< 2h)

---

## As a

CI pipeline author (AI-Platform gateway operator)

## I want

`list-app-sections` (`ApplicationSectionGetListTool`) to execute against the header-supplied tenant,
including its nested `FindApplicationId` lookup

## So that

listing an application's sections under passthrough does not fail with an opaque error or leak the active
tenant's app-id resolution

---

## Merge order (shared-file constraint)

Stories 3-9 all modify `clio/Command/McpServer/Tools/ApplicationTool.cs`, so they are **serialized:
3 → 4 → 5 → 6 → 7 → 8 → 9** via `depends_on`. This story is **last** in the chain and starts only after
**Story 8** merges.

## Acceptance Criteria

- [x] **AC-01** (PRD AC-02, decision-matrix "Route — full nested graph") — Given authorized passthrough and
  no `environment-name`, when `list-app-sections` runs, then it returns the header tenant's sections — never
  `Environment name is required.`.
- [x] **AC-02** — **Conditional requiredness (FR-05a) — blocking prerequisite for AC-01.** Given authorized
  passthrough, when `list-app-sections` is called with no `environment-name`, then the MCP schema does
  **not** reject the call at pre-tool binding — `[Required]` is removed from `environment-name` on the
  corresponding `ApplicationSectionGetListArgs` record (ADR "CLI flag specification" table), making it
  schema-optional. On non-passthrough transports, runtime requiredness is enforced by the existing
  `IToolCommandResolver.ResolveSettingsAndKey`'s `EnvironmentResolutionException` throw (ADR OQ-03,
  "Resolver-ROUTED tools").
- [x] **AC-03** — **Nested `FindApplicationId` path.** Given the same setup, when
  `ApplicationSectionGetListCommand`'s `applicationInfoService.FindApplicationId(environmentName,
  request.ApplicationCode)` call (today `ApplicationSectionGetListCommand.cs:74`) runs, then it uses the
  settings-based `FindApplicationId(EnvironmentSettings, string)` overload from Story 2 and resolves the
  application id against the **header** tenant.
- [x] **AC-04** — **Mixed input (PRD AC-06).** Given authorized passthrough with both the header and an
  explicit `environment-name` naming a different registered environment, when `list-app-sections` runs
  (outer call and the nested `FindApplicationId` call), then it is rejected by `HasExplicitCredentialArgs`
  before any Creatio-reaching call.
- [x] **AC-05** (PRD AC-09 / SM-03) — Given stdio or registered-environment `mcp-http`, when
  `list-app-sections` is called with `environment-name`, then behavior — including the nested lookup —
  matches the pre-change baseline exactly.
- [x] **AC-ERR** — **Error semantics respect the ENG-93208 middleware boundary (PRD AC-ERR).**
  (a) A **malformed/unusable** header is out of scope: the middleware returns HTTP 400 **before** any tool
  is invoked (`McpHttpServerCommand.cs:317`) — the tool is never entered and must not add handling for it.
  (b) Given a **valid** header whose target operation fails, when the tool executes, then it returns the
  typed error envelope with `SensitiveErrorTextRedactor`-redacted text.

## Implementation Notes

Depends on Story 2 (`IApplicationInfoService.FindApplicationId(EnvironmentSettings, string)`).

Add the settings-based `IApplicationSectionGetListService` overload (owned by THIS story, ADR slice 6g)
whose body replaces the name-based `FindApplicationId(environmentName, code)` call with the settings-based
one.

`ApplicationSectionGetListTool` derives from `BaseTool<EnvironmentOptions>(null, logger, commandResolver)` +
`ExecuteWithCleanLog(options, ...)` (`BaseTool.cs:63`).

**Also remove `[Required]` from `environment-name`** on the `ApplicationSectionGetListArgs` record —
schema-optional so a header-only passthrough call reaches the tool instead of being rejected at MCP binding
(PRD A-02 / FR-05a). Non-passthrough requiredness is enforced by the resolver's existing throw.

Key files: `clio/Command/ApplicationSectionGetListCommand.cs`,
`clio/Command/ApplicationInfoService.cs` (consume Story 2's overload),
`clio/Command/McpServer/Tools/ApplicationTool.cs`.
Pattern to follow: Story 8 (`delete-app-section`) shares the exact same nested-dependency shape — implement
both consistently (Story 8 merges immediately before this one in the serialized chain).

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | Header-only executes against header tenant (schema no longer rejects a blank `environment-name`); **separate, explicit test** for the nested `FindApplicationId` call being header-aware; mixed-input rejected end-to-end; registered-env/stdio unchanged | `clio.tests/Command/McpServer/ApplicationSectionGetListToolPassthroughTests.cs` |
| Integration `[Category("Integration")]` | none required | — |
| E2E `[Category("E2E")]` | Owned by **Story 15**: two-tenant isolation (newly routed tool) + stdio/`-e` no-regression | `clio.mcp.e2e/` (Story 15) |

Test naming: `MethodName_ShouldBehavior_WhenCondition`

## Definition of Done

- [x] All `CLIO*` diagnostics clean in changed files — including CLIO005 (FR-10)
- [x] Targeted tests green before commit: `dotnet test clio.tests/clio.tests.csproj --filter
  "Category=Unit&(Module=McpServer|Module=Command)" --no-build` (ADR slice 9)
- [x] All new/changed MCP arguments stay kebab-case (relaxing `[Required]` does not rename `environment-name`)
- [x] Unit tests added with `[Category("Unit")]` — never `[Category("UnitTests")]`
- [ ] PR description references this story file (no PR opened by this work order; left for the architect/PR author)

## Dev Agent Record

- Implementation started: 2026-07-11
- Implementation completed: 2026-07-11
- Tests passing: `dotnet test clio.tests/clio.tests.csproj -c Release -f net10.0 --filter "Category=Unit&(Module=McpServer|Module=Command)" --no-build` → Passed! Failed: 0, Passed: 4174, Skipped: 30, Total: 4204 (net10.0)
- Notes:
  - Added the settings-based `IApplicationSectionGetListService.GetSections(EnvironmentSettings, ApplicationSectionGetListRequest)` overload in `clio/Command/ApplicationSectionGetListCommand.cs`. Both public overloads converge on a private `GetSectionsCore` taking a `Func<InstalledAppSummary> findApplication` delegate; the name-based overload binds the name-based `FindApplicationId` call byte-for-byte (unchanged nested call), the settings-based overload binds the Story-2 settings-based `FindApplicationId(EnvironmentSettings, string)` overload.
  - `ApplicationSectionGetListTool` (`clio/Command/McpServer/Tools/ApplicationTool.cs`) reworked onto `BaseTool<EnvironmentOptions>(null, logger, commandResolver)` + `ExecuteWithCleanLog(options, ...)`, mirroring Story 8. Tenant is resolved via `IToolCommandResolver.Resolve<EnvironmentSettings>(options)` FIRST inside the heartbeat delegate, before the settings-based service call, so mixed header + environment-name input is rejected before any Creatio-reaching call (including the nested `FindApplicationId`). Read-only/idempotent MCP metadata (`ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false`) left unchanged.
  - `ApplicationSectionGetListArgs` (`clio/Command/McpServer/Tools/ApplicationToolArgs.cs`): removed `[Required]` from `environment-name`, made it `string?` with default `null`, and reordered the positional record parameters (`ApplicationCode` required-first; `EnvironmentName` optional-with-default-null second) since C# requires optional positional parameters to trail required ones. Verified no positional (non-named) call sites existed via `grep -rn "new ApplicationSectionGetListArgs("` — all three call sites (in `ApplicationToolTests.cs`) already used named arguments, so no positional breakage.
  - `ToolContractGetTool.cs` `BuildApplicationSectionGetList()`: dropped `environment-name` from the `Required` list and appended the "Required on stdio / registered-environment transports; omit under credential passthrough…" suffix to its description, matching Story 7/8's wording exactly.
  - Migrated `ApplicationToolTests.cs` (3 list-tool tests: 2 direct + 1 heartbeat-contract test) and `GetAppSectionsCommandTests.cs` (1 disambiguation fix) to the new ctor/settings-based mocks; added a 2-arg (`args`, `server`) test-invocation-extension overload for `ApplicationSectionGetList` in `ApplicationToolTestInvocationExtensions.cs`, mirroring `ApplicationSectionDelete`'s shape.
  - New dedicated file `clio.tests/Command/McpServer/ApplicationSectionGetListToolPassthroughTests.cs` (8 tests) covers AC-01/02 (header-only success), AC-03 (nested `FindApplicationId` header-routing, separate/explicit test), AC-04 (mixed-input end-to-end rejection before any Creatio-reaching call, including the nested site), AC-05 (registered-env/stdio unchanged), AC-02 runtime-requiredness (resolver throw surfaces as typed error outside passthrough), AC-ERR(b) (redacted error envelope), FR-05a reflection check (`[Required]` absent), and FR-05 tenant-lock-key test.
  - New service-level test file `clio.tests/Command/ApplicationSectionGetListServiceTests.cs` (no prior service-level test file existed for this service — command-level coverage lived only in `GetAppSectionsCommandTests.cs`): null-`EnvironmentSettings` guard (`ArgumentNullException` fail-fast before `CreateEnvironmentClient`) and settings-based-overload routing proof (never touches `ISettingsRepository`, routes nested `FindApplicationId` to the settings-based overload).
  - Added a `get-tool-contract` regression test (`ToolContractGetToolTests.cs`) asserting `environment-name` is absent from the `Required` list for `list-app-sections`, mirroring the existing `delete-app-section`/`update-app-section` contract tests.
  - `McpProfileGatingTests` (tools/list size-budget ratchet, 35328 bytes) verified: unlike Story 8's `delete-app-section`, `ApplicationSectionGetListTool` IS a member of `McpCoreToolProfile.CoreToolTypes` (`list-app-sections` is core), so its `[Description]` text DOES serialize into the flat `tools/list` payload. Kept the tool-method `[Description]` and args `[Description]` terse ("Optional under credential passthrough.") matching Stories 3-8's wording; `McpProfileGatingTests` (6 tests) passes unchanged at the existing 35328-byte ceiling with headroom to spare.
  - `spec/sprint-status.yaml` was already marked `in-progress` for this story before this work order started (pre-existing working-tree diff); left untouched per instruction.
  - Scope respected: only `list-app-sections` (+ its service, args, tool, contract entry, tests) touched. No other Story 10-14 tool (`get-user-culture`, `update-page`, `sync-pages`, `get-component-info`, `build-theme`) touched.
  - MCP prompts/resources reviewed: no prompt or resource references `list-app-sections`' argument shape or requiredness in a way that needed updating.
  - CLI docs (`help/en/list-app-sections.txt`, `docs/commands/list-app-sections.md`, `Commands.md`) reviewed — the CLI verb's behavior, options, and output are unchanged (only an internal service overload and the MCP tool surface changed), so no doc update required.
