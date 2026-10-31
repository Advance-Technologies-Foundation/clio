# Test Plan: Send email attachments (ENG-95985)

**Feature**: eng-95985-send-email-attachments
**Jira**: ENG-95985 Send email attachments (Story; epic ENG-92704 Create BP via AI Toolkit)
**Spec**: [spec-eng-95985-send-email-attachments.md](../prd/spec-eng-95985-send-email-attachments.md) - AC1-AC7
**ADR**: [adr-eng-95985-send-email-attachments.md](../adr/adr-eng-95985-send-email-attachments.md) - D1-D5
**Story**: [1](../stories/story-eng-95985-send-email-attachments-1.md)
**Source**: the analysis test plan
[`spec/eng-95985-send-email-attachments/eng-95985-send-email-attachments-test-plan.md`](../eng-95985-send-email-attachments/eng-95985-send-email-attachments-test-plan.md).
This file keeps its ids and its coverage; the full rows (test names, arguments, source anchors, CI markers, the
harness rules, the parity comparison rule, the e2e fixture rules and the manual case steps) are in that file and are
not duplicated here. A row changes there first.
**Status**: Draft (the cases follow the owner-agreed answers Q1-Q20 of 2026-10-28; rows that move with a measurement
name it)
**Created**: 2026-10-28

Path aliases: `PB/` = crt-process-builder `packages/CrtProcessBuilder/Files/src/cs/`; `PBT/` =
`tests/UnitTests/CrtProcessBuilder.Tests/`; `KB/` = the clio-knowledge repository root; clio paths are relative to the
clio repository root.

---

## Test id classes

| Class | Where | Ids | Count |
|---|---|---|---|
| **TC-U** | package unit tests (PBT) | TC-U-01..TC-U-92, plus TC-U-78a, 78b, 78c; TC-U-42 and TC-U-48 are removed rows (Q9) | 93 live |
| **TC-P** | package parity tests (PBT) | TC-P-01..TC-P-09 | 9 |
| **TC-C** | clio unit tests (`clio.tests`) | TC-C-01..TC-C-23 | 23 |
| **TC-I** | clio MCP e2e (`clio.mcp.e2e/SendEmailAttachmentsToolE2ETests.cs`, on stand `Creatio`) | TC-I-01..TC-I-10 | 10 |
| **TC-K** | clio-knowledge tests and gates (`KB/automation/Clio.Knowledge.Bundle.Tests`) | TC-K-01..TC-K-13 (TC-K-12 is the fact-check gate; TC-K-13 follows EM8) | 13 |
| **TC-01..TC-10** | manual Jira cases (business tasks given to an agent) | TC-01..TC-10 | 10 |
| **EM0-EM15** | stand measurements (analysis measurements file) | EM6 dropped (Q17), EM12 dropped (Q9) | 14 |

TC-U, TC-P, TC-C and TC-K together are this plan's unit level and TC-I its integration level; every row of all five
classes is a mandatory exit criterion (AGENTS.md "all TC-U-* and TC-I-* test cases must be implemented" applies to all
of them).

"Existing" means the test is already in the tree and is only re-run or extended with rows.

## Coverage matrix

| Item | Package unit | Parity | clio unit | e2e | KB | Stand | Manual |
|---|---|---|---|---|---|---|---|
| **AC1** block + describe read-back (build, `addElement`, `setElement`) | TC-U-01..07, 09..14, 19, 59..63, 69, 74 | TC-P-03, 08 | TC-C-01..03, 07..11 | TC-I-01, 06, 09 | TC-K-01 | EM2, EM3 | TC-10 |
| **AC2** Process file variants; one collection feeds several emails | TC-U-09, 10, 15, 20 | TC-P-03, 04 | - | TC-I-01, 02, 03 | TC-K-06 | EM4, EM4c, EM11, EM14 | TC-01, 02, 03, 06 |
| **AC3** more than one attachment | TC-U-14, 25, 28 | TC-P-05, 06 | TC-C-09, 14 | TC-I-01, 05 | TC-K-01 | EM4 | TC-07 |
| **AC4** manual/auto x custom/template | TC-U-16, 17 | TC-P-03, 07 | - | TC-I-03, 10 | TC-K-06 | EM4, EM5, EM7, EM15 | TC-01, 02, 04, 05 |
| **AC5** clear error naming the source | TC-U-31..41, 54, 57, 76, 78, 78a, 78b, 80 | - | TC-C-02 | TC-I-04 | TC-K-01 | - | TC-09 |
| **AC6** ordering rule | TC-U-54..58, 77 | - | - | TC-I-04 | TC-K-03 | EM10 | - |
| **AC7** parity, tests, docs, MCP | TC-U-03, 04, 13, 69, 70, 73 | TC-P-01..09 | TC-C-04..06, 16..22 | all TC-I | TC-K-07..12 | EM1, EM2, EM3 | - |
| **Q3** single File accepted, warned | TC-U-11, 43 | TC-P-04, 09 | - | TC-I-01 | TC-K-06 | EM8 | TC-10 |
| **Q4** replace semantics | TC-U-21..30, 44, 45, 76 | - | TC-C-08, 12..15, 23 | TC-I-05, 06 | TC-K-04 | EM1 | TC-08 |
| **Q5** lowest free number | TC-U-02, 25 | TC-P-06 | - | TC-I-05 | - | EM1 | - |
| **Q6** refusals + reachability warning | TC-U-31..41, 47, 54..57, 77, 79 | - | - | TC-I-04 | TC-K-03 | EM10 | TC-09 |
| **Q8** non-block mappings onto a slot refused | TC-U-49, 50, 52, 53, 74, 75 | - | - | TC-I-07 | - | EM9 | - |
| **Q9** no build-time feature check; KB troubleshooting line | - | - | - | - | TC-K-05 | - | - |
| **Q10** mapping FROM a slot refused, a Formula body too | TC-U-51, 83 | - | - | - | - | - | - |
| **Q11** describe shape | TC-U-59..66 | - | TC-C-03, 09..11 | TC-I-01, 06, 09 | TC-K-04 | EM2 | TC-10 |
| **Q16** multi-instance Send email refused | TC-U-80, 81 | - | - | - | TC-K-13 | EM8, EM11 | - |
| **Q20** unscoped Process file source refused; Sub-process hand-offs noticed | TC-U-78, 78a, 78b, 78c, 84..86, 89, 91, 92 | - | - | TC-I-04 | TC-K-06 | - | - |
| **G3** modify-as-new-version keeps the slots owned | TC-U-71 | - | TC-C-06 | TC-I-08 | - | EM13 | - |

## TC-U - package unit tests (PBT)

Every new fixture is `[TestFixture(Category = "UnitTests"), Category("PreCommit")]`; the schema comes from
`UserConnection.CreateSchema()` (a REAL `UId`, with a precondition assert), and "untouched" is asserted on the
element's `WriteMetaData` bytes. Run on `-c dev-nf` and `-c dev-n8`.

| Group (fixture) | Ids | What it proves |
|---|---|---|
| Slots (C3) `EmailAttachmentSlotTests` | TC-U-01..08 | the read/write slot predicate `IsSlot` equals the run-time filter; lowest free number over every parameter of the element; the designer's two-level shape, stamps, item name; remove both levels; numeric order; the platform's own filter returns exactly the built slots (feasibility-gated) |
| Sources, shape, modes, routes (C4) `SendEmailAttachmentsApplierTests` | TC-U-09..20, 73 | both levels bound from a collection (item found by type), from an item path, item-only from a single File and an expression; Script bindings stamped with the process; one slot per entry; one collection feeds two emails; identical slots in all four mode combinations and after a mode switch; attachments count as configuration; the same bytes on every route; SysFile-stored Object source; designer display values on slots only |
| Replace semantics (Q4, Q5) `SendEmailAttachmentsReplaceTests` | TC-U-21..30, 76 | matched slot kept byte for byte; unmatched removed; `[]` removes all; absent untouched; new entry after a gap; matching on resolved bindings; idempotent replay; duplicates; foreign-stamped slot untouched; designer odd shapes replaced; removal refused while something reads the slot |
| Refusals (Q6, Q16, Q20) `SendEmailAttachmentsRefusalTests` | TC-U-31..41, 78, 78a, 78b, 78c, 80 | each refusal by fragment (element, `email.attachments[<i>]`, source), bytes unchanged, no notice: no source, several sources, `value`, unresolvable, fileless, several File items, nested, own parameter, not a Send email, validate-all-before-write, entry index; unscoped Object and Report sources judged at the end of the request; describe issue for an unscoped source; multi-instance Send email |
| Notices `SendEmailAttachmentsNoticeTests` | TC-U-43..47, 79 (TC-U-42, 48 removed) | empty single File warning, duplicate source, removal notice in the write shape, expression not checked, notices only on a saved edit, firing rules for every entry of a sent list |
| Slot guards (C5, Q8, Q10) `SendEmailAttachmentsRawMappingTests` | TC-U-49..53, 74 | raw target refused on every non-block route through the guard predicate `IsReservedName` (also a slot not built yet, a foreign-stamped slot, a near-miss name); block writes pass; a slot as a source refused; other `Attachments`-named parameters as before (PU-42 mirror); `TakesFiles` unchanged |
| Order checks (C6, Q6, Q7) rows in `ProcessBuildHandlerTests` / `ProcessGraphBuilderTests` / `ProcessModifyHandlerTests` | TC-U-54..58, 77 | later-declared source refused on build (planned names); earlier accepted; process parameter never order-checked; modify resolves in operation order; block inventory; reachability warning on build and modify |
| Describe (C7, Q11, Q16) `SendEmailAttachmentsDescribeTests` | TC-U-59..66, 75, 81, 82 | write-shape entries with `slot` and `bindingLevels`; stored (designer) order, not slot number (revised 2026-10-29); null when none; `configured`; describe -> replay writes nothing; one issue per stored shape; no false alarm on the package's own shapes; non-slots ignored; generic `parameters[]` does not decode any name `IsReservedName` matches (also a foreign-stamped `Attachments2` and the near-miss `attachments1`); multi-instance element reported with an issue |
| C2 refactor proof `FileSourcePlannerCharacterizationTests` | TC-U-67, 68 | every `fileProcessing.files` message pinned verbatim BEFORE the lift and unchanged after it; the planner speaks in the caller's terms |
| Reuse round: stored filter read as the run time reads it, flags, formula body, consolidation pins | TC-U-83..90 | a Formula body reading a slot refused; `$id`/`$ref`, shared trees and (Object) unmarked filters unreadable; a disabled item in any spelling selects nothing; a negated or OR scope never lifted; refusal wording for an ungenerated printable |
| Sub-process and Formula hand-offs (decisions Q20, 2026-10-30) `SendEmailAttachmentsSweepTests`, `SendEmailAttachmentsNoticeTests` | TC-U-91, 92 | a Formula element result onto a Process parameter element's `Files` input is followed by the end-of-request sweep (unscoped refused, scoped sent); files passed into a Sub-process `In`/`Variable` parameter from an unscoped source, and a slot reading an `In`/`Variable` parameter, get a notice on each side and save |
| Contract, DI, clone (existing fixtures) | TC-U-69..72 | wire names of the new members; DI graph resolves; the version clone keeps the slots owned; every fixture carries a CI category |

## TC-P - package parity tests (PBT)

Comparison rule: the attachment comparison rule (ACR, analysis serialization section 6.2, R1-R11).

| Pattern | Fixture | Ids | Oracle |
|---|---|---|---|
| A - the corpus, committed (3 processes, PROVENANCE SHA pins, `-text`) | `SendEmailAttachmentsParityTests` | TC-P-01..03 | shipped `ProcessFileSendMail`, `ProcessFileReport`, `MailConnectionsProcess` (four slots, all four mode combinations) |
| B - the stand designer capture (EM1), restated as rows | `SendEmailAttachmentsStandCaptureParityTests` | TC-P-04..07 | EM1: every source kind, five slots, gap and re-add, template + auto; rows written only after EM1 ran |
| C - the platform writer's JSON | `SendEmailAttachmentsSerializationTests` | TC-P-08, 09 | a quoted corpus fragment; EM1's single-File row |

## TC-C - clio unit tests

Full unit suite before every commit (more than three modules change):
`dotnet test clio.tests/clio.tests.csproj --filter "TestCategory=Unit" < /dev/null`.

| Group | Ids | What it proves |
|---|---|---|
| Key check (K2) `ProcessBlockKeyCheckTests` + existing contract tests | TC-C-01..05 | `attachments` accepted on every route; unknown entry keys refused with their path; read-back fields tolerated; key sets equal the bundled contracts; read-back fields are describe-only |
| Floors (K3) | TC-C-06 | the three floors and the shipped archive |
| Forwarding (K4) | TC-C-07, 08 | create and modify forward the list verbatim; an empty list survives |
| Typed describe (K6) `ServerProcessDescriberTests` | TC-C-09..11 | typed `DescribedEmail.Attachments` with a gap; null when omitted; the extension bag kept |
| Read-back checks (K7) `EmailBlockExpectationTests` | TC-C-12..16, 23, 26 | detected on every route; last list wins; unlanded attachments reported; quiet when all landed; qualitative wording without a version; "N attachments were removed by the replace rule"; TC-C-26: two rows of one source listed out of slot order (before `[Attachments2 Doc, Attachments1 Doc]`, after `[Attachments1 Doc]`) report Attachments2 as the removed slot - rows whose slot and source both survive are matched before the rest is matched by source |
| Stand-run fix: a name in `element` (`ProcessBlockKeyCheckTests`, `ModifyBusinessProcessServiceTests`) | TC-C-24 | a string `element` is refused on every operation before anything is posted; setElement / removeElement are pointed to `elementName` with the caller's value, addElement to the element object with its `name` and `type`; the value prints readably (non-ASCII, `'`) |
| Stand-run fix: a request the server could not read (`ProcessDesignServerFaultTests`, `CreateBusinessProcessServiceTests`, `ModifyBusinessProcessServiceTests`, `ModifyProcessAsNewVersionServiceTests`) | TC-C-25 | WCF's request-deserialization string is reported as NOT applied - create: no process, modify: no edit, version save: no version - with the server's text quoted and cut at 400 characters; any other bare error string keeps the outcome UNKNOWN and is quoted; one shared helper classifies all three |
| Descriptions, budgets, prompts (K4, K5, Q12) | TC-C-17..19 | description pins; `ToolContractPayloadBudgetTests` incl. the seven-contract 19,000 B wire bundle; prompt pins |
| Bundle, curated names, e2e selection (K1, K9) | TC-C-20..22 | `BundledProcessBuilderPackageTests` with the two security counts unchanged; `WorkspaceTemplateGuidanceDriftTests` after the re-pin; `McpE2eSelectionCoverageTests` |

## TC-I - clio MCP e2e (stand `Creatio`)

Fixture `SendEmailAttachmentsToolE2ETests`, category `McpE2E.ProcessDesigner`, writes strictly sequential, cleanup
newest-first in `finally`. The PR lane excludes this category, so the evidence is a stand run recorded in the clio PR
body per framework with Ignored = 0:

```
dotnet test clio.mcp.e2e/clio.mcp.e2e.csproj -f net10.0 --filter "TestCategory=McpE2E.ProcessDesigner&FullyQualifiedName~SendEmailAttachmentsToolE2ETests"
dotnet test clio.mcp.e2e/clio.mcp.e2e.csproj -f net8.0  --filter "TestCategory=McpE2E.ProcessDesigner&FullyQualifiedName~SendEmailAttachmentsToolE2ETests"
```

| Id | Test | Asserts |
|---|---|---|
| TC-I-01 | `CreateBusinessProcess_Should_StoreEveryAttachmentInTheDesignersShape_AndDescribeItBack` | four entries (Object, Process-parameter variant, FileCollection, File): describe and stored bytes |
| TC-I-02 | `CreateBusinessProcess_Should_AttachAGeneratedReport` | `ReportFiles` source |
| TC-I-03 | `CreateBusinessProcess_Should_FeedFourSendEmailElementsFromOneCollection_InEveryModeCombination` | one collection, four mode combinations, equal slots |
| TC-I-04 | `CreateBusinessProcess_Should_RefuseAnAttachmentWithoutFiles_AndCreateNothing` | the refusals incl. later-declared and unscoped sources; nothing created |
| TC-I-05 | `ModifyBusinessProcess_Should_ReplaceThenRemoveAttachments` | replace keeps slot 1, removes the rest, numbers the new entry; `[]` removes all |
| TC-I-06 | `ModifyBusinessProcess_Should_ChangeNoStoredByte_WhenTheDescribedListIsSentBack` | describe -> replay is byte-neutral |
| TC-I-07 | `ModifyBusinessProcess_Should_RefuseARawMappingOntoAnAttachment` | `addMapping` onto a slot refused |
| TC-I-08 | `ModifyProcessAsNewVersion_Should_KeepTheAttachmentsOwnedByTheNewVersion` | clone stamps on both levels |
| TC-I-09 | `DescribeBusinessProcess_Should_ReadTheDesignerBuiltAttachmentsFixture` | read-only on the EM1 fixture `UsrSeAttachmentsDesignerFixture` |
| TC-I-10 | `RunProcess_Should_PutTheGeneratedReportOnTheManualEmailActivity` | a run parks on the manual email; one Activity and one `ActivityFile` (a `.docx`); cleanup through `execute-dataservice-batch` |
| TC-I-11 | `ModifyBusinessProcessToolE2ETests.ModifyBusinessProcess_Should_RefuseANameInElement_BeforeAnythingIsSent` | setElement and addElement with a name in `element` are refused by clio before sending, each with its own fix, exit code 1, no missing-process answer from the server; needs a registered environment, writes nothing (lives in the modify fixture, not this one) |

## TC-K - clio-knowledge tests and gates

Run the whole `automation/Clio.Knowledge.Bundle.Tests` project.

| Id | What it proves |
|---|---|
| TC-K-01 | the new article owns the block and carries the real floor; the entry article routes to it |
| TC-K-02 | `email.attachments` (files the email sends) kept apart from `fileProcessing.attachments` (files a Process file element reads) in both articles |
| TC-K-03 | both order rules, with EM10's measured result (the dated record is in the measurements file, EM10) |
| TC-K-04 | the replace rule and the describe-before-send recipe |
| TC-K-05 | the `UseProcessEmailAttachments` troubleshooting line (Q9), and no claim of a build-time check |
| TC-K-06 | run-time facts: four combinations, manual activity, system rights (Q20), report is a Word file |
| TC-K-07 | the retired "not buildable" claim is gone everywhere |
| TC-K-08 | no internal reference or placeholder floor |
| TC-K-09 | the pinned `files.md` clause split in the same commit |
| TC-K-10 | size gates (`process-modeling.md` route line paid by a swap) |
| TC-K-11 | manifest and routing |
| TC-K-12 | refute-first fact-check gate (not a unit test): one agent per claim cluster, forbidden to cite `docs/knowledge/`, the analysis folder, the research notes or another agent's output; verdict table in the KB PR body |
| TC-K-13 | the per-recipient multi-instance Sub-process pattern described only if EM8 passed (Q16) |

## Manual Jira cases (TC-01..TC-10)

Business tasks given to an agent; drafted in the analysis test plan section 8 (conditions, steps, expected results)
for the owner to paste.

| Id | Task |
|---|---|
| TC-01 | Email the generated invoice to the customer |
| TC-02 | Forward the latest file of a contact |
| TC-03 | Email files passed in as a parameter |
| TC-04 | Prepare a templated draft with the contact's files for review |
| TC-05 | Send a templated email with a document pack automatically |
| TC-06 | Use one generated report in two emails |
| TC-07 | Attach files from two sources to one email |
| TC-08 | Change and remove the attachments of an existing process |
| TC-09 | Refuse an attachment whose source holds no files |
| TC-10 | Explain a designer-built email with attachments without changing it |

## Stand measurements

EM0-EM15 (EM6 and EM12 dropped) are mapped to the AC and the tests they feed in the analysis test plan section 7;
their procedures, gates, expected results and cleanup are in the analysis measurements file. Every stand write waits
for the owner's yes. The STOP gate is EM2 + EM3 after slice B; EM7 (real delivery) runs once the owner says mail
sending on stand `Creatio` works.

## Regression scope

Analysis test plan section 9: the PBT mapping, Send email, Process file, graph/build, ledger, clone, wire-contract and
DI suites and the whole PBT project on dev-nf and dev-n8; the clio full unit suite; the existing Send email and
Process file e2e fixtures with the same category filter; the whole KB test project.

## Conventions

AAA with explicit `// Arrange`, `// Act`, `// Assert`; `because` on every assertion; a `[Description]` on every test;
no Jira key in test names or descriptions; PBT is C# 7.3; no OS paths; categories as the host fixture places them (analysis test plan section
10).

## Risk assessment

The risks and their mitigations are the analysis plan's section 10 (EM2/EM3 failing at the STOP gate, the description
budget, mail on stand `Creatio`, a foreign cut during review, a KB `libraryVersion` collision, regressions from the
shared mapping-service guard, the local core-bin newer than CI's 8.3.4); the tests that answer them are the PU-42
mirror (TC-U-52), the budget rows (TC-C-17..19) and the full regression scope above.

## Exit criteria

The story's Definition of Done, with every row of TC-U, TC-P, TC-C, TC-K and TC-I implemented and green (see "Test id
classes"); TC-U-42 and TC-U-48 are removed rows, TC-K-12 is the fact-check gate and TC-K-13 follows EM8.

## Deliberately not covered

A flow-reachability refusal (Q6 C), attachments on a multi-instance Send email beyond the refusal (Q16), Creatio.ai
`Files<N>` slots (Q15), the `templateEntity` order guard (Q7 B), `UseProcessEmailAttachments` off (Q9), file size and
extension limits, and real SMTP delivery in automated tests (EM7 and the manual auto-mode cases only).
