# Story 2: Report version facts in the describe response

**Feature**: process-versioning
**Jira**: ENG-94374
**FR coverage**: FR-01..FR-06, FR-17, FR-18, FR-19
**PRD**: [prd-process-versioning.md](../prd/prd-process-versioning.md)
**ADR**: [adr-process-versioning.md](../adr/adr-process-versioning.md)
**Test plan**: [tp-process-versioning.md](../test-plans/tp-process-versioning.md)
**Repository**: clio
**Status**: ready-for-dev
**Size**: M

---

## As a

developer

## I want

describe-business-process to carry the version, the active-version pointer, the family root and the version history

## So that

an agent can tell whether the graph it just read is the one the runtime executes

---

## Acceptance Criteria

- [ ] **AC-01** — Given an unversioned process, when describe runs, then `version` is 0, `isActiveVersion` is true, `versions[]` holds one root entry and no `versionReadWarning` is present
- [ ] **AC-02** — Given the stock `InvoiceVisaProcess` family, when describe runs against the root by name, then `isActiveVersion` is false and `activeVersionName` is `InvoiceVisaProcessInvoice1`
- [ ] **AC-03** — Given the same family, when describe runs against the active version by UId, then `isActiveVersion` is true
- [ ] **AC-04** — Given the reader reports a warning, when describe runs, then the graph is returned, the parsed output has no version keys, and `versionReadWarning` names the failure
- [ ] **AC-05** — Given a described family member, when the output is serialized, then it carries `schemaUId`, `name`, `caption`, `version`, `isActiveVersion`, `isRoot`, `packageUId` and `enabled`
- [ ] **AC-06** — Given a truncated family, when describe runs, then `versionsTruncatedAt` is 50
- [ ] **AC-ERR** — Given the server response cannot be parsed, when describe runs, then the existing `Error: could not parse server response` path is unchanged

## Implementation Notes

Key file: `clio/Command/ProcessModel/IProcessDescriber.cs`.
Members go on the PUBLIC `DescribeProcessResult` (insert after `SchemaUId`, `:144-145`) and NEVER on the private `DescribeProcessWireResult` subclass — `DescribeProcessCommand.cs:70` serializes by the static type and drops subclass members.
`ServerProcessDescriber` takes `IProcessVersionLibReader` as a 4th primary-constructor parameter (`:38-41`) and fills the members at `return result;` (`:86`), after all five refusal paths.
Doc comments state ADR choices 5 and 6: the answer is the process library's, absent means NOT ESTABLISHED, and a family entry's `enabled` is FAMILY state.
`versions` stays null when not established — never `[]`.
Assert **parsed keys**, not substrings: `DescribeProcessResult` carries `[JsonExtensionData]` (`:159-165`), so a server that already returns a `version` key would defeat a substring scan.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | each member present/absent, active-version naming, family projection, truncation flag | `clio.tests/Command/ProcessModel/ServerProcessDescriberTests.cs` |
| Unit `[Category("Unit")]` | parsed-key assertions on the serialized command output, including key absence | `clio.tests/Command/DescribeProcessCommandTests.cs` |

Test naming: `MethodName_ShouldBehavior_WhenCondition`

## Definition of Done

- [ ] `CreateDescriber` in `ServerProcessDescriberTests.cs:26-31` gains the reader — a deliberate compile break, fixed first
- [ ] Serialized assertions use capture-then-assert (`WriteInfo(Arg.Do<string>(...))`, `:132`/`:176`) so every assertion carries a because-clause
- [ ] **This story owns** the narrowing of `docs/knowledge/ProcessModel/describe-business-process-reads-the-base-version-not-the-active-one.md`: the by-name half stays true, the "no field to reveal it" half is removed
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
