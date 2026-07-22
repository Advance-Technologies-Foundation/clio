# Story 3: MCP Tool [Description] + Guidance Resources (Option C — agent-facing)

**Feature**: create-app-section-parallel-guard
**FR coverage**: FR-06, FR-07
**PRD**: [prd-create-app-section-parallel-guard.md](../prd/prd-create-app-section-parallel-guard.md)
**ADR**: [adr-create-app-section-parallel-guard.md](../adr/adr-create-app-section-parallel-guard.md)
**Jira**: ENG-93089 (JAC-3)
**Status**: ready-for-dev
**Size**: M (half day)
**Depends on**: story-create-app-section-parallel-guard-2 (needs the `contention` wire value / error-class to reference)
**Blocks**: none

---

## As a

AI no-code agent (MCP client) reading the `create-app-section` tool metadata and MCP guidance

## I want

the tool `[Description]` and both guidance guides to state that sections in one application must be created sequentially (not in parallel) and to list `contention` as a retryable error-class

## So that

I stop batching section-create calls in the first place and know to serialize/retry on `contention`

---

## Acceptance Criteria

- [ ] **AC-01** — Given the `create-app-section` MCP tool metadata, when inspected, then its `[Description]` states sections in one application must be created **sequentially, not in parallel**, and the `error-class` enumeration reads `(transport | creatio-timeout | server-error | contention)` (traces PRD AC-06 / FR-06).
- [ ] **AC-02** — Given the tool args `[Description]`, when inspected, then the detail-less-rejection sentence mentions `contention` / sequential-only and the "clio serializes in-process and auto-retries once with verification — do not manually blast parallel create-app-section calls" note (traces FR-06).
- [ ] **AC-03** — Given `AppModelingGuidanceResource`, when inspected, then its `create-app-section` error-class bullet includes `contention` (retryable — serialize/retry) and it carries a "create sections in one app sequentially, not in parallel" guardrail bullet (traces PRD AC-06 / FR-07).
- [ ] **AC-04** — Given `ExistingAppMaintenanceGuidanceResource`, when inspected, then its error-class bullet includes `contention` and the sequential-only note (traces PRD AC-06 / FR-07).
- [ ] **AC-05** — Given the routing map, when reviewed, then it is confirmed unaffected (no guide added/renamed) and the change summary states "MCP reviewed: routing map unaffected".
- [ ] **AC-ERR** — Given content tests over the tool `[Description]` and both guidance guides, when they run, then they assert the sequential-only text and the `contention` error-class are present (pass/fail).

## Implementation Notes

Docs-agent-facing MCP surface only. No behavior change; no new guide (routing map unaffected). Descriptions are inline `[Description]` attributes — there is no `McpToolDescriptions.cs`.

Key file: `clio/Command/McpServer/Tools/ApplicationTool.cs`
- Update the `ApplicationSectionCreate` tool `[Description]`: add the sequential-only constraint; add `contention` to the `error-class` enumeration `(transport | creatio-timeout | server-error | contention)`; add the in-process-serialize + auto-retry-once-with-verify note ("do not manually blast parallel create-app-section calls").
- Update the args `[Description]` detail-less-rejection sentence to mention contention / sequential.

Key file: `clio/Command/McpServer/Resources/AppModelingGuidanceResource.cs`
- Update the `create-app-section` error-class bullet (~line 92) to include `contention` (retryable — serialize/retry); add a sequential-only guardrail bullet.

Key file: `clio/Command/McpServer/Resources/ExistingAppMaintenanceGuidanceResource.cs`
- Same edits on the error-class bullet (~line 51): add `contention` + sequential-only note.

Pattern to follow: existing content-test fixture in `clio.tests/Command/McpServer/*` that asserts description/guidance text.

Use the `$create-mcp-tool` skill for the tool/resource edits and the `$test-mcp-tool` skill for the content tests, per repo policy.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit (content) `[Category("Unit")]` | Tool `[Description]` contains sequential-only + `contention` in error-class enum (AC-01/AC-02); both guidance guides contain `contention` + sequential-only (AC-03/AC-04) | `clio.tests/Command/McpServer/*` (existing description/guidance content-test fixture) |

Test naming: `MethodName_ShouldExpectedBehavior_WhenCondition`. AAA + `because` on every assertion + `[Description]` on every test.

## Definition of Done

- [ ] Code compiles without Roslyn analyzer warnings (CLIO001-CLIO004)
- [ ] Tool `[Description]` + both guidance guides updated; routing map confirmed unaffected (stated in change summary)
- [ ] No behavior/flag change; MCP envelope shape unchanged
- [ ] Content tests added with `[Category("Unit")]` — never `[Category("UnitTests")]`
- [ ] Regression filter run: `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=McpServer" --no-build`
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
