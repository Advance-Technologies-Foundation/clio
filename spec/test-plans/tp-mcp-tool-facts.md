# Test Plan: MCP tool facts

**Feature**: mcp-tool-facts
**Stories**: [story-1](../stories/story-mcp-tool-facts-1.md), [story-2](../stories/story-mcp-tool-facts-2.md)
**Spec**: [spec-mcp-tool-facts.md](../prd/spec-mcp-tool-facts.md)
**ADR**: [adr-mcp-tool-facts.md](../adr/adr-mcp-tool-facts.md)
**Status**: Implemented
**Created**: 2026-11-14

## Scope

In scope: the facts catalog `Command/McpServer/Data/mcp-tool-facts.json` for every MCP tool, the reader, the
`tools/list` `_meta` publication, `get-tool-contract` `detail=facts`, the coverage guard, and the host-path facts
per argument (`host-paths`, `not-host-paths`).
Out of scope: where clio reads or writes when a path argument is omitted; a guide's minimum clio version.

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
| TC-U-11 | `McpToolFactsCoverageTests.EveryEnabledTool_ShouldDeclareCompleteAndConsistentFacts` (extended) | Every argument whose name reads like a path is declared a host path or, with why, not one; every declaration names an argument the tool takes, once. |
| TC-U-12 | `McpToolFactsCoverageTests.FactsRoutine_ShouldReportHostPathDefects_WhenRunOverSyntheticTools` | The routine reports an undeclared path-like argument, an unknown, doubled or contradictory declaration, an unknown alternative, a look-alike without its reason and a host path without its kind or access, and passes a correct tool. |
| TC-U-13 | `McpToolFactsSurfaceTests.ToJson_ShouldPublishHostPathsAndNotHostPaths_OnlyWhenDeclared` | The published shape of `host-paths` and `not-host-paths`; a tool with neither publishes what it did before. |
| TC-U-14 | `McpToolFactsSurfaceTests.GetToolContract_ShouldAnswerHostPaths_WhenDetailIsFacts` | `set-logo` `logo` is a file clio reads; `get-package-file` `file-path` is not a host path. |

## E2E tests (`clio.mcp.e2e`, `McpE2E.NoEnvironment`)

| ID | Test | Proves |
|---|---|---|
| TC-E-01 | `McpToolFactsE2ETests.ToolsList_Should_Carry_Facts_In_Meta_For_Every_Resident_Tool` | The real stdio server publishes `_meta` facts in `tools/list`. |
| TC-E-02 | `McpToolFactsE2ETests.GetToolContract_Should_Answer_Facts_For_Every_Reachable_Tool` | The real server answers `detail=facts` for every reachable tool. |
| TC-E-03 | `McpToolFactsE2ETests.GetToolContract_Should_Answer_Host_Paths_For_A_Tool_That_Names_Files` | The real server answers `host-paths` and `not-host-paths` under `detail=facts`. |
