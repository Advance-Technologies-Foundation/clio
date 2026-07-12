# Story 7: MCP Guidance — Detect-once / Reuse / Ask-on-failure

**Feature**: user-profile-language-detection
**FR coverage**: FR-07, FR-08, FR-09
**AC coverage**: AC-05, AC-07
**PRD**: [prd-user-profile-language-detection.md](../prd/prd-user-profile-language-detection.md)
**ADR**: [adr-user-profile-language-detection.md](../adr/adr-user-profile-language-detection.md)
**Status**: ready-for-dev
**Size**: M (half day)
**Depends on**: story-user-profile-language-detection-3
**Blocks**: none

---

## As a

AI agent (MCP)

## I want

clear MCP guidance to detect the profile language once per session, reuse it for subsequent creations, and ask the user when it cannot be retrieved

## So that

I never silently create entities in the wrong language and never redundantly re-detect

---

## Acceptance Criteria

- [ ] **AC-07** — Given the MCP server initializes, when a client reads server instructions, the `app-modeling` resource, and the entity/page/section/application prompts, then ALL FOUR families contain the detect-once / reuse / ask-on-failure profile-language guidance (asserted by `clio.mcp.e2e`).
- [ ] **AC-05 (guidance)** — Given an MCP session, when multiple entities are created in sequence, then the guidance instructs the agent to detect the profile language at most once and reuse it (complementary to the server-side singleton cache from Story 1).
- [ ] **AC-04 (guidance)** — Given a `success:false` result from `get-user-culture`, when the agent is about to create an entity, then the guidance instructs it to explicitly ask the user which language to use and NOT fall back to host locale or a silent `en-US`.
- [ ] **SM-03** — Given the guidance change, when verified, then 4/4 prompt families are updated and asserted by e2e.

## Implementation Notes

References the `get-user-culture` MCP tool contract from Story 3 (hence the dependency edge Story 3 → 7).

Files to modify:
- `clio/Command/McpServer/McpServerInstructions.cs` — add the detect-once / reuse / ask-on-failure profile-language rule (FR-07).
- `clio/Command/McpServer/Resources/AppModelingGuidanceResource.cs` — document detect-once / reuse / ask-on-failure (FR-08).
- `clio/Command/McpServer/Prompts/EntitySchemaPrompt.cs` — add profile-language detection guidance (FR-09).
- `clio/Command/McpServer/Prompts/PagePrompt.cs` — same (FR-09).
- section prompt (the section/`ApplicationSectionCreate` prompt family) — same (FR-09).
- `clio/Command/McpServer/Prompts/ApplicationPrompt.cs` — same (FR-09).

Guidance text must instruct: call `get-user-culture` once per session; reuse the result; on `success:false` ask the user which language to use; never silently default to host locale / `en-US`.

Use the `create-mcp-tool` skill for prompt/resource edits and `test-mcp-tool` for the assertion tests (CLAUDE.md MCP policy).

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | server instructions contain detect-once/reuse/ask-on-failure text | `clio.tests/Command/McpServer/McpServerInstructionsTests.cs` |
| Unit `[Category("Unit")]` | `app-modeling` guidance resource contains the rule | `clio.tests/Command/McpServer/AppModelingGuidanceResourceTests.cs` |
| Unit `[Category("Unit")]` | entity/page/section/application prompts each contain the guidance (4 families) | `clio.tests/Command/McpServer/*PromptTests.cs` |
| E2E `[Category("E2E")]` | real `mcp-server`: instructions + resource + 4 prompts all expose the guidance (AC-07) — manual, not in CI | `clio.mcp.e2e/GetUserCultureToolE2ETests.cs` (guidance assertions) |

String/content assertions; AAA + `because` + `[Description]`.
Test naming: `GetInstructions_ShouldContainDetectOnceGuidance_WhenServerInitializes`, `GetPrompt_ShouldContainAskOnFailureGuidance_WhenEntityPromptRequested`.

## Definition of Done

- [ ] Code compiles without Roslyn analyzer warnings (CLIO001-CLIO004)
- [ ] All four prompt families + instructions + `app-modeling` resource contain detect-once/reuse/ask-on-failure (SM-03: 4/4)
- [ ] Guidance forbids silent host-locale / `en-US` fallback (FR-06 / AC-04)
- [ ] Guidance references the `get-user-culture` tool contract from Story 3
- [ ] Unit tests assert the guidance text in each family with `[Category("Unit")]`
- [ ] `clio.mcp.e2e` guidance assertions added (AC-07) — manual, flagged not-in-CI
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
