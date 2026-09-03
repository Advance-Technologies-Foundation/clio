# Story 5: Prove the version read end to end against a stand

**Feature**: process-versioning
**Jira**: ENG-94374
**FR coverage**: FR-01, FR-02, FR-04
**PRD**: [prd-process-versioning.md](../prd/prd-process-versioning.md)
**ADR**: [adr-process-versioning.md](../adr/adr-process-versioning.md)
**Test plan**: [tp-process-versioning.md](../test-plans/tp-process-versioning.md)
**Repository**: clio
**Status**: ready-for-dev
**Size**: S

---

## As a

QA engineer

## I want

MCP E2E cases that describe a real versioned family

## So that

the version members are proven against a live stand and not only against mocks

---

## Acceptance Criteria

- [ ] **AC-01** — Given a stand carrying `CrtProcessBuilder`, when describe runs against an unversioned process, then `version` is 0 and `versions[]` has one root entry
- [ ] **AC-02** — Given the stock `InvoiceVisaProcess` family, when describe runs against the root by name, then `isActiveVersion` is false and the active version is named
- [ ] **AC-03** — Given the same family, when describe runs against the active version by UId, then `isActiveVersion` is true and `versions[]` lists both members ascending
- [ ] **AC-ERR** — Given the configured sandbox environment is unreachable or unset, when the fixture runs, then it self-ignores with a message naming the setting, as the existing cases do

## Implementation Notes

File: `clio.mcp.e2e/DescribeProcessToolE2ETests.cs` — extend the existing fixture.
No seeding: 12 versioned families ship with the product; `InvoiceVisaProcess` / `InvoiceVisaProcessInvoice1` (package `Invoice`) differ observably — 3 vs 7 parameters.
Keep `[TestFixture]`, `[AllureNUnit]`, `[AllureFeature]`, `[NonParallelizable]`, `[Category(McpE2ECategories.ProcessDesigner)]` (`:23-27`). `ProcessDesignerE2EGate` was deleted — the old category constant does not compile and `SkipIfFeatureDisabled` must NOT come back (`McpE2ECategories.cs:16-20`).
Add `[AllureTag]` naming the tool, `[AllureName]` and `[AllureDescription]` — all three required by `clio.mcp.e2e/AGENTS.md:90`, and currently missing. Never `[AllureStep]` on an async helper.
Point the run at an environment with `McpE2E__Sandbox__EnvironmentName=<env>` rather than editing `appsettings.json`.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| E2E `[Category("E2E")]` | the three cases above | `clio.mcp.e2e/DescribeProcessToolE2ETests.cs` |

Test naming: `MethodName_ShouldBehavior_WhenCondition`

## Definition of Done

- [ ] Everything load-bearing mirrored at unit level — this suite is advisory in CI, path-filtered, and needs the package installed
- [ ] No fixture restarts or recompiles the shared instance
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
