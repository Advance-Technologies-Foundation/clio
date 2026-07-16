# Story 4: `get-app-info` — route through the resolver (class c1)

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

`get-app-info` (`ApplicationGetInfoTool`) to execute against the header-supplied tenant under authorized
credential passthrough

## So that

the gateway can look up application metadata without registering an environment first

---

## Merge order (shared-file constraint)

Stories 3-9 all modify `clio/Command/McpServer/Tools/ApplicationTool.cs` (and 3-5 also
`ApplicationToolArgs.cs`), so they are **serialized: 3 → 4 → 5 → 6 → 7 → 8 → 9** via `depends_on`. This
story starts only after **Story 3** merges.

## Acceptance Criteria

- [ ] **AC-01** (PRD AC-02, decision-matrix "Route") — Given authorized passthrough and no
  `environment-name`, when `get-app-info` runs, then it returns the tenant's application info — never
  `Environment name is required.`.
- [ ] **AC-02** — **Conditional requiredness (FR-05a) — blocking prerequisite for AC-01.** Given authorized
  passthrough, when `get-app-info` is called with no `environment-name`, then the MCP schema does **not**
  reject the call at pre-tool binding — `[Required]` is removed from `environment-name` on the corresponding
  `ApplicationGetInfoArgs` record (ADR "CLI flag specification" table), making it schema-optional. On
  non-passthrough transports, runtime requiredness is enforced by the existing
  `IToolCommandResolver.ResolveSettingsAndKey`'s `EnvironmentResolutionException` throw (ADR OQ-03,
  "Resolver-ROUTED tools").
- [ ] **AC-03** — **Mixed input (PRD AC-06).** Given authorized passthrough with both the header and an
  explicit `environment-name` naming a different registered environment, when `get-app-info` runs, then it
  is rejected by `HasExplicitCredentialArgs` before any Creatio-reaching call.
- [ ] **AC-04** (PRD AC-09 / SM-03) — Given stdio or registered-environment `mcp-http`, when `get-app-info`
  is called with `environment-name`, then behavior matches the pre-change baseline exactly.
- [ ] **AC-ERR** — **Error semantics respect the ENG-93208 middleware boundary (PRD AC-ERR).**
  (a) A **malformed/unusable** header is out of scope: the middleware returns HTTP 400 **before** any tool
  is invoked (`McpHttpServerCommand.cs:317`) — the tool is never entered and must not add handling for it.
  (b) Given a **valid** header whose target operation fails, when the tool executes, then it returns the
  typed error envelope with `SensitiveErrorTextRedactor`-redacted text — no secret material leaks.

## Implementation Notes

Uses Story 2's `IApplicationInfoService.GetApplicationInfo(EnvironmentSettings, string?, string?)` overload
directly — this tool has no nested dependency beyond that one call (unlike `create-app`/`create-app-section`
/`update-app-section`, which additionally nest a caption-culture resolution).

`ApplicationGetInfoTool` derives from `BaseTool<EnvironmentOptions>(null, logger, commandResolver)` and
wraps its body in `ExecuteWithCleanLog(options, () => {...})` — the options-aware overload
(`clio/Command/McpServer/Tools/BaseTool.cs:63`), same shape as Story 3.

**Also remove `[Required]` from `environment-name`** on the `ApplicationGetInfoArgs` record — schema-optional
so a header-only passthrough call reaches the tool instead of being rejected at MCP binding (PRD A-02 /
FR-05a). Non-passthrough requiredness is enforced by the resolver's existing throw, not a new check.

Key files: `clio/Command/ApplicationInfoService.cs`,
`clio/Command/McpServer/Tools/ApplicationTool.cs`, `clio/Command/McpServer/Tools/ApplicationToolArgs.cs`.
Pattern to follow: Story 3 (`list-apps`) — identical wiring shape, single dependency instead of a list
service.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | Header-only executes against header tenant (schema no longer rejects a blank `environment-name`); mixed-input rejected; registered-env/stdio unchanged | `clio.tests/Command/McpServer/ApplicationGetInfoToolPassthroughTests.cs` |
| Integration `[Category("Integration")]` | none required | — |
| E2E `[Category("E2E")]` | Owned by **Story 15**: two-tenant isolation (this tool is newly routed → mandatory per ADR) + stdio/`-e` no-regression. Manual only — MCP e2e not in CI | `clio.mcp.e2e/` (Story 15) |

Test naming: `MethodName_ShouldBehavior_WhenCondition`

## Definition of Done

- [x] All `CLIO*` diagnostics clean in changed files — including CLIO005 (FR-10)
- [x] Targeted tests green before commit: `dotnet test clio.tests/clio.tests.csproj --filter
  "Category=Unit&(Module=McpServer|Module=Command)" --no-build` (ADR slice 9)
- [x] All new/changed MCP arguments stay kebab-case (relaxing `[Required]` does not rename `environment-name`)
- [x] Unit tests added with `[Category("Unit")]` — never `[Category("UnitTests")]`
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started: 2026-07-11
- Implementation completed: 2026-07-11
- Tests passing: `Category=Unit&Module=McpServer` → 2049 passed / 0 failed / 1 skipped;
  `Category=Unit&Module=Command` → 2066 passed / 0 failed / 29 skipped (net10.0)
- Notes:
  - `ApplicationGetInfoTool` now derives from `BaseTool<EnvironmentOptions>(null, logger,
    commandResolver)`; the Story-2 settings-based `IApplicationInfoService.GetApplicationInfo`
    overload is consumed as-is (no service changes). The options-aware
    `ExecuteWithCleanLog(options, ...)` (FR-05 per-tenant lock + in-flight guard) runs INSIDE the
    `McpProgressHeartbeat` work delegate so the tenant lock is held on the worker thread for the
    whole synchronous backend call and never across an await — the heartbeat/deadline behavior and
    the exactly-one-of id/code validation are unchanged.
  - `[Required]` removed from `ApplicationGetInfoArgs.EnvironmentName` (FR-05a); the parameter
    became nullable-with-default, so no reordering was needed (all following parameters already
    have defaults) and all call sites use named arguments. The curated `get-tool-contract` entry
    for `get-app-info` was aligned (required list emptied, conditional-requiredness description).
  - **`tools/list` size ratchet (34 KiB, `McpProfileGatingTests`) is effectively exhausted.**
    Story 3's exact parameter-description wording ("required unless credential passthrough supplies
    the tenant") tripped the ratchet by 25 bytes; the limit was NOT raised — instead this story's
    method-parameter description uses the terser "required unless passthrough" (the FR-05a args-record
    suffix "Optional under credential passthrough." is verbatim per the pattern). Remaining headroom
    is ~7 bytes: **Stories 5-9 cannot add any tools/list-visible text without an explicit decision
    to raise the ratchet.**
  - E2E (`clio.mcp.e2e`) coverage: owned by Story 15 per the test table; existing
    `ApplicationToolE2ETests` exercise the unchanged registered-env path.
