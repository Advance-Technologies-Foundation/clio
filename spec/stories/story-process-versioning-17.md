# Story 17: Expose set-active-business-process-version through MCP

**Feature**: process-versioning
**Jira**: ENG-94374
**FR coverage**: FR-08, FR-09, FR-14
**PRD**: [prd-process-versioning.md](../prd/prd-process-versioning.md)
**ADR**: [adr-process-versioning.md](../adr/adr-process-versioning.md)
**Test plan**: [tp-process-versioning.md](../test-plans/tp-process-versioning.md)
**Repository**: clio
**Status**: ready-for-dev
**Size**: M

---

## As a

developer (AI agent acting for a no-code builder)

## I want

an MCP tool that makes a chosen version actual

## So that

rollback is one explicit, auditable gesture

---

## Acceptance Criteria

- [ ] **AC-01** — Given a version identity, when the tool runs, then it reports the active version read back after the write
- [ ] **AC-02** — Given the read-back does not match, when the tool returns, then it fails and names the version that is actually active
- [ ] **AC-03** — Given the tool metadata, when it is reflected, then it is `ReadOnly=false, Destructive=true, Idempotent=true, OpenWorld=false`
- [ ] **AC-04** — Given the tool description, when it is read, then it states that activation affects only NEW instances, that running instances stay on their version, that the UI calls this the actual version, and that deleting a version does not exist
- [ ] **AC-ERR** — Given neither `version-name` nor `version-uid` is supplied, or both are, when the tool runs, then the MCP result is `success:false` with a message naming the violation

## Implementation Notes

New: `SetActiveProcessVersionTool.cs`, `SetActiveProcessVersionCommand.cs` + options. Same two easily-missed edits as story 16: a `KnownRoute` value with its leading-slash route string, and a `BindingsModule` registration.
The versioned `[RequiresPackage]` floor is declared here, at or below the version story 15 bundled.
Destructive ⇒ it owns its own timeout contract and must NOT route through the 120 s read deadline (`clio/Command/McpServer/AGENTS.md`).
`Idempotent=true` is deliberate: setting the same version active twice yields the same state.
The same two counted pins as story 16 move with this tool.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | arg validation, flag pins, read-back mismatch surfaced, package-floor refusal, route resolution | `clio.tests/Command/McpServer/SetActiveProcessVersionToolTests.cs` |
| E2E `[Category("E2E")]` | activate on a dedicated sandbox behind the destructive opt-in | `clio.mcp.e2e/SetActiveProcessVersionToolE2ETests.cs` |

Test naming: `MethodName_ShouldBehavior_WhenCondition`

## Definition of Done

- [ ] `PassthroughToolClassificationRegistry` row and a `McpCoreToolProfile` decision
- [ ] `install-process-builder` md + txt and `docs/McpCapabilityMap.md` list the tool
- [ ] The two counted pins moved or explicitly ruled out
- [ ] ClioRing MCP compatibility verdict recorded (`AGENTS.md:241-299`)
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
