# Story 13: The MCP surface — tool contracts, capability map, E2E coverage, and the ClioRing verdict

**Feature**: eng-99856-multi-instance
**FR coverage**: FR-22
**AC coverage**: AC-17 (its E2E half), AC-21 (its MCP + ClioRing half)
**PRD**: [prd-eng-99856-multi-instance.md](../prd/prd-eng-99856-multi-instance.md) — *CLI Impact*, *Delivery cost*
**ADR**: [adr-eng-99856-multi-instance.md](../adr/adr-eng-99856-multi-instance.md) — *Files to modify*, *Test strategy*
**Jira**: ENG-99856
**Status**: ready-for-dev
**Size**: L (full day — four tool descriptions that are the agent-facing contract, the capability map, the
unit guards and an E2E fixture that needs a stand)
**Repo**: clio — `clio/Command/McpServer/`, `clio.tests/`, `clio.mcp.e2e/`, `docs/`
**Depends on**: stories 3, 4, 5, 6, 8, 9, 11 (A-05 decides whether the fixture can exist), 12 (the E2E run
needs the archive the rebundle produced, or an explicit `push-pkg` of the same cut)
**Blocks**: story 14 (the guidance quotes these texts; they must be final first)

---

## As a

AI agent discovering what this toolkit can do

## I want

the tool contracts to describe `multiInstanceOptions`, the dotted mapping path and the refusals in the
same words the server enforces

## So that

I do not learn the contract by being refused, and I do not keep believing the sentence that says
multi-instance is not supported

---

## Acceptance Criteria

- [ ] **AC-01** (FR-22) — Given `CreateBusinessProcessTool` and `ModifyBusinessProcessTool`, when their
  `[Description]` text is read, then it documents `multiInstanceOptions` (`enabled`, `executionMode` as a
  **string only**, `ignoreErrors`), the dotted `elementParameter` / `sourceElementParameter` form, and
  that the collection to iterate is bound with plain `addMapping`.
- [ ] **AC-02** (FR-22) — Given `DescribeProcessTool`, when its text is read, then the
  `multiInstanceOptions` read block is described and the "a re-sync is REFUSED on it" clause is **gone**
  (it is one of story 14's 19 retraction targets — coordinate so it is retracted once).
- [ ] **AC-03** — Given the first sentence of each changed `[Description]`, when the `get-tool-contract`
  compact index renders it, then that sentence still says what the tool **does**. The compact index is
  the only discovery surface a non-resident tool has, and the first sentence is what it shows.
- [ ] **AC-04** — Given the refusals (output-collection target, mode-without-`enabled`, numeric
  `executionMode`, retarget), when the tool text describes them, then it names the working alternative for
  each, in the same words the server's message uses. A tool description that disagrees with the runtime
  message is worse than silence.
- [ ] **AC-05** — Given de-conversion (`enabled: false`, story 8), when it is documented, then it is
  described as a **destructive** write and its lossy `Variable`-twin behaviour is stated.
- [ ] **AC-06** (FR-22) — Given `docs/McpCapabilityMap.md`, when it is read, then the changed describe and
  write surface is reflected. Check the whole sub-process bullet, not only the added line — this document
  has been left stale by a merge before.
- [ ] **AC-07** (FR-22) — Given the MCP prompts and resources under `clio/Command/McpServer/Prompts/` and
  `Resources/`, when they are reviewed, then they are updated or the PR states **"MCP reviewed, no update
  required"** for each, explicitly.
- [ ] **AC-08** (FR-22) — Given `clio.tests/Command/McpServer/`, when the suite runs, then the tool
  `[Description]` guards cover the new text (including any drift test that pins the description's
  content), and the shipped workspace templates under `clio/tpl/**` still satisfy
  `WorkspaceTemplateGuidanceDriftTests` — no template may name a tool that is not resident or bridged.
- [ ] **AC-09** (FR-22, AC-17) — Given `clio.mcp.e2e/SubProcessMultiInstanceToolE2ETests.cs`, when it runs
  against a stand carrying the rebundled package, then it covers convert → bind the collection → map one
  item → describe round-trip, plus one refusal. **Mandatory** coverage, even though it is advisory in CI.
- [ ] **AC-10** — Given that the process-designer E2E fixtures do **not** run in CI (`CrtProcessBuilder`
  is not installed on that stand) and that the MCP E2E check is advisory and cannot fail a merge, when
  this story completes, then every load-bearing assertion in it has a **unit-level mirror** named in the
  PR description — starting with AC-17's caller stamp, which is mirrored by story 5's test.
- [ ] **AC-11** — Given story 11's A-05 answer, when the fixture is designed, then it is designed against
  it: if a multi-instance element cannot be built by hand on the target stand, the PR says the E2E happy
  path rests on a toolkit-built element (or says plainly that there is none) rather than quietly shipping
  a weaker fixture.
- [ ] **AC-12** (AC-21) — Given the PR description, when it is reviewed, then it carries
  **"ClioRing compatibility reviewed, no Ring-consumed contract changed"** with the inspected paths
  (`clio-ring/ClioRing.Ipc`, `clio-ring/ClioRing`, `clio-ring/ClioRing.Desktop/actions.json`). Ring's
  entire consumed surface is three tool names and six nested `clio-run` commands, **none** of them
  process-designer — but the statement is mandatory, and the inspection that backs it is not optional.
- [ ] **AC-ERR** — Given a tool call with an unknown argument key, when it is refused, then the existing
  `ValidArgsHint` echo still lists the canonical fields and no new top-level tool argument was added (this
  feature adds none — the member lives inside the descriptor JSON).

## Implementation Notes

Files: `clio/Command/McpServer/Tools/ProcessDesigner/CreateBusinessProcessTool.cs`,
`ModifyBusinessProcessTool.cs`, `DescribeProcessTool.cs`, and `ModifyProcessAsNewVersionTool.cs` (it
shares the descriptor); `docs/McpCapabilityMap.md`; `clio.tests/Command/McpServer/`;
`clio.mcp.e2e/SubProcessMultiInstanceToolE2ETests.cs`.

**This feature adds no CLI surface at all.** The four process-designer commands carry no `[Verb]` and no
`[Option]`; they are registered in `clio/BindingsModule.cs` and reached only through their MCP tools.
CLIO001 is therefore satisfied **vacuously** — and the new JSON members stay camelCase
(`multiInstanceOptions`, `executionMode`, `ignoreErrors`): the kebab-case rule governs CLI option long
names, not wire members. Say this in the PR so nobody "fixes" it.

The write descriptor crosses clio as **opaque JSON** (`CreateBusinessProcessArgs.descriptor` is a string),
so the tool `[Description]` is the only clio-side contract surface for the write member. That makes the
text load-bearing rather than decorative: an agent that cannot read the member here cannot discover it at
all.

Keep the descriptions **tight**. These four tools already carry very long descriptions and one of them
previously shipped the same paragraph four times; add what is new, and delete anything the change makes
false rather than appending a correction next to it.

**OQ-05 applies here too**: `validate-process-graph` grows **no** rule in v1, so
`ValidateProcessGraphTool` and `ProcessGraphValidator` are untouched. State that as a deliberate
non-change in the PR (the reviewer will look). If the owner reverses it, that is a separate story in clio
and it starts by extending `ProcessGraphNode`, which is `(string Name, string Type)` today.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` (clio) | tool `[Description]` guards for the four tools; the compact-index first sentence; the template drift oracle stays green | `clio.tests/Command/McpServer/` |
| Unit `[Category("Unit")]` (clio) | the describe DTO round trip (story 9's fixture, re-run) | `clio.tests/Command/McpServer/DescribeProcessMultiInstanceTests.cs` |
| E2E `[Category("E2E")]` | convert → bind collection → map item → describe; one refusal | `clio.mcp.e2e/SubProcessMultiInstanceToolE2ETests.cs` |

Test naming: `Description_ShouldDocumentMultiInstanceOptions_WhenToolContractIsRead`.

## Definition of Done

- [ ] Four tool descriptions updated; first sentences still state purpose; nothing false left appended
- [ ] `docs/McpCapabilityMap.md` updated; prompts/resources updated or explicitly "no update required"
- [ ] `clio.mcp.e2e` coverage added (mandatory) and every load-bearing assertion has a unit mirror
- [ ] `WorkspaceTemplateGuidanceDriftTests` green
- [ ] **"ClioRing compatibility reviewed, no Ring-consumed contract changed"** with the inspected paths
- [ ] `validate-process-graph` deliberately unchanged (OQ-05), stated in the PR
- [ ] Tests: AAA, `because` on every assertion, `[Description]` on every method, categories are only
      `Unit` / `Integration` / `E2E`
- [ ] No new `CLIO*` diagnostics in touched files; no new CLI flag (kebab-case vacuous)
- [ ] Validated locally:
      `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&(Module=McpServer|Module=ProcessModel)" --no-build`
      and the E2E fixture run by hand against the stand, with the result quoted
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- E2E run result (stand, version):
- Notes:
