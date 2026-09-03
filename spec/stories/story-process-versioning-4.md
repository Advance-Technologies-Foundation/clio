# Story 4: State the version contract on the MCP surface

**Feature**: process-versioning
**Jira**: ENG-94374
**FR coverage**: FR-01, FR-02, FR-12, FR-19
**PRD**: [prd-process-versioning.md](../prd/prd-process-versioning.md)
**ADR**: [adr-process-versioning.md](../adr/adr-process-versioning.md)
**Test plan**: [tp-process-versioning.md](../test-plans/tp-process-versioning.md)
**Repository**: clio
**Status**: ready-for-dev
**Size**: S

---

## As a

developer

## I want

the process tools to say which version they read or start

## So that

an agent stops silently conflating a named version with the running one

---

## Acceptance Criteria

- [ ] **AC-01** — Given `describe-business-process`, when its description is read, then it names the version members, states that `process-name` targets one specific version rather than the running one, and states that the active-version answer comes from the process library
- [ ] **AC-02** — Given `run-process`, when its description is read, then it states that it starts the ACTIVE version, not the named one
- [ ] **AC-03** — Given the describe prompt, when it is read, then it instructs reading `isActiveVersion` first and re-describing `activeVersionName` when it is false
- [ ] **AC-04** — Given `docs/McpCapabilityMap.md`, when the describe row is read, then it lists the version members and the by-name caveat
- [ ] **AC-ERR** — Given the description names a guidance article, when `WorkspaceTemplateGuidanceDriftTests` runs, then the named article is present in `curated-knowledge-names.json` and is not feature-gated

## Implementation Notes

Files: `DescribeProcessTool.cs` (`[Description]` is one 7437-character line at `:28`), `RunProcessTool.cs`, `DescribeProcessPrompt.cs` (`:31-37`), `docs/McpCapabilityMap.md:744`.
Insert the version paragraph BEFORE the sentence "Identify the process by exactly one of process-name / process-uid / process-caption." — it qualifies exactly that sentence.
Do NOT restore `[FeatureToggle("process-designer")]`: its absence is pinned by `ProcessDesignerGoLiveTests.cs:62`.
No four-part version literals in the description — `BundledProcessBuilderPackageTests.cs:885` polices them.
The version paragraph names **no** guidance article, so this story does not depend on the guidance stories (6/7); if that changes, add the dependency, because `WorkspaceTemplateGuidanceDriftTests.cs:537` resolves every article an ungated description names.
The description carries three counted claims ("with ONE exception", "with four exceptions", "the one exception being") — keep them consistent.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | the four tool-flag reflection pins still pass; new description tokens asserted | `clio.tests/Command/McpServer/DescribeProcessToolTests.cs` |
| Unit `[Category("Unit")]` | guidance-name drift over the ungated tool descriptions | `clio.tests/Command/McpServer/WorkspaceTemplateGuidanceDriftTests.cs` |

Test naming: `MethodName_ShouldBehavior_WhenCondition`

## Definition of Done

- [ ] `docs/McpCapabilityMap.md` edited in the same commit as the tool description (no automated guard on that file)
- [ ] Read-deadline check: describe stays `ReadOnly=true`; the version read is projected and measured
- [ ] Code compiles without Roslyn analyzer warnings
- [ ] All new tool names and flags are kebab-case
- [ ] Unit tests use `[Category("Unit")]` — never `[Category("UnitTests")]`
- [ ] No `catch (Exception)` added to clio code paths
- [ ] Knowledge records whose `applies-to` names a file this story touches are updated or deleted **in this PR** (`AGENTS.md:456-457`)
- [ ] Docs verdict stated explicitly in the PR body, including "no update required" where that is the verdict
- [ ] MCP verdict stated in the PR body ("MCP reviewed, no update required" where that applies)
- [ ] PR description references this story file

## Dev Agent Record

{Left blank — filled by dev agent during implementation}
- Implementation started: 
- Implementation completed: 
- Tests passing: 
- Notes: 
