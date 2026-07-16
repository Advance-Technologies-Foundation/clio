# Story 7: `update-app-section` — route through the resolver, including nested caption-culture and app-info calls (class c1)

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

`update-app-section` (`ApplicationSectionUpdateTool`) to execute against the header-supplied tenant on every
call it makes — including the nested caption-culture resolution and the nested `GetApplicationInfo` call

## So that

updating a section under passthrough closes the same class of nested active-tenant leak found in
`create-app`/`create-app-section`

---

## Merge order (shared-file constraint)

Stories 3-9 all modify `clio/Command/McpServer/Tools/ApplicationTool.cs`, so they are **serialized:
3 → 4 → 5 → 6 → 7 → 8 → 9** via `depends_on`. This story starts only after **Story 6** merges.

## Acceptance Criteria

- [x] **AC-01** (PRD AC-02, decision-matrix "Route — full nested graph") — Given authorized passthrough and
  no `environment-name`, when `update-app-section` runs, then the section is updated against the header
  tenant — never `Environment name is required.`.
- [x] **AC-02** — **Conditional requiredness (FR-05a) — blocking prerequisite for AC-01.** Given authorized
  passthrough, when `update-app-section` is called with no `environment-name`, then the MCP schema does
  **not** reject the call at pre-tool binding — `[Required]` is removed from `environment-name` on the
  corresponding `ApplicationSectionUpdateArgs` record (ADR "CLI flag specification" table), making it
  schema-optional. On non-passthrough transports, runtime requiredness is enforced by the existing
  `IToolCommandResolver.ResolveSettingsAndKey`'s `EnvironmentResolutionException` throw (ADR OQ-03,
  "Resolver-ROUTED tools").
- [x] **AC-03** — **Nested caption-culture path.** Given the same setup, when
  `ApplicationSectionUpdateCommand`'s caption-culture call (today `:87`) runs, then it uses the
  settings-based `ICaptionCultureResolver.Resolve(EnvironmentSettings, ...)` overload and resolves the
  **header** tenant's culture — never the configured active/registered environment's culture.
- [x] **AC-04** — **Nested `GetApplicationInfo` path.** Given the same setup, when the
  `GetApplicationInfo(environmentName, ...)` nested call (today `:93`) runs, then it uses the settings-based
  overload against the header tenant.
- [x] **AC-05** — **Mixed input (PRD AC-06).** Given authorized passthrough with both the header and an
  explicit `environment-name` naming a different registered environment, when `update-app-section` runs
  (outer call and both nested calls), then it is rejected by `HasExplicitCredentialArgs` before any
  Creatio-reaching call.
- [x] **AC-06** (PRD AC-09 / SM-03) — Given stdio or registered-environment `mcp-http`, when
  `update-app-section` is called with `environment-name`, then behavior — including the nested calls —
  matches the pre-change baseline exactly.
- [x] **AC-ERR** — **Error semantics respect the ENG-93208 middleware boundary (PRD AC-ERR).**
  (a) A **malformed/unusable** header is out of scope: the middleware returns HTTP 400 **before** any tool
  is invoked (`McpHttpServerCommand.cs:317`) — the tool is never entered and must not add handling for it.
  (b) Given a **valid** header whose target operation fails, when the tool executes, then it returns the
  typed error envelope with `SensitiveErrorTextRedactor`-redacted text.

## Implementation Notes

Depends on Story 2 (`ICaptionCultureResolver` + `IApplicationInfoService` settings-based overloads).

Add the settings-based `IApplicationSectionUpdateService` overload (owned by THIS story, ADR slice 6f).
`ApplicationSectionUpdateCommand` repeats the caption-culture-by-name call (`:87`) and the
`GetApplicationInfo(environmentName, ...)` nested call (`:93`) — both must be replaced with the
settings-based overloads from Story 2, not just the outer client-construction call (ADR verification #4,
decision matrix row).

`ApplicationSectionUpdateTool` derives from `BaseTool<EnvironmentOptions>(null, logger, commandResolver)` +
`ExecuteWithCleanLog(options, ...)` (`BaseTool.cs:63`).

**Also remove `[Required]` from `environment-name`** on the `ApplicationSectionUpdateArgs` record —
schema-optional so a header-only passthrough call reaches the tool instead of being rejected at MCP binding
(PRD A-02 / FR-05a). Non-passthrough requiredness is enforced by the resolver's existing throw.

Key files: `clio/Command/ApplicationSectionUpdateCommand.cs`,
`clio/Command/EntitySchemaDesigner/CaptionCultureResolver.cs` (consume Story 2's overload),
`clio/Command/ApplicationInfoService.cs` (consume Story 2's overload),
`clio/Command/McpServer/Tools/ApplicationTool.cs`.
Pattern to follow: Story 5/6 — same nested-call-graph discipline, single call site each here (vs. doubled in
`create-app-section`).

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | Header-only executes against header tenant (outer call, schema no longer rejects a blank `environment-name`); **separate, explicit test** for the nested caption-culture call; **separate, explicit test** for the nested `GetApplicationInfo` call; mixed-input rejected end-to-end; registered-env/stdio unchanged | `clio.tests/Command/McpServer/ApplicationSectionUpdateToolPassthroughTests.cs` |
| Integration `[Category("Integration")]` | none required | — |
| E2E `[Category("E2E")]` | Owned by **Story 15**: two-tenant isolation (newly routed tool) + stdio/`-e` no-regression; the FR-08 "one section tool" multi-tenant case is carried by `create-app-section` there | `clio.mcp.e2e/` (Story 15) |

Test naming: `MethodName_ShouldBehavior_WhenCondition`

## Definition of Done

- [x] All `CLIO*` diagnostics clean in changed files — including CLIO005 (FR-10)
- [x] Targeted tests green before commit: `dotnet test clio.tests/clio.tests.csproj --filter
  "Category=Unit&(Module=McpServer|Module=Command)" --no-build` (ADR slice 9)
- [x] All new/changed MCP arguments stay kebab-case (relaxing `[Required]` does not rename `environment-name`)
- [x] Unit tests added with `[Category("Unit")]` — never `[Category("UnitTests")]`
- [ ] PR description references this story file (no PR opened by this dev-agent pass; changes left in the working tree per work order)

## Dev Agent Record

- Implementation started: 2026-07-11
- Implementation completed: 2026-07-11
- Tests passing: `dotnet test clio.tests/clio.tests.csproj -c Release -f net10.0 --filter
  "Category=Unit&(Module=McpServer|Module=Command)" --no-build` — 4152 passed, 0 failed, 30 skipped
  (net10.0). 0 new `CLIO*` diagnostics (build of `clio` and `clio.tests` both clean).
- Notes:
  - `IApplicationSectionUpdateService` gained a settings-based `UpdateSection(EnvironmentSettings, ...)`
    overload. Both the name-based and settings-based public overloads converge on a private
    `UpdateSectionCore` that takes `Func<string>`/`Func<ApplicationInfoResult>` delegates for the
    profile-culture resolution (`:87`) and the `GetApplicationInfo` read (`:93`) — simpler than story 6's
    `Func<string?,string>`/`Func<string?,string?,ApplicationInfoResult>` shape because each nested call
    site here is invoked exactly once with fixed arguments (single-site, per the work order), unlike
    story 6's doubled/reused sites.
  - `ApplicationSectionUpdateTool` reworked onto `BaseTool<EnvironmentOptions>(null, logger,
    commandResolver)` + `ExecuteWithCleanLog(options, ...)`, matching stories 5/6. Tenant is resolved
    FIRST inside the heartbeat work delegate so mixed header + `environment-name` input is rejected
    before any Creatio-reaching call (AC-05), including both nested sites.
  - `ApplicationSectionUpdateArgs.EnvironmentName` lost `[Required]` and moved after the required
    `application-code`/`section-code` parameters (C# optional-parameter ordering); all call sites already
    used named arguments so no positional-argument breakage was found.
  - `ToolContractGetTool`'s `BuildApplicationSectionUpdate` contract updated to drop `environment-name`
    from the required-field list and append the same "omit under credential passthrough" description
    suffix used by stories 3-6.
  - `update-app-section` is not a member of `McpCoreToolProfile.CoreToolTypes` (the lazy/core tool
    surface), so the `tools/list` byte-budget ratchet in `McpProfileGatingTests`
    (`MaxLazyToolsSerializedBytes`) is unaffected by this story's description/schema changes.
  - Migrated `UpdateAppSectionCommandTests` (added an `EnvironmentSettings` overload disambiguation for
    the `DidNotReceiveWithAnyArgs()` assertion — NSubstitute CS0121 gotcha) and the four
    `ApplicationSectionUpdate*` tests in `ApplicationToolTests.cs` onto the new 3-arg tool constructor
    and settings-based service stub.
  - Added `clio.tests/Command/McpServer/ApplicationSectionUpdateToolPassthroughTests.cs` (9 tests) driving
    a REAL `ApplicationSectionUpdateService` so both nested call sites are proven end-to-end, and
    extended `clio.tests/Command/ApplicationSectionUpdateServiceTests.cs` with a null-`EnvironmentSettings`
    guard test and a settings-based routing test (mirrors `ApplicationSectionCreateServiceTests` from
    story 6).
  - Scope guard respected: only `update-app-section` (`ApplicationSectionUpdateCommand.cs`,
    `ApplicationSectionUpdateTool`, its args record, and its `ToolContractGetTool` entry) was touched.
    `delete-app-section` / `list-app-sections` (stories 8-9) were left untouched — confirmed their shared
    `[EnvironmentNameFieldName, ApplicationCodeFieldName, SectionCodeFieldName]` contract literal in
    `ToolContractGetTool.cs` still exists verbatim for `delete-app-section`.
  - E2E coverage for this tool is explicitly owned by Story 15 per the Test Requirements table; no
    `clio.mcp.e2e` changes were made in this pass.
