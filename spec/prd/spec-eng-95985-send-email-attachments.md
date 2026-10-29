# SPEC: Send email attachments (ENG-95985)

**Jira**: ENG-95985 Send email attachments (Story; epic ENG-92704 Create BP via AI Toolkit)
**ADR**: [adr-eng-95985-send-email-attachments.md](../adr/adr-eng-95985-send-email-attachments.md)
**Story**: [1](../stories/story-eng-95985-send-email-attachments-1.md) (the whole ticket)
**Test plan**: [tp-eng-95985-send-email-attachments.md](../test-plans/tp-eng-95985-send-email-attachments.md)
**Analysis**: [`spec/eng-95985-send-email-attachments/`](../eng-95985-send-email-attachments/README.md) - README,
decisions (Q1-Q20), facts, serialization, design, measurements (EM0-EM15), plan, test plan, estimate. This SPEC
restates what implementation needs; the analysis holds the evidence and the source anchors.
**Status**: Accepted. All twenty owner questions (Q1-Q20) were agreed by the owner on 2026-10-28; Q9, Q16 and Q17
differ from the first recommendation. The Jira acceptance-criteria text (Q19) is written into the issue description by
the lead only after the owner approves that text.
**Created**: 2026-10-28
**Size estimate**: one story, delivered as one feature PR per repository (crt-process-builder, then clio-knowledge,
then clio) after this docs-only clio PR. The /bmad-spec form is used because the ticket is one story (AGENTS.md: fewer
than 5 stories); an ADR is added because the change alters the shared mapping service and the MCP tool contracts,
following the ENG-95984 File process parameter type precedent (a small feature in one story, its contract decisions
recorded in an ADR).

Baselines (2026-10-28): crt-process-builder `origin/main` `389198a0` (CrtProcessBuilder 1.6.6.125), clio
`origin/master` `358e9db52` (drift to `0b25e89e6` checked: nothing relevant moved, see the plan's drift note),
clio-knowledge `origin/master` `534e913` (drift to `a49f258`, `libraryVersion` 1.15.174), CrtProcessDesigner 7.8.0 and
the PackageStore corpus, core `TSBpm` trunk, a read-only inventory of stand `Creatio` (a registered clio environment).
Basis labels: *source* (read in code; a run-time claim with this label is a hypothesis until an EM measures it),
*measured* (observed on a stand or counted over the shipped corpus, with the date), *inference*.

Path aliases: `PB/` = crt-process-builder `packages/CrtProcessBuilder/Files/src/cs/`; `PD/` = PackageStore
`CrtProcessDesigner/branches/7.8.0/Schemas/`; `TS/` = core `TSBpm/Src/Lib/`; clio and clio-knowledge paths are
relative to their repository roots.

---

## Why

An agent building a business process through `create-business-process` / `modify-business-process` cannot put files
on a Send email element. clio refuses `email.attachments` today as an undeclared key
(`clio/Command/ProcessModel/ProcessBlockKeyCheck.cs`, the `EmailBlock` key set), and the package has no member for it.
Agents fall back to the designer. The Process file element (ENG-92719 File processing element) now produces files
(Object attachments, Generated report, Process parameter variant), and the commonest use of those files is to send
them by email (Academy use cases; corpus).

The obvious fix (a raw `addMapping` onto an attachment parameter) does not work: the attachment parameter does not
exist until something creates it, and a raw outer-only mapping saves green and then fails the element at run time
(source, traced; EM9).

## What the platform requires (the contract the package must meet)

- **One designer row per attachment.** An attachment is an element parameter `Attachments<N>` (a collection, Direction
  `In`) with exactly one nested item `Attachments<N>FileLocator` (a file) (`PD/ProcessFilesControlSchema/`; corpus).
- **The run time finds rows by name AND stamp**: regex `^Attachments\d+$` over the element's own top-level
  parameters whose `CreatedInSchemaUId` is the process schema, and reads each row's `<slot>FileLocator`
  (`TS/Terrasoft.Core/Process/ProcessUserTaskExtensions.cs`, `PD/EmailTemplateUserTask/EmailTemplateUserTask.cs`;
  source). A wrong stamp, a wrong item name or a missing outer binding saves green and then sends nothing or fails at
  run time.
- **All four mode combinations share one run-time path** (manual/auto x custom/template). Manual mode copies the files
  to the draft email activity (`ActivityFile`), so it can be proven without a mailbox; automatic mode copies them to
  the activity only with "Create activity" and otherwise attaches them to the outgoing message.
- **Feature `UseProcessEmailAttachments`** gates the designer control and the run time for the executing user. It is a
  rollout toggle on everywhere since 7.17.1 (measured on stand `Creatio` 2026-10-28; history in decisions Q9).
- **Files are read with system rights** at run time and sent outside the system (source).

Full key-by-key target: analysis `serialization` file; invariants: design section 3.

## Scope

In scope (package `PB/`, clio, clio-knowledge):

- New `email.attachments: [entry]` member on create (`elements[].email`), `addElement` and `setElement`
  (`elementUpdate.email`); entry = the `fileProcessing.files` shape (`processParameter`, `sourceElement` +
  `sourceElementParameter`, `expression`, and a declared-but-refused `value`) (Q1).
- Sources: anything that holds files - a collection with one FileLocator item (an element output such as
  `ObjectFiles`, `ReportFiles`, a sub-process FileCollection output, a process FileCollection), a collection item path,
  or a single File (Q2); a single File is accepted with a warning (Q3; EM8 must confirm it before the guidance states
  it, otherwise fall back to B: refuse and wrap the File in a FileCollection).
- Modify semantics: REPLACE (Q4); new slots take the lowest free number from 1, counted over every parameter of the
  element (Q5).
- Refusals and warnings of design section 5 (Q6, Q7, Q8, Q10, Q16, Q20).
- `describe-business-process` returns `email.attachments[]` in the write shape plus `slot`, `bindingLevels`,
  `issues` (Q11).
- clio: key check, floors, descriptions (paid by swaps, Q12), typed describe, read-back checks, capability map, curated
  knowledge re-pin, MCP e2e.
- clio-knowledge: new article `process-send-email-attachments` and the edits that route to it (Q12).

Out of scope (deliberately not built, design section 8):

- A flow-reachability REFUSAL (reachability is a warning only).
- `CollectionBindingRules.TakesFiles` membership for email slots (Q8 option B).
- Creatio.ai `Files<N>` slots (Q15: only the package comment is fixed).
- Attachments on a multi-instance Send email (Q16: refused; describe reports it with an issue).
- A build-time check of `UseProcessEmailAttachments` (Q9: one troubleshooting line in the guide instead).
- Recipient replace semantics; a `templateEntity` declaration-order guard (Q7 option B, a follow-up).
- Any change to `ProcessParameterService`'s type allow-list (element-owned slots never pass through it).
- A `CreateActivity` member on the `email` block.

## Contract

Write (all three routes reach one applier, `PB/Email/SendEmailApplier.cs`):

```json
"email": {
  "mode": "manual",
  "subject": "Your invoice",
  "body": "<p>Please find the invoice attached.</p>",
  "to": [ { "processParameter": "Contact" } ],
  "attachments": [
    { "sourceElement": "GenerateInvoice", "sourceElementParameter": "ReportFiles" },
    { "sourceElement": "ReadContractFiles", "sourceElementParameter": "ObjectFiles" },
    { "processParameter": "Documents" }
  ]
}
```

- Exactly one source per entry. `attachments` absent: the element's attachments are untouched. `attachments: []`:
  every attachment slot is removed.
- A sent list replaces the element's attachments: a slot whose stored binding equals an entry's planned binding is kept
  as it is (name, UIds, resources); the others are removed (refused while something reads them); unmatched entries get
  new slots. Matching is one to one. The removal notice carries each removed entry in the write shape.
- Not added: a removal op, a per-entry slot name, a caption.

Read back (`describe-business-process`, the element's `email` block):

```json
"attachments": [
  { "sourceElement": "GenerateInvoice", "sourceElementParameter": "ReportFiles",
    "slot": "Attachments1", "bindingLevels": "both" },
  { "processParameter": "Contract", "slot": "Attachments3", "bindingLevels": "itemOnly" }
]
```

Write members are decoded from the stored formulas, so an entry can be sent back unchanged; `slot`, `bindingLevels`
(`both` | `outerOnly` | `itemOnly` | `none`) and `issues` are read-back fields clio tolerates. Order: the designer's row order, the order the email attaches them (revised
2026-10-29, was slot number); an
element with no slot omits the member (null, not `[]`); `configured` counts attachments.

## Acceptance criteria

AC1-AC7 are the analysis README section 3 rows; the wording below is the proposed Jira text (decisions Q19: the lead
writes it into the description after the owner approves the wording), grouped under them.

- **AC1 - block and describe.** The `sendEmail` configuration accepts an `attachments` list; each entry maps one source
  of files (a collection of files or a single File) into one attachment row of the element;
  `describe-business-process` reads the list back in the same shape. A sent `attachments` list replaces the element's
  attachments; an empty list removes them.
- **AC2 - sources.** The files can come from any of the three Process file source variants or from a process
  FileCollection / File parameter, and one collection can feed several Send email elements.
- **AC3 - more than one.** The `sendEmail` can contain more than 1 file attachment.
- **AC4 - modes.** Attachments work for both **Send email manually** and **Send email automatically**, and for both
  custom-message and template-message modes; automatic sending is proven by a real delivery on a stand with configured
  mail.
- **AC5 - clear errors.** Building a Send email element with an attachment whose source is missing, holds no files, or
  is declared after the Send email element fails with a clear error naming the attachment entry and the source; a
  source that does not lie on a flow path before the Send email element produces a warning. A Process file source
  (Object attachments or Generated report) with no record condition and no filter is refused as an attachment source.
  Attachments on a multi-instance Send email are refused with a clear message.
- **AC6 - ordering rule.** Guidance states the ordering rule - the Process file element is configured before the Send
  email element that consumes its output and runs before it on the flow - and names the `UseProcessEmailAttachments`
  feature for troubleshooting an email that arrives without its files.
- **AC7 - parity, tests, docs, MCP.** Server serialization matches a designer-built capture covering 2+ attachments, a
  numbering gap and the Process parameter variant; tests, docs, and MCP surface updated.

AC -> design -> proof (extends analysis README section 3: it adds Q4 to AC1 and Q16 to AC5):

| AC | Design | Proof |
|---|---|---|
| AC1 | `email.attachments[]` (Q1), replace (Q4), describe in the write shape (Q11) | unit round trip; EM2, EM3 |
| AC2 | any source holding files (Q2); item found by type | unit per source kind; EM4, EM4c, EM11 |
| AC3 | one slot per entry; any rows per slot | unit; EM4 |
| AC4 | no mode-specific code; shared run-time path | unit x4; EM4, EM5, EM7 on stand `Creatio` after the owner configures mail (Q17) |
| AC5 | refusals of design section 5.1 (Q6, Q16, Q20) and the reachability warning | unit per refusal; e2e refusal creates nothing |
| AC6 | guidance states configure-first and flow-before; build guard (Q7) | KB tests; EM10 |
| AC7 | parity patterns A (corpus), B (EM1 designer capture), C (writer JSON) | parity suite; e2e Ignored = 0 |

## Gates and measurements

- **No package code before this SPEC and the ADR exist on the BMAD branch** (AGENTS.md); they need not be merged for
  slice A. This docs PR merges before the package PR leaves draft (Q14).
- **EM1** - a designer capture on the current platform (2+ rows, a numbering gap, the Process-parameter variant, a
  single File), the AC7 oracle: the corpus has only four one-row bindings from 2021-2022. Run by the lead in Claude in
  Chrome, the owner logs in (Q18). Slice A (behaviour-neutral refactors) and C1 may proceed on the agreed answers;
  EM1 must pass before P-B2/P-B3 (slot numbering, stamps, sources), and its rows feed the P-C5 parity pattern B.
- **STOP gate (EM2 + EM3)** right after slice B, on an experimental cut: the platform's pre-save gate and the
  designer's files control accept a server-built slot. On failure work stops, the cause goes to the owner, and the
  estimate is redone before slice C.
- **EM7** - real delivery (auto + custom, auto + template, the Create activity variant with `ActivityFile` rows) on
  stand `Creatio` once the owner says its mail sending works (Q17). EM6 is dropped.
- Other stand checks (EM4, EM4c, EM5, EM8, EM9, EM10, EM11, EM13, EM14, EM15) are listed with their gates in the
  measurements file. Every stand write, install and mail send waits for the owner's explicit yes.

## Constraints

- No future CrtProcessBuilder cut number appears in these documents; numbers live in the ENG-95985 claim comment
  (Q13), the tag and `descriptor.json`. Versions appear only as history ("measured on 1.6.6.125").
- The stand is named only as `Creatio` (a registered clio environment); its address is not written here.
- clio create/modify descriptions have about 2-3 bytes free and `process-modeling.md` 1 byte: every added sentence is
  paid by a swap; no ceiling raise (Q12). The 2-3 bytes were computed outside the repository (the facts row is marked
  inferred); the last figures recorded in `clio.tests/Command/McpServer/ToolContractPayloadBudgetTests.cs` are create
  35,054 and modify 35,021 bytes against the 35,072 ceiling. CL-4 re-measures on the branch with the ceilings lowered.
- Merge order (human merges only): package (tag on the remote) -> knowledge (published) -> clio (Q13).

## Success signal

An agent builds a process whose Send email element attaches a Process file element's files with one
`create-business-process` call; `describe-business-process` returns `email.attachments` in the write shape; sending the
described list back through `modify-business-process` writes nothing; on stand `Creatio` the files arrive in manual mode
(on the email activity) and in automatic mode (on the delivered message).

## Risks

See the plan section 10: EM2/EM3 failing (STOP gate), the description budget, mail on stand `Creatio` not configured in
time (AC4 open), a foreign cut on `main` during review, a KB `libraryVersion` collision, regressions from the shared
mapping-service guard (PU-42 mirror test), and the local core-bin being newer than CI's 8.3.4.
