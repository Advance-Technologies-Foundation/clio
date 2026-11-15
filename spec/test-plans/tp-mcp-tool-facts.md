# Test Plan: MCP tool facts

**Feature**: mcp-tool-facts
**Stories**: [story-1](../stories/story-mcp-tool-facts-1.md)
**Spec**: [spec-mcp-tool-facts.md](../prd/spec-mcp-tool-facts.md)
**ADR**: [adr-mcp-tool-facts.md](../adr/adr-mcp-tool-facts.md)
**Status**: Implemented
**Created**: 2026-11-14

## Scope

In scope: the facts catalog `Command/McpServer/Data/mcp-tool-facts.json` for every MCP tool, the reader, the
`tools/list` `_meta` publication, `get-tool-contract` `detail=facts`, and the coverage guard.
Out of scope: host-path facts per argument; a guide's minimum clio version.

## Unit tests

| ID | Test | Proves |
|---|---|---|
| TC-U-01 | `McpToolFactsCoverageTests.EveryEnabledTool_ShouldDeclareCompleteAndConsistentFacts` | Every enabled tool declares all four facts; they agree with its hints; entries name its own arguments and only narrow. |
| TC-U-02 | `McpToolFactsCoverageTests.FactsRoutine_ShouldReportEachKindOfDefect_WhenRunOverSyntheticTools` | The routine reports a missing, partial, hint-contradicting and over-wide declaration and passes a correct one. |
| TC-U-03 | `McpToolFactsCoverageTests.MethodLevelAliases_ShouldDeclareTheFactsOfTheirCanonicalTool` | A method-level alias declares its canonical tool's facts. |
| TC-U-04 | `McpToolFactsSurfaceTests.ToJson_ShouldPublishTheFourFieldsAndTheResolvedArgumentEntries` | The published JSON shape, field names and values. |
| TC-U-05 | `McpToolFactsSurfaceTests.Read_ShouldResolveAnArgumentEntrysUnspecifiedFieldsFromTheTool` | An entry's unspecified field takes the tool's value. |
| TC-U-06 | `McpToolFactsSurfaceTests.ToolsList_ShouldCarryEachResidentToolsFactsInMeta` | Every resident tool carries its declared facts in `_meta`. |
| TC-U-07 | `McpToolFactsSurfaceTests.GetToolContract_ShouldAnswerEveryServedToolsFacts_WhenDetailIsFacts` | `detail=facts` answers every indexed tool, get-tool-contract and aliases. |
| TC-U-08 | `McpToolFactsSurfaceTests.GetToolContract_ShouldAnswerTheNamedToolsFactsAndReportMisses_WhenDetailIsFactsWithNames` | Named lookup, `manage-role` `action=list` is a read, misses in `not-found`. |
| TC-U-09 | `McpToolFactsSurfaceTests.GetToolContract_ShouldLeaveTheIndexWithoutFacts_WhenDetailIsNotFacts` | The agent-facing answers carry no facts. |
| TC-U-10 | `ToolContractPayloadBudgetTests` (existing) | Every payload budget holds unchanged. |

## E2E tests (`clio.mcp.e2e`, `McpE2E.NoEnvironment`)

| ID | Test | Proves |
|---|---|---|
| TC-E-01 | `McpToolFactsE2ETests.ToolsList_Should_Carry_Facts_In_Meta_For_Every_Resident_Tool` | The real stdio server publishes `_meta` facts in `tools/list`. |
| TC-E-02 | `McpToolFactsE2ETests.GetToolContract_Should_Answer_Facts_For_Every_Reachable_Tool` | The real server answers `detail=facts` for every reachable tool. |
