# ENG-95985 Send email attachments: owner decisions

Written 2026-10-28. All twenty questions were answered by the owner on 2026-10-28; each row and section carries the answer. Answers are recorded in place ("**Agreed <date>.**"), the way ENG-92719 recorded Q1-Q20.

Basis: crt-process-builder `origin/main` `389198a0` (CrtProcessBuilder 1.6.6.125), clio `origin/master` `358e9db52`,
clio-knowledge `origin/master` `534e913`, CrtProcessDesigner 7.8.0 (PackageStore), core `TSBpm` trunk, the PackageStore
corpus, a read-only inventory of stand `Creatio` on 2026-10-28, and the research notes r0-r10 (local notes, not in the repository; see README).
Paths: `PB/` = `packages/CrtProcessBuilder/Files/src/cs/`, `PD/` = `CrtProcessDesigner/branches/7.8.0/Schemas/`,
`TS/` = `TSBpm/Src/Lib/`, `CLIO/` = clio repository root, `KB/` = clio-knowledge repository root.

Rule for arguing options (owner feedback): options are compared on clean-slate cost and constraints. The prototype
mentioned in the refinement comment is not an argument for or against any option.

## Summary

| # | Question | Agreed answer | Gates |
|---|---|---|---|
| Q1 | Contract member and entry shape | `email.attachments: [entry]`, entry = the `fileProcessing.files` shape **Agreed 2026-10-28.** | all package code |
| Q2 | Which sources are accepted | anything that holds files: a file collection (one FileLocator item) or a single File, from an element output or a process parameter **Agreed 2026-10-28.** | binder, AC2, AC5 text |
| Q3 | A single File as an attachment | accept (item-only binding, designer parity), with a warning that an empty File fails the element **Agreed 2026-10-28.** | binder, guidance; EM8 confirms |
| Q4 | Modify semantics | REPLACE: a sent list becomes the element's attachments; absent = untouched; `[]` removes all **Agreed 2026-10-28.** | slot writer, describe, guidance |
| Q5 | Slot numbering | lowest free number from 1 (designer parity) **Agreed 2026-10-28.** | slot writer, parity |
| Q6 | What AC5 means | refuse no source / unresolvable / not file-bearing / nested / declared later in `elements[]`; WARN when the source is not on a flow path before the email **Agreed 2026-10-28.** | applier, Jira AC wording |
| Q7 | How AC6 is enforced | guidance states both orders; the build path refuses a later-declared source for attachments only **Agreed 2026-10-28.** | build handler |
| Q8 | Raw `addMapping` onto a slot | refuse it, naming `email.attachments` (R6 precedent); `TakesFiles` unchanged **Agreed 2026-10-28.** | mapping service |
| Q9 | `UseProcessEmailAttachments` off | no build-time check; a troubleshooting line in the guide (a stale rollout toggle, on everywhere since 7.17.1) **Agreed 2026-10-28.** | guidance |
| Q10 | Mapping FROM a slot | refuse **Agreed 2026-10-28.** | mapping service |
| Q11 | Describe shape | `email.attachments[]` in the write shape, plus `bindingLevels` and `issues` **Agreed 2026-10-28.** | describe, clio DTO |
| Q12 | Byte budgets (clio descriptions, KB) | clio: one short clause paid by a swap; KB: new article `process-send-email-attachments` **Agreed 2026-10-28.** | clio PR, KB PR |
| Q13 | Cut claims and merge order | a claim comment on ENG-95985; merge package -> knowledge -> clio **Agreed 2026-10-28.** | every cut |
| Q14 | BMAD set | docs-only clio PR first: spec, ADR, story, test plan, tracker rows **Agreed 2026-10-28.** | package PR leaving draft |
| Q15 | Creatio.ai `Files<N>` promise in package code | fix the comment; Creatio.ai stays out of scope **Agreed 2026-10-28.** | package PR |
| Q16 | Out-of-AC patterns | `email.attachments` on a (designer-built) multi-instance Send email refused; per-recipient files through a multi-instance sub-process documented only if EM8 passes; `removeElement` guard only if EM11 shows a silent break **Agreed 2026-10-28.** | scope |
| Q17 | Where auto-mode attachments are proven | the owner configures real mail sending on stand `Creatio`; EM7 (real delivery, incl. the Create activity variant) runs there; EM6 is dropped **Agreed 2026-10-28.** | AC4 evidence |
| Q18 | Who runs the designer captures (EM1, EM3) | the lead through Claude in Chrome; the owner logs in **Agreed 2026-10-28.** | day 0 |
| Q19 | Jira AC wording | the lead writes the issue description only (the checklist field `customfield_10193` untouched) and reads it back; the text is approved by the owner first **Agreed 2026-10-28.** | Jira |
| Q20 | A Process file source with no record scope and no filter condition feeding an email | refuse, for Object attachments AND Generated report, judged at the end of the request **Agreed 2026-10-28.** | applier, filter ledger, security |

---

## Q1. Contract member and entry shape

**Question.** What does the agent send to put files on a Send email element?

**Facts.**
- The `email` block is applied by one applier on every route: build, `addElement`, `setElement`
  (`PB/Design/ProcessBuildHandler.cs:553-561`, `PB/Operations/ElementOperations.cs:64-70, 326-332`). [source]
- `fileProcessing.files` already describes "exactly one source of files": `processParameter`, `sourceElement` +
  `sourceElementParameter`, `expression`, and a declared-but-refused `value` (`PB/Contracts/ProcessDescriptorContracts.cs:416-440`).
  Its planner classifies a collection, a collection item, a single File and an expression, and refuses nested sources
  (`PB/FileProcessing/ProcessFileProcessingBinder.cs:130-195`). [source]
- One designer row is one slot `Attachments<N>` holding one collection (or one file) (`PD/ProcessFilesControlSchema/ProcessFilesControlSchema.js:254-295`). [source]
- The word "attachments" already names a SOURCE group of the Process file element (`fileProcessing.attachments`, the
  Object attachments variant). The new member is a CONSUMER. Guidance must keep them apart. [source]

**Options.**
- **A. `email.attachments: [entry]`, entry = the `fileProcessing.files` shape.** One entry per slot. The agent already
  knows the vocabulary from the Process file element. The binder can be shared (see the design file, component C2).
- B. `email.attachments: ["OF1", ...]` (element names only). Shorter, but cannot express a process parameter, an item
  path or an expression, and needs a default output per element kind.
- C. No block: let the agent use `addMapping` onto `Attachments<N>`. The slot does not exist until something creates
  it, so this needs a slot-creation operation anyway, and every refusal of AC5 lands in the generic mapping service.
- D. Another member name (`files`). Avoids the word clash, but the AC, the designer label ("Attachments") and the
  runtime prefix all say "attachments".

**Recommendation: A.** **Agreed 2026-10-28 by the owner.** The name matches the AC, the designer and the runtime. The clash with the source group is a
guidance problem, solved by one sentence in each article.

**Consequences.** New `[DataMember(Name = "attachments")]` on `SendEmailDescriptor` and on `DescribeSendEmailInfo`;
clio `ProcessBlockKeyCheck` gets the key and a child key set; the floor rises. An older package drops the unknown
member (`PB/Contracts/VersionContracts.cs:24-26`): a block that carries ONLY `attachments` is then refused as
configuring nothing (`PB/Email/SendEmailApplier.cs:203-216`), and a block with other members saves green without the
attachments, which is the silent case the floor and clio's read-back check exist for.

---

## Q2. Which sources are accepted

**Question.** AC2 names "the three Process file source variants". Is that the whole list?

**Facts.**
- The designer's picker for the attachment item offers, on the element tab, items of element output collections and
  single FileLocator outputs (a whole element collection is listed but cannot be selected, and `In` parameters are
  dropped); on the process tab, a File parameter, an item of a process FileCollection, and the process FileCollection
  itself (`PD/ProcessElementParametersMappingPage/ProcessElementParametersMappingPage.js:506-509, 532-538, 880-883`; facts section 3). [source]
- The runtime reads every row's `<slot>FileLocator` and does not care where the collection came from
  (`PD/EmailTemplateUserTask/EmailTemplateUserTask.cs:107-119, 280-287`). [source]
- The Academy describes the attachment field as taking a file collection, normally produced by a Process file
  element (research r6 §2.2). [doc]
- The corpus has only Object and Report sources (research r5 §3.2). [corpus]

**Options.**
- **A. Anything that holds files**: a collection with exactly one FileLocator item (an element output such as
  `ObjectFiles`, `ReportFiles`, a sub-process output, or a process FileCollection), a collection item path
  (`OF1.ObjectFiles.File`), or a single File (process File parameter, an element's File output).
- B. Only the outputs of the three Process file variants.

**Recommendation: A.** **Agreed 2026-10-28 by the owner.** It is designer parity and costs nothing extra: the shared binder already classifies these
shapes. The three Process file variants stay the TESTED set for AC2. A process FileCollection is how a file reaches
an email from a parent process or a page (Academy use case UC4).

**Consequences.** AC5 wording changes from "no upstream file source" to "a source that holds no files" (Q6, Q19).

---

## Q3. A single File as an attachment

**Question.** Accept a single File (item-only binding, outer slot unbound)?

**Facts.**
- The designer stores exactly that for a single File: the item is bound, the parent stays as created (None)
  (`PD/MappingEditMixin/MappingEditMixin.js:1090-1094`; research r10 §3d). [source]
- At run time the parameter reader builds ONE row for an unbound collection whose item is bound
  (`TS/Terrasoft.Core/Process/ProcessInstanceParametersDataReader.cs:443-452`). Traced, not measured for the email.
  The same shape on the Process file element copied one file (measured 2026-10-19, recorded at
  `PB/FileProcessing/ProcessFileProcessingBinder.cs:27-29`). [prior measured]
- An EMPTY File gives a row with a null locator, and the email element then fails with a NullReferenceException in
  `FileFactory.CreateFileInstance`, which `IgnoreErrors` does not cover (`TS/Terrasoft.Common/CompositeObject.cs:120-127`,
  `TS/Terrasoft.File/FileFactory.cs:44-45`). Traced, not measured. [source]

**Options.** **A. Accept, with a warning** ("if '<File>' is empty when the email runs, the element fails"). B. Refuse
and tell the agent to wrap the File in a FileCollection.

**Recommendation: A** **Agreed 2026-10-28 by the owner.**, confirmed by EM8 before the guidance states it. If EM8 shows the item-only row does not
attach, fall back to B.

---

## Q4. Modify semantics

**Question.** What does `setElement` with `email.attachments` do to the slots already on the element?

**Facts.**
- Recipients are match-or-append: no way to remove or replace one through the tool (`PB/Email/SendEmailApplier.cs:798-818`);
  the guidance routes removal to the designer (`KB/guidance/mcp/guides/processes/send-email.md`). The refinement called
  this out as "discovered late last time". [source]
- The designer removes a slot by clearing its row and saving (`ProcessFilesControlSchema.js:376-421`). [source]
- No mapping or Script value can read an element `In` parameter while `Feature-UseVerificationOfProcessParameterDirection`
  is on (`TS/Terrasoft.Core/Process/ParameterValuesValidationRule.cs:252-277`, gated at `:282`; default true,
  `GlobalAppSettings.cs:343`, but configurable). The designer still checks `canRemoveParameter` before it removes a slot
  (`PD/ProcessFilesControlSchema/ProcessFilesControlSchema.js:396-403`), so the package checks dependents too
  (design 2.3 step 4). [source; that nothing reads a slot in practice is inferred]
- Modify has no concurrency token (the modify contract, `PB/Contracts/ModifyContracts.cs`, has no expected-version member), so a list built
  from a stale describe deletes rows added since. [source]
- Replace-the-list precedents in the package: access rights (`PB/AccessRights/ChangeAccessRightsApplier.cs:526-545`). [source]

**Options.**
- **A. Replace.** A sent `attachments` list becomes the element's attachments. Slots whose resolved source equals
  an entry are kept as they are (names, UIds, resources unchanged); the rest are removed; new entries take new slots.
  `attachments` absent = untouched; `[]` removes every slot.
- B. Match-or-append, like recipients. No removal path.
- C. Append plus a new removal op (`removeAttachment`). Costs a new op token: bytes in the 19,000 B short-form bundle and
  the op drift tests (research r7 §2).

**Recommendation: A.** **Agreed 2026-10-28 by the owner.** Describe returns the same list (Q11), so describe -> edit -> send back is the modify recipe.
It is idempotent by construction. It differs from recipients inside the same block, where an agent naturally sends
only the new item; three things carry that risk: the removal notice names every removed entry in the write shape
(one call restores it), clio's read-back says how many were removed, and the modify description says "to add one,
send the described list plus the new one". The guide adds: describe immediately before sending. A later ticket may
move recipients to the same rule.

---

## Q5. Slot numbering

**Facts.** The designer takes the lowest free number from 1 (`PD/ProcessSchemaUserTaskUtilities/*.js:465-479`,
called from `ProcessFilesControlSchema.js:244-260`). The package's recipients take max+1
(`PB/Email/SendEmailApplier.cs:854-867`), and its doc comment wrongly says "first free". The runtime does not care
(regex `^Attachments\d+$`, `TS/Terrasoft.Core/Process/ProcessUserTaskExtensions.cs:19, 78-98`). [source]

**Options.** **A. Lowest free from 1.** B. max+1.

**Recommendation: A** **Agreed 2026-10-28 by the owner.**, counted the designer's way: the lowest k such that NO parameter of the element (every
parameter of the element, nested items included, whatever its `CreatedInSchemaUId`) is named `Attachments<k>`. It is what the AC7 capture will show after a gap (EM1). With
Q4 = A, kept slots keep their names, so only new slots are numbered.

---

## Q6. What AC5 means

**Question.** "Fails with a clear error naming the missing source": which failures?

**Facts.**
- Named refusals already exist for an unknown element, parameter or process parameter
  (`PB/ProcessSchemaElementLocator.cs:60-66, 247-252, 262-268`). [source]
- The `files` planner has the "names no source", "neither a file nor a collection of files", "with no File item" and
  nested-source refusals (`PB/FileProcessing/ProcessFileProcessingBinder.cs:136-187, 209-225`). [source]
- The build path's declaration-order guard ("a source element must appear EARLIER in `elements[]`") is not wired to
  the `email` block and scans top-level values only (`PB/Graph/ProcessGraphBuilder.cs:356-430, 452-455`;
  `PB/Design/ProcessBuildHandler.cs:557-561`). [source]
- No check in the package says that an attachment's SOURCE lies upstream of its consumer. The only flow traversal is
  `ValidateStructure`'s reachability from a start and to an end, a breadth-first `Traverse` over adjacency maps, run on
  the create path (`PB/Graph/ProcessGraphBuilder.cs:2228-2230`; `Traverse` itself, private, `:2250-2270`). [source]
- At run time a source element that has not run yields 0 attachments and no error
  (`TS/Terrasoft.Core/Process/ProcessInstanceParametersDataReader.cs:454-468`; traced, not measured: EM10). [source]

**Options.**
- **A. Refuse:** (i) an entry with no source; (ii) an unresolvable element, parameter or item; (iii) a source that holds
  no files (not a File, not a collection with a FileLocator item); (iv) a nested collection; (v) on the build path, a
  source element declared after the Send email element in `elements[]`. Every message names `email.attachments[<i>]`
  and the source.
- B. A plus a flow REFUSAL (the source element must lie on a path before the email). Cheap to build over the existing
  `Traverse`, but wrong as a refusal: a source on another gateway branch can reach the email and still not run, and a
  loop can make an "after" source run first.
- C. A plus a flow WARNING built on `Traverse`, on build and on modify.

**Recommendation: C.** **Agreed 2026-10-28 by the owner.** The refusals of A, and a warning when the source cannot reach the email along sequence flows.
The flow rule also goes into guidance (Q7) with EM10's measured result.

---

## Q7. How AC6 is enforced

**Facts.** On build, every `fileProcessing` block is configured before any `email` block (the configure loop
`PB/Graph/ProcessGraphBuilder.cs:238-243`, inside `BuildGraph`, which `PB/Design/ProcessBuildHandler.cs:166` calls before
`ApplyDeclarativeContent` `:172`, whose `email` pass is `:557-561`), so "configured before" holds
by construction on build; on modify, operations run in order and a missing element is refused. The real hazard is
FLOW order (EM10). The Academy states the configuration-order rule (research r6 §2.4). [source] [doc]

**Options.** **A. Guidance states both orders; the build path refuses a later-declared source for `attachments`
only.** B. Same, and also wire the guard to `templateEntity` (closes an existing gap, research r1: a behaviour change
outside attachments). C. Guidance only.

**Recommendation: A.** **Agreed 2026-10-28 by the owner.** B is a separate fix with its own notice text; propose it as a follow-up task.

---

## Q8. Raw `addMapping` onto a slot

**Question.** What happens when an agent writes `addMapping` onto `Attachments<N>` or its item, instead of using
the block?

**Facts.**
- Today `CollectionBindingRules.TakesFiles` is false for a Send email slot, so a raw outer mapping binds the outer
  level only, with no notice (`PB/Mappings/CollectionBindingRules.cs:164-181`). At run time that row reads a null
  locator and fails the element (traced, research r10 C12; EM9). [source]
- `TakesFiles` is reached by every structured mapping of every element and by every describe
  (`PB/Mappings/CollectionBindingRules.cs:93, 361, 396`; research r2 §7). [source]
- Precedent: a raw mapping onto a block-owned Process file parameter is refused, naming the block member
  (`PB/Mappings/ProcessMappingService.cs:416-435`). [source]

**Options.**
- **A. Refuse every non-block mapping onto `Attachments<N>` and `Attachments<N>FileLocator` of a Send email
  element**, naming `email.attachments`. All mapping entry points plan in one method (`Plan`), and `fromBlock` is the
  only switch, so the guard sits under `!fromBlock` and `BindCollection` gains a block form for the applier
  (design 4.5). `TakesFiles` does not change.
- B. Widen `TakesFiles` so P2 pairs the item and R-M2 refuses a fileless source. Touches the shared rules of every
  mapping; PU-42 stays green only with a narrow predicate.

**Recommendation: A.** **Agreed 2026-10-28 by the owner.** One route, one set of checks, the smallest blast radius. A designer-made slot is edited
through the block too (Q4).

---

## Q9. `UseProcessEmailAttachments` off

**Facts.** The feature gates the designer control and the RUN TIME (`PD/EmailTemplateUserTaskSender/EmailTemplateUserTaskSender.cs:227`,
`PD/EmailTemplateUserTask/EmailTemplateUserTask.cs:207-210`). With it off for the EXECUTING user the email (or, in
manual mode, the email activity, `PD/ManualEmailUserTaskSender/ManualEmailUserTaskSender.cs:56-70, 163-181`) has no
files, silently. The package ships it on through `AdminUnitFeatureState` rows for "All employees" and "All
external users"; stand `Creatio` has both (measured 2026-10-28). A feature-reader seam exists
(`PB/FileProcessing/FileProcessingFeatureReader.cs`), but it asks without a user, and a request without a user
gets the feature's `DefaultState` (`TS/Terrasoft.Core/FeatureToggling/DbFeatureProvider.cs:810-827`), which this
feature's package data does not set; option A below would have needed a reader that asks for the current user, and
none is built (design 4.8). [source] [measured]

**History of the toggle (researched 2026-10-28 at the owner's request; SVN `PackageStore/CrtProcessDesigner`, Jira).**
- Added 2020-11-19 (r194794) with the feature itself, task CRM-58692 "Реализовать возможность прикреплять вложенные
  файлы к письмам ... элемента «Отправить email»" (epic ENG-64351), and switched ON six days later, 2020-11-25 (r195148,
  "включена фича"), through `AdminUnitFeatureState` rows for "All employees" and the portal users (renamed "All
  external users" in r258827, 2023-04-03). Shipped in 7.17.1 (released 2021-01-14). A classic rollout toggle. [measured, svn]
- Never switched off or scheduled for removal: Jira has no issue about it besides ENG-95985; no package in PackageStore
  references it outside CrtProcessDesigner; no data row sets it to 0. [measured]
- Kept alive by later refactors rather than by a decision: the run-time sender check dates from 2020-11-23 (r195004),
  the activity-copy check was carried into the code written for RND-10941 (r204435, 2021-06-11), and the designer check
  was re-written as a `getIsFileControlEnabled` override when the files control became generic for the Creatio.ai
  element (RND-72316, r336217, 2025-06-03). [measured, svn blame]
- The shipped state rows force-update `FeatureState` and `SysAdminUnit` (`IsForceUpdate: true` in
  `PD/../Data/AdminUnitFeatureState_UseProcessEmailAttachments/descriptor.json`), so every install of the package resets
  the two group rows to ON; switching it off durably is possible only for individual users or other roles. [source]
- Stand `Creatio`: the `Feature` row has `DefaultState = 1` (set by the platform on 2022-12-03, not by the package data,
  which carries Code and Id only), both group rows 1, no row with state 0. So even a context-free read answers ON
  here; the earlier inference that a context-free read returns OFF does not hold on this stand. [measured 2026-10-28]

**Options.** A. Warning notice when the feature is off for the building user (a per-user reader seam, C8). B. Refuse
the build (the refinement's proposal). **C. No build-time check: one troubleshooting line in the guide** ("an email
without its files: check the feature UseProcessEmailAttachments for the user who runs the process").

**Recommendation: C** **Agreed 2026-10-28 by the owner.** (changed after the history above). The toggle is a five-year-old rollout switch that is on by
default, on for every employee and external user, re-asserted by every install, and never switched off by any
package or ticket. A build-time check would cost a reader seam, its tests and a stand check for a state that does not
occur in practice, and it could only see the building user anyway. The run time still honours the switch, so the
guide names it for troubleshooting.

---

## Q10. Mapping FROM a slot

**Facts.** A slot is an `In` parameter; the platform's pre-save gate refuses an element `In` parameter as a mapping
source (`InvalidParameterMappingDirection`, `TS/Terrasoft.Core/Process/ParameterValuesValidationRule.cs:252-277`),
only while `Feature-UseVerificationOfProcessParameterDirection` is on (`:282`; default true,
`TS/Terrasoft.Core/GlobalAppSettings.cs:343`), and counts it as an error while `Feature-UseInterpretableProcessOnly` is
on (`TS/Terrasoft.Core/Process/ProcessValidationResult.cs:44-54`; default true). [source]

**Recommendation: refuse in the package** **Agreed 2026-10-28 by the owner.**, with a message that names the slot and says it only receives files. The
platform would refuse it anyway under the default settings, with a less helpful message.

---

## Q11. Describe shape

**Facts.** `SendEmailElementHandler.DescribeRecipients` projects without decoding sources and skips a slot whose own
value is None (`PB/Elements/SendEmailElementHandler.cs:228-255`). `ProcessFileProcessingDescriber` already reports
`bindingLevels` and issues for the `files` binding (`PB/FileProcessing/ProcessFileProcessingDescriber.cs:26-72`).
clio's `DescribedEmail` has an extension bag that passes an unknown `attachments` member through
(`CLIO/clio/Command/ProcessModel/IProcessDescriber.cs:1240-1249`). [source]

**Options.** **A. `email.attachments[]` entries in the write shape** (re-sendable as they are), each with `slot`,
`bindingLevels` and `issues` as read-back fields. B. Only the generic element `parameters[]`.

**Recommendation: A.** **Agreed 2026-10-28 by the owner.** It is the modify recipe of Q4 and the evidence AC1 asks for.

---

## Q12. Byte budgets

**Facts (computed, not run; research r7 §2, §4.2).** clio full contracts: create ~35,070 B and modify ~35,069 B against
a 35,072 B ceiling; describe ~155 B free. KB: `process-modeling.md` 1 B of inline headroom, the digest card ~2 B,
`send-email.md` 17.5 KB on the wire (the inline-spill zone starts near 19 KB). [computed]

**Options (clio).** **A. One short clause in create (`attachments?` in the email shape + a pointer to the guide),
one line in modify (the replace rule), one read-back line in describe, paid by swapping out restatements the KB
already owns** (precedent: ENG-92719 "File processing element" decision Q20, the budget trim rule). B. Raise `MaxToolContractSerializedBytes` by 256 B.

**Options (KB).** **A. New article `process-send-email-attachments`** (precedent `process-send-email-template`),
reached from `send-email.md`, `files.md`, `element-catalog.md`; one route line in `process-modeling.md` paid by a swap.
B. A section inside `send-email.md` (no curated-fixture re-pin, but the article reaches the spill zone).

**Recommendation: A and A.** **Agreed 2026-10-28 by the owner.** The measured sizes are re-read on the branch first (lower the ceiling temporarily and
read the failure, as earlier tickets did), including the seven-contract default lookup on the wire
(`MaxInlineWireReplyBytes = 19_000`): its recorded 18,815 B is stale since a later commit added text to that bundle.

---

## Q13. Cut claims and merge order

**Facts.** The shared claim list (ENG-92719 comment 550565) is on a Closed ticket and lacks the last two cuts made by
another ticket (research r9 §4.6). `git ls-remote --tags` in the package repository is the authoritative check at cut
time. The File family merged package -> knowledge -> clio within minutes ("guide-first"). A clio built from
2026-10-13 onward (when `ProcessBlockKeyCheck` was added) refuses `email.attachments` loudly; an older clio 8.1.0.x
forwards it, the published library still reaches that clio (`KB/bundle-source.json` serves clio 8.1.0-8.1.999), and
an older package then drops the member with success (`PB/Contracts/VersionContracts.cs:23-26`). For that window the
article's floor sentence is the only defence. [source; the build window is inferred from clio's git history]

**Recommendation.** **Agreed 2026-10-28 by the owner.** Claim every number in a new claim comment on ENG-95985 after checking the tags of both
repositories; plan docs never name a number. Merge order: package (tag pushed) -> knowledge -> clio.

---

## Q14. BMAD set

**Facts.** ENG-95985 has no story id in `CLIO/spec/sprint-status.yaml` (research r9 §4.1). AGENTS.md requires a PRD
or SPEC and an ADR before code and a story before a PR. The first ENG-95985 clio PR also inherits the "done" flip of
`story-eng-92719-file-processing-element-3` (`CLIO/spec/sprint-status.yaml:4919`). [source]

**Recommendation.** **Agreed 2026-10-28 by the owner.** A docs-only clio PR first: `spec/prd/spec-eng-95985-send-email-attachments.md`,
`spec/adr/adr-eng-95985-send-email-attachments.md`, `spec/stories/story-eng-95985-send-email-attachments-1.md`,
`spec/test-plans/tp-eng-95985-send-email-attachments.md`, tracker rows, the inherited flip (only boxes that are really
done), and this folder (stand address already absent). It merges before the package PR leaves draft.

---

## Q15. Creatio.ai promise in package code

**Facts.** `PB/Mappings/CollectionBindingRules.cs:164-169` says the email and Creatio.ai attachment slots join
`TakesFiles` "with ENG-95985". The ENG-92719 decision D22 (agreed 2026-10-07) puts Creatio.ai out of scope. With
Q8 = A the email slots do not join either. [source]

**Recommendation.** **Agreed 2026-10-28 by the owner.** Rewrite the comment in the package PR: email slots are refused as raw targets (Q8), Creatio.ai
`Files<N>` slots are not buildable.

---

## Q16. Out-of-AC patterns

- **Multi-instance Send email.** The package makes only a Sub-process multi-instance
  (`PB/Elements/MultiInstanceApplier.cs:1051-1058`). The mapping rules' `IsMultiInstance`
  (`PB/Mappings/CollectionBindingRules.cs:194-195`) answers false for a designer-built multi-instance Send email, so
  mappings onto one get the single-instance rules.
  The Process Designer CAN make a Send email multi-instance (checked 2026-10-28 at the owner's request): mapping a
  TOP-LEVEL parameter of any activity, for example a recipient, to an ITEM of a collection fires
  `collectionMappingSet` (`PD/MappingEditMixin/MappingEditMixin.js:981-995, 1029-1035`), and the base properties page
  converts the element (`PD/ProcessFlowElementPropertiesPage/ProcessFlowElementPropertiesPage.js:846, 875-888`);
  `convertToMultiInstance` lives on the client activity class that user tasks extend
  (`TS/Terrasoft.Nui/Resources/Terrasoft/manager/process-flow-element-schema-manager/process-activity-schema.js:526`),
  and the server keeps `MultiInstanceOptions` on every activity (`TS/Terrasoft.Core/Process/ProcessSchemaActivity.cs:22, 122`;
  a user task is a `ProcessSchemaTask : ProcessSchemaActivity`). The conversion MOVES every root parameter of the
  element into the item properties of `InputRecordCollection` / `OutputRecordCollection` (`process-activity-schema.js:317-338`),
  so an `Attachments<N>` slot would become a nested item, which the run time's top-level slot search
  (`TS/Terrasoft.Core/Process/ProcessUserTaskExtensions.cs:78-98`) does not see [traced, not measured]. Nobody uses it:
  the corpus holds 61 multi-instance elements, all Sub-processes, and no multi-instance user task at all
  (scan of every PackageStore `metadata.json` for `BP6`, 2026-10-28). [source] [corpus]
  **Recommendation (changed): refuse `email.attachments` on a multi-instance Send email** **Agreed 2026-10-28 by the owner.** ("attachments of a
  multi-instance Send email are not supported through this tool"), detected by `MultiInstanceOptions` on the activity;
  describe reports such an element as found, with an issue. The supported per-recipient pattern stays the
  multi-instance Sub-process below.
- **One file per recipient** (Academy UC1 variant): a multi-instance sub-process whose callee has a File `In`
  parameter feeding its Send email as a single File (Q3). Documented in the guide only if EM8 passes.
- **`removeElement` of a source.** No dependency guard exists for `removeElement`
  (`PB/Graph/ProcessGraphBuilder.cs:1882-1919`). If EM11 shows the platform saves a dangling reference green, propose a
  follow-up; otherwise the platform's own refusal is enough.

---

## Q17. Where auto-mode attachments are proven

**Facts.** Manual mode copies the files to `ActivityFile` without a mailbox. Auto mode creates an Activity (and copies
the files) only with `CreateActivity = true`, a non-empty sender and no existing `ActivityId` (a re-executed element
re-sends its stored Activity and copies nothing,
`PD/AutoEmailUserTaskSender/AutoEmailUserTaskSender.cs:120-133, 165-174`); the builder resolves `sender` against
`MailboxSyncSettings`, and stand `Creatio` has 0 rows (measured 2026-10-28). The `email` block has no
`CreateActivity` member, so the Create activity probe (EM7c since EM6 was dropped) builds the slots with the builder
and sets `CreateActivity` by a raw constant mapping or in the designer: the slots under test stay builder-made. Real delivery needs a working mailbox. [source] [measured]

**Options.** **A. EM6 on stand `Creatio` with one MailboxSyncSettings record** created for the test (an environment
change, owner's yes; the send fails and `IgnoreErrors` swallows it, the Activity and its files remain), **plus EM7 on
a stand with a working mailbox the owner names.** B. EM6 only; real delivery stays unproven (the shared code path is
the argument). C. Skip auto mode evidence.

**Recommendation: A**, with B as the fallback if no mailbox stand is available; the PR says which one ran.

**Decided 2026-10-28 by the owner, differently:** the owner configures stand `Creatio` to really send mail. EM7 (real delivery: auto + custom, auto + template, and the Create activity variant that leaves ActivityFile rows) runs on stand `Creatio` once the owner says the mailbox works; EM6 (a test-only MailboxSyncSettings record) is dropped. Until then auto mode rests on the unit tests and the shared run-time path.

---

## Q18. Who runs the designer captures

**Recommendation.** The lead builds EM1 and runs EM3 through Claude in Chrome; the owner logs in. **Agreed 2026-10-28.** The owner may
prefer to build EM1 by hand (about 30-45 minutes); then the lead reads the metadata.

---

## Q19. Jira acceptance-criteria wording

**Decided 2026-10-28 by the owner: the lead edits the AC** (the owner first chose to edit by hand, then changed it).
Only the issue `description` is written; the HeroCoders checklist field (`customfield_10193`) is never touched, which
is the field an API write breaks. The lead reads the description back after the write and compares every bullet.
The table below is the first proposal; the final text, updated for the agreed Q9, Q16, Q17 and Q20, follows it and
is written only after the owner approves that text.

Proposed changes:

| AC | Today | Proposed |
|---|---|---|
| AC2 | "any of the three Process file source variants" | add: "or a process FileCollection / File parameter" (Q2) |
| AC5 | "no upstream file source fails with a clear error naming the missing source" | "a source that is missing, holds no files, or is declared after the Send email element fails the build with an error naming the attachment entry and the source" (Q6) |
| AC6 | "configured before" | add: "and runs before it on the flow; what a source that has not run yields is stated as EM10 measured it" (traced today: no files and no error; the outcome is filled in after EM10) |
| AC7 | "matches a designer-built capture" | add: "the capture covers 2+ attachments, a numbering gap and the Process-parameter variant; delivery is proven by ActivityFile rows (manual mode, and auto mode with Create activity on) and, where a mailbox stand is available, by a received email" |
| new | - | "Attachments are sent only while feature UseProcessEmailAttachments is on for the executing user; the build warns when it is off for the building user" (Q9) |
| new | - | "A sent attachments list replaces the element's attachments; an empty list removes them" (Q4) |
| new | - | "A Process file source (Object attachments or Generated report) with no record condition and no filter is refused as an attachment source" (Q20) |

**Final AC text for the ENG-95985 description** (replaces the "Acceptance criteria" list; the user-story sentence above
it stays as it is). Differences from the table: no feature warning (Q9 agreed: no build-time check, a guidance line
instead), the multi-instance refusal (Q16), real delivery on a stand with configured mail (Q17), both Process file
variants in the unscoped-source refusal (Q20).

```
### Acceptance criteria

* The `sendEmail` configuration accepts an `attachments` list; each entry maps one source of files (a collection of files or a single File) into one attachment row of the element; `describe-business-process` reads the list back in the same shape.
* The files can come from any of the three Process file source variants or from a process FileCollection / File parameter, and one collection can feed several Send email elements.
* The `sendEmail` can contain more than 1 file attachment.
* Attachments work for both **Send email manually** and **Send email automatically**, and for both custom-message and template-message modes; automatic sending is proven by a real delivery on a stand with configured mail.
* A sent `attachments` list replaces the element's attachments; an empty list removes them.
* Building a Send email element with an attachment whose source is missing, holds no files, or is declared after the Send email element fails with a clear error naming the attachment entry and the source; a source that does not lie on a flow path before the Send email element produces a warning.
* A Process file source (Object attachments or Generated report) with no record condition and no filter is refused as an attachment source.
* Attachments on a multi-instance Send email are refused with a clear message.
* Guidance states the ordering rule — the Process file element is configured before the Send email element that consumes its output and runs before it on the flow — and names the `UseProcessEmailAttachments` feature for troubleshooting an email that arrives without its files.
* Server serialization matches a designer-built capture covering 2+ attachments, a numbering gap and the Process parameter variant; tests, docs, and MCP surface updated.
```

---

## Q20. An unscoped record source feeding an email

**Question.** What happens when an attachment entry reads a Process file Object attachments element that has no
record condition and no selecting filter?

**Facts.**
- The run time reads attachment files with rights disabled (`PD/EmailTemplateUserTask/EmailTemplateUserTask.cs:76-104`)
  and sends them outside the system. [source]
- Such an Object element reads "up to N arbitrary files". Saving them is refused (ENG-92719 decision D16, enforced in
  `PB/FileProcessing/FileProcessingFilterLedger.cs:75-95`); using them in the process gets only a notice, raised by the
  element's own describe; no notice fires when a later request wires the element into an email
  (`PB/FileProcessing/ObjectFileProcessingNoticeRule.cs:62-104`). [source]
- Authority is unchanged: the builder needs the same process-design rights as the designer. [source]
- The file selection itself applies no record rights: the Process file element builds its query with
  `UseAdminRights = false` (`PD/FileProcessing/FileProcessing.cs`, `InitializeEntitySchemaQuery`). [source]
- The Process Designer has NO such check anywhere (checked 2026-10-28 at the owner's request): the Object attachments
  page validates only that the source object is chosen and that "Read first" is 1-5000 (default 50,
  `PD/ProcessSchemaUserTaskUtilities/ProcessSchemaUserTaskUtilities.js:506-518`,
  `PD/ObjectFileProcessingUserTaskPropertiesPage/ObjectFileProcessingUserTaskPropertiesPage.js:353-357`); its filter
  panel only OFFERS a `RecordId` condition (`:423-438`) and never requires one, in either action; the Report page has
  only confirmation dialogs; the Send email files control checks only that the row's mapping is not empty
  (`ProcessSchemaUserTaskUtilities.js:489-498`); the run time runs an empty filter as "first N files"
  (`PD/ObjectFileProcessingUserTask/ObjectFileProcessingUserTask.cs` `InternalExecute`). D16 was already a
  builder-only rule, stricter than the designer, because an agent leaves out a condition it was never shown. [source]

**Options.** **A. Refuse the entry**, naming the missing record condition. B. Warn. C. Nothing.

**Recommendation: A**, the same line D16 draws for saving: an email is the more exposed destination. **Agreed 2026-10-28 by the owner.**
Scope and mechanism agreed with it:
- **Both variants.** Object attachments with no record scope and no selecting filter, AND Generated report with no
  selecting filter (one report per record of the object, for example every invoice, mailed to one recipient). The
  Process parameter variant has no record condition and is not judged.
- **The existing rules, the existing ledger.** The refusal is the D16 empty-filter judgement already in the package:
  `ObjectFileProcessingEmptyFilterRule` (today it refuses only a SAVING element) and
  `ReportFileProcessingEmptyFilterRule` (it already refuses an unscoped MS Word element whatever its action), run by
  `FileProcessingFilterLedger.EnsureSelectingFilters` at the END of the request. An attachment entry that reads a Process
  file element tracks that element in the ledger, and the Object rule also refuses an element that a Send email slot
  reads. Judged at the end of the request, the order of operations in a modify batch does not matter, and a later
  `setFilter` / `clearFilter` that leaves the source unscoped while an email reads it is refused too.
- **Not caught, deliberately:** a filter that selects something but spans every record (by file name only, for a
  company price list): that is the user's intent. A designer-built process keeps working; describe reports the binding
  with an issue.
