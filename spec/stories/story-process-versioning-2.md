# Story 2: Report version facts in the describe response

**Feature**: process-versioning
**Jira**: ENG-94374
**FR coverage**: FR-01..FR-06, FR-17, FR-18, FR-19
**PRD**: [prd-process-versioning.md](../prd/prd-process-versioning.md)
**ADR**: [adr-process-versioning.md](../adr/adr-process-versioning.md)
**Test plan**: [tp-process-versioning.md](../test-plans/tp-process-versioning.md)
**Repository**: clio
**Status**: done
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

- [x] **AC-01** — Given an unversioned process, when describe runs, then `version` is 0, `isActiveVersion` is true, `versions[]` holds one root entry and no `versionReadWarning` is present
- [x] **AC-02** — Given the stock `InvoiceVisaProcess` family, when describe runs against the root by name, then `isActiveVersion` is false and `activeVersionName` is `InvoiceVisaProcessInvoice1`
- [x] **AC-03** — Given the same family, when describe runs against the active version by UId, then `isActiveVersion` is true
- [x] **AC-04** — Given the reader reports a warning, when describe runs, then the graph is returned, the parsed output has no version keys, and `versionReadWarning` names the failure
- [x] **AC-05** — Given a described family member, when the output is serialized, then it carries `schemaUId`, `name`, `caption`, `version`, `isActiveVersion`, `isRoot`, `packageUId` and `enabled`
- [x] **AC-06** — Given a truncated family, when describe runs, then `versionsTruncatedAt` is 50
- [x] **AC-ERR** — Given the server response cannot be parsed, when describe runs, then the existing `Error: could not parse server response` path is unchanged

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

- [x] `CreateDescriber` in `ServerProcessDescriberTests.cs:26-31` gains the reader — a deliberate compile break, fixed first
- [x] Serialized assertions use capture-then-assert (`WriteInfo(Arg.Do<string>(...))`, `:132`/`:176`) so every assertion carries a because-clause
- [x] **This story owns** the narrowing of `docs/knowledge/ProcessModel/describe-business-process-reads-the-base-version-not-the-active-one.md`: the by-name half stays true, the "no field to reveal it" half is removed
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
- Tests passing: `dotnet test --filter "Category=Unit&(Module=Command|Module=McpServer|Module=ProcessModel)"` → 8133 passed, 0 failed, 14 skipped. 7 new tests in `ServerProcessDescriberTests` and 3 in `DescribeProcessCommandTests` cover TC-U-09, TC-U-10, TC-U-11, TC-U-11b, TC-U-12, TC-U-13 and TC-U-14; 2 in `DescribeProcessToolTests` pin the shipped MCP text.
- Notes:
  - The overlay assigns EVERY version member, the failure path included, and has its own regression test.
    The wire result deserializes into the same public type, so a newer `CrtProcessBuilder` already returning a
    `version` key would otherwise bind it to the property and leave a server value standing beside a warning
    saying nothing was established. The process library is the authority (ADR choice 5) and
    `activeVersionSource` says so in the output.
  - TC-U-14 held, but its premise moved: the plan expected a server-sent root-level `version` to land in
    `[JsonExtensionData]`. Now that the key is a declared property, it binds to the property instead — which
    is exactly why the overlay assigns on the failure path, and the test asserts the discarded value.
  - `versionsTruncatedAt` reports the length of the list actually published rather than the reader cap, so the
    number can never disagree with the list. ADR updated.
  - `DescribedProcessVersion` carries no `[JsonExtensionData]` bag — clio builds every entry, so there is no
    server field to drop. Documented on `ApplyVersionFacts` and added to
    `docs/knowledge/ProcessModel/described-filter-types-have-no-json-overflow-bag.md`, whose enumeration of
    bag-less types was otherwise left incomplete by this change.
  - Docs: `DescribeProcessCommand` has no `[Verb]`, so `help/en/`, `docs/commands/`, `Commands.md` and
    `WikiAnchors.txt` have no entry for it — docs reviewed, no update required.
  - MCP: the tool `[Description]` and the `describe-business-process` prompt both gained the version contract,
    the prompt as a new step 2 that has to be read BEFORE narrating. That is story 4 work pulled forward —
    AGENTS.md makes the MCP review mandatory on a "Command output" change, so it could not wait a story;
    story 4 records the reduction and what still remains there. The shipped text is pinned in the same commit
    rather than left to story 4: a per-token test plus a cross-channel drift guard over the description and
    the prompt, following the `CompileCreatioToolTests` pattern. No tool renamed or removed, so no
    `McpToolCompatibilityCatalog` entry. ClioRing compatibility reviewed, no Ring-consumed contract changed:
    `clio-ring/ClioRing.Ipc`, `clio-ring/ClioRing` and `clio-ring/ClioRing.Desktop/actions.json` carry no
    reference to `describe-business-process`.
  - E2E: AGENTS.md requires MCP e2e coverage in the PR that changes a tool surface. The plan assigns describe
    e2e to story 5 (TC-E-01, TC-E-02), so story 5 must land in the same PR as this one — this story is not
    PR-ready alone. 
