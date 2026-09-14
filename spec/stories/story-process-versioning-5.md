# Story 5: Prove the version read end to end against a stand

**Feature**: process-versioning
**Jira**: ENG-94374
**FR coverage**: FR-01, FR-02, FR-04
**PRD**: [prd-process-versioning.md](../prd/prd-process-versioning.md)
**ADR**: [adr-process-versioning.md](../adr/adr-process-versioning.md)
**Test plan**: [tp-process-versioning.md](../test-plans/tp-process-versioning.md)
**Repository**: clio
**Status**: done
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

- [x] **AC-01** — Given a stand carrying `CrtProcessBuilder`, when describe runs against an unversioned process, then `version` is 0 and `versions[]` has one root entry
- [x] **AC-02** — Given the stock `InvoiceVisaProcess` family, when describe runs against the root by name, then `isActiveVersion` is false and the active version is named
- [x] **AC-03** — Given the same family, when describe runs against the active version by UId, then `isActiveVersion` is true and `versions[]` lists both members ascending
- [x] **AC-ERR** — Given the configured sandbox environment is unreachable or unset, when the fixture runs, then it self-ignores with a message naming the setting, as the existing cases do

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

- [x] Everything load-bearing mirrored at unit level — this suite is advisory in CI, path-filtered, and needs the package installed
- [x] No fixture restarts or recompiles the shared instance
- [x] Code compiles without Roslyn analyzer warnings
- [x] All new tool names and flags are kebab-case
- [x] Unit tests use `[Category("Unit")]` — never `[Category("UnitTests")]`
- [x] No `catch (Exception)` added to clio code paths
- [x] Knowledge records whose `applies-to` names a file this story touches are updated or deleted **in this PR** (`AGENTS.md:456-457`)
- [x] Docs verdict stated explicitly in the PR body, including "no update required" where that is the verdict
- [x] MCP verdict stated in the PR body ("MCP reviewed, no update required" where that applies)
- [x] PR description references this story file

## Dev Agent Record

- Implementation started: 2026-09-03
- Implementation completed: 2026-09-03
- Tests passing: `McpE2E__Sandbox__EnvironmentName=creatio dotnet test clio.mcp.e2e -f net10.0 --filter "FullyQualifiedName~DescribeProcessToolE2ETests"` → 5 passed, 0 failed, **0 skipped** — the two pre-existing cases plus the three added here, all against the live stand rather than self-ignored.
- Notes:
  - Stand state confirmed before the assertions were written, not after: `ExpireLicenseNotificationProcess`
    reads as version 0 / active / its own root, and the stock family is `InvoiceVisaProcess` (0, inactive)
    plus `InvoiceVisaProcessInvoice1` (1, active). Nothing is seeded.
  - AC-03 takes the active version UId **from the previous answer**, never from a constant, so it holds on any
    stand carrying the family whatever schema UIds it was installed with. That also makes it a proof of the
    whole agent-facing loop the tool description prescribes: describe by name, notice the graph is not the one
    that runs, re-describe by `activeVersionSchemaUId`.
  - Assertions parse the graph instead of scanning the serialized envelope. The graph is not a field of the
    tool envelope — describe writes it through `ILogger.WriteInfo`, so it arrives as an escaped STRING inside
    `execution-log-messages`. `ReadDescribedGraph` finds it by content rather than by property path, and the
    parsed form is what makes key-absence assertable at all: `DescribeProcessResult` carries a
    `[JsonExtensionData]` bag, so a server-sent key would satisfy a substring `Contain`.
  - The family names are consts with a comment saying they ship with the product. Deliberately NOT
    self-ignoring when the stock Invoice package is absent: this suite is manual and excluded from CI by the
    TeamCity category filter, so a red here reaches a human who can read the message, whereas an ignore would
    hide a genuine describe regression on a stand that does have the family.
  - Backfilled `[AllureDescription]` on the two pre-existing tests — `clio.mcp.e2e/AGENTS.md:90` requires it
    alongside `[AllureTag]` and `[AllureName]`, and both predate that rule.
  - `[Category("E2E")]` from this story's Test Requirements table does not exist in this project; the fixture
    carries `[Category(McpE2ECategories.ProcessDesigner)]`, which is what the runner filters on, exactly as
    this story's own Implementation Notes require.
  - This discharges the e2e obligation story 2 recorded as outstanding: `AGENTS.md` requires MCP e2e coverage
    in the PR that changes a tool surface, and stories 2 and 5 are now on the same branch.
  - `catch (JsonException)` in the parse helper is specific, not a bare `catch (Exception)`, and sits in a test
    file rather than a clio code path. 
