# SPEC: File process parameter type (ENG-95984)

**Jira**: ENG-95984 File process parameter type (Task; epic ENG-92704 Create BP via AI Toolkit)
**ADR**: [adr-eng-95984-file-parameter-type.md](../adr/adr-eng-95984-file-parameter-type.md)
**Stories**: [1](../stories/story-eng-95984-file-parameter-type-1.md) (the whole ticket) · follow-up Sub-tasks
[2](../stories/story-eng-95984-file-parameter-type-2.md) · [3](../stories/story-eng-95984-file-parameter-type-3.md) ·
[4](../stories/story-eng-95984-file-parameter-type-4.md)
**Test plan**: [tp-eng-95984-file-parameter-type.md](../test-plans/tp-eng-95984-file-parameter-type.md)
**Analysis**: the full analysis (`spec/eng-95984-file-parameter-type/`, `spec/eng-92719-file-processing-element/`)
lands with the docs PR titled `ENG-92719 File processing element`. This set does not depend on it: every rule it relies
on is restated here; the analysis holds the evidence.
**Status**: Accepted. The acceptance criteria and the contract were agreed by the owner on 2026-10-07 and 2026-10-08.
Two items are open (see "Open owner items"): O-TP1 (where the e2e cases on the Process-parameter variant's `Files`
run) and, recorded on 2026-10-14, whether a single-instance sub-process's FileCollection input joins the
file-consuming targets (measurement first).
Q20 (where the description bytes come from) was agreed on 2026-10-14 as A: trim existing text, reviewer-approved.
**Created**: 2026-10-13
**Size estimate**: one story for the ticket, delivered as one feature PR per repository (CrtProcessBuilder, then clio,
then clio-knowledge). The /bmad-spec form is used because the ticket is one story (AGENTS.md: fewer than 5 stories);
an ADR is added because the change alters the shared mapping funnel and the MCP contract (owner decision O-10,
2026-10-07: this docs-only set merges before the package PR leaves draft).

Baselines: CrtProcessBuilder `main` at 1.6.6.96, clio `master` `19e6f9c53` (bundles CrtProcessBuilder 1.6.6.96, the
three process-designer floors at 1.6.6.92), clio-knowledge `master` at libraryVersion 1.15.142; re-checked on
2026-10-13. Stand measurements of 2026-10-02 ran on CrtProcessBuilder 1.6.6.54. Platform claims were read in Creatio
core 10.1.37. Basis labels: *source* (read in code; a runtime claim with this label is a hypothesis), *measured*
(observed on a stand or counted over the shipped corpus, with the date), *inference*.

---

## Why

An agent that builds a business process through `create-business-process` / `modify-business-process` cannot declare
a parameter that carries a file: every file spelling is refused. The Process file element (ENG-92719 File processing
element) takes its files from a process parameter in its Process-parameter variant, and a multi-instance sub-process
per file needs a callee with a file parameter, so neither can be built without this ticket.

The obvious fix does not work. The platform type NAME `"File"` is `FileDataValueType` `BA40CFC5` ("File (BLOB)"), a
type no designer page offers; Binary `B7342B7A` is a Stream type the flow-engine state store cannot hold (it throws on
one path and silently drops the value on the writer path). Shipped content holds 0 Binary or BLOB process parameters
and 50 FileLocator occurrences in 21 metadata files (measured, corpus, 2026-10-01). The file-reference type is
**FileLocator** `A33C9252-D401-453E-949D-169157067ED9`.

Admitting the type is the small half. A file collection is the generic `CompositeObjectList` `651ec16f` with one
FileLocator item, and the runtime rebuilds a collection's rows from the TARGET's own items: a collection bound only at
its outer level saves green, gives rows `{File: null}` and fails in the consumer at run time (source). The designer
binds both levels, and shipped content does so in 11 of 11 file-collection bindings (measured, corpus). So the
builder's shared mapping funnel must bind both levels and refuse the shapes that crash.

What the Jira acceptance criteria say against what is true (full table in the analysis, plan section 1.3):

| Jira AC says | What is true |
|---|---|
| A "Binary / File" data type | Two declared types: `File` (FileLocator, one file) and `FileCollection` (a collection with one FileLocator item). Binary and BLOB stay refused |
| "removed from the rejection allow-list" | The type normaliser is an ALLOW-list; File and FileCollection are ADDED to it |
| `describe-process` reads it back | The tool is `describe-business-process` |
| One type holds a single file or a collection | No such type exists |
| Mapped from the element output when `ResultActionType = 1` | Object attachments and Generated report produce their output collections for both values of `ResultActionType` (SaveToFiles = 0, UseInProcess = 1) |
| Matches a designer-built capture | The shipped captures `FileParameterProcess.FileParameter`, `FileParameterProcess.FileCollection` and `MarkProcessesToCancel.FilesCollection` are the oracle; no designer session is needed |

## Goal

`create-business-process` (`parameters[]`) and `modify-business-process` (`addParameter`) declare process parameters
that carry files by reference, never by bytes; a mapping onto or from them binds the way the designer binds; and
`describe-business-process` reads them back in a form that rebuilds the same shape.

## Users

- A coding agent driving the clio MCP process tools (create, modify, describe), and a developer using the same tools.
- The next tickets of the chain, which consume what this one adds: ENG-96506 Generated report + process parameter modes
  (its Process-parameter variant reads a File or FileCollection; functionally blocked by this ticket), ENG-96505
  Element readiness and object attachments mode (not functionally blocked; it merges after this ticket) and later
  ENG-95985 Send email attachments (reuses the collection binder).

## Scope

- The `File` and `FileCollection` types with their aliases, defaults, refusals and describe read-back.
- Dotted process-parameter paths (`Docs.File`) as a mapping source and target.
- The two-level collection binder inside the package's one mapping funnel (`ProcessMappingService.ApplyMapping`, used
  by `mappings[]`, `addMapping`, the build, the `typeFromElement` mirror, `setConnections` and approvals), with the
  explicit `BindCollection` API for later consumers.
- The dotted-mirror bug (`typeFromElementParameter: "ObjectFiles.File"` silently creates an unbound parameter) refused.
- Describe: nested-only element bindings listed; a value that is exactly one element-output token decoded into
  `sourceElement` + `sourceElementParameter`, only when replaying those names changes no other level (parity with the
  write side: no P1 parent write, no P3 reset, no P2 item write); one element index per describe.
- The `typeFromElement` mirror binds the items too, on add and on re-mirror; a re-mirror keeps a caller's formula on
  the outer level and binds only the items.
- clio: rebundle, floor move, the type lists in the create and modify descriptions, the describe clause, the
  `ManagerMap` arm for the future `fileprocessing` token, capability-map rows, unit tests and a new MCP e2e fixture.
- clio-knowledge: the new `process-files` guide, the `parameters` type list, and the two multi-instance passages
  rewritten under a version gate.

## Non-goals

- The Process file element itself (ENG-92719 File processing element, delivered by ENG-96505 Element readiness and
  object attachments mode and ENG-96506 Generated report + process parameter modes). The element's mappings are
  verified here at design time on hand-built elements only.
- Send email and Creatio.ai attachment slots (ENG-95985 Send email attachments; Creatio.ai is out of scope).
- Any constant, upload or byte-content form of a file parameter, and passing a file as a `run-process` start value.
- A declared item shape for any Collection, a type allow-list for every mirror, and describe decoding of
  process-parameter sources: three follow-up Sub-tasks with their own stories (2, 3, 4), off this ticket's path.
- Other type families of ENG-92730 Complex / structured parameter types.

## Acceptance criteria

AC-1..AC-10 **replace** the Jira acceptance criteria A1-A4 of ENG-95984 File process parameter type. The owner agreed
the replacement on 2026-10-07 (open question Q1, plan decision O-7, decisions D24). It also carries one description
edit: "the Binary / File slice of ENG-92730 Complex / structured parameter types" becomes "the File (FileLocator) slice
of ENG-92730 Complex / structured parameter types".

The text the owner pastes into Jira is AC-1..AC-10 of the analysis plan, section 1.4. The table below is a faithful
copy of that text, with editorial differences only, so either can be pasted.

| # | Acceptance criterion |
|---|---|
| AC-1 | `parameters[]` (create) and `addParameter` (modify) accept `File` (one file; platform type FileLocator `A33C9252`; aliases `file`, `filelocator`, `file locator`) and `FileCollection` (aliases `filecollection`, `file collection`, `collection of files`), which creates a `CompositeObjectList` with exactly one item `File` (FileLocator, Variable, no Tag). Both are ADDED to the allow-list; the platform name `"File"` never reaches `DataValueTypeManager`. `Binary` / `Blob` stay refused with a message (names the type given, says "not supported", and points to `File` and `FileCollection`; for `Binary` the existing pin's words "not supported" and "Binary" stay) |
| AC-2 | Defaults and refusals: `File` defaults to Variable; `FileCollection` defaults to Variable (agreed 2026-10-07), so a caller can fill it and the process can return it; an explicit `direction` wins. A file has no constant form: `value` on File, and `addMapping value` onto any FileLocator or `CompositeObjectList` target, are refused. `referenceSchema` is refused on both types. `setParameter type: FileCollection` on a collection of another shape is refused. `removeParameter` is refused while any ITEM of the collection is referenced |
| AC-3 | `describe-business-process` reports File as `FileLocator`, and a process-level collection with no Tag and exactly one FileLocator item as `FileCollection` (plus `itemProperties`). Feeding that output back rebuilds the same shape |
| AC-4 | `processParameter` and `targetProcessParameter` accept a dotted item path (`Docs.File`); the stored meta path of a process-level item has no element segment |
| AC-5 | Mapping an item of a collection into an item of a collection binds both levels (P1), including a multi-instance sub-process's `InputRecordCollection`. A collection mapped onto a file-consuming collection (the Process-parameter variant's `Files`, a FileCollection) pairs its file item (P2); a file item P2 cannot pair is reported with a notice (and cleared if it was bound elsewhere), not refused. A collection with no File item onto a file-consuming target (R-M2) and a collection item into a single File (R-M1) are refused. An `expression` source triggers none of these rules: it is written as given, apart from the package's meta-path check (since 1.6.6.79, ENG-102114), which refuses a malformed or foreign token and lower-cases its GUIDs |
| AC-6 | A FileCollection can be mapped from `ObjectFiles` / `ObjectFiles.File` (Object attachments) or `ReportFiles` / `ReportFiles.File` (Generated report) whatever the action; after "Save to object attachments" the copies' Ids are in `CreatedObjectFileIds`. Mirroring a file output through `typeFromElement` stays refused with a message that points to `FileCollection`; a dotted mirror source (`ObjectFiles.File`) is refused instead of silently creating an unbound parameter. In this ticket the mappings are verified at design time on hand-built elements, for both `ResultActionType` values (the builder does not read the action). The same mappings from builder-CONFIGURED elements are verified in the sub-task that makes each variant configurable: `ObjectFiles` in ENG-96505 Element readiness and object attachments mode, `ReportFiles` in ENG-96506 Generated report + process parameter modes |
| AC-7 | Describe reports a value that is exactly one element-output token as `sourceElement` + `sourceElementParameter` (dotted for a collection item) when replaying those names changes no other level, so a described binding replays as a structured mapping; an element input bound only on a nested item is listed |
| AC-8 | Server serialization matches the shipped captures `FileParameterProcess.FileParameter` (File), `MarkProcessesToCancel.FilesCollection` (Out FileCollection) or `FileParameterProcess.FileCollection` (Variable FileCollection), under one finite comparison rule (test plan, "The AC-8 comparison rule"): parameter and item UIds are ignored (N5); schema UIds in `A3`, `A4`, `GS5` and `IL2` are role-replaced (PT-a); the builder's process-level `IL2` is normalised (N9); the three "no value" `L8` forms are equal (N15); only the authoring culture's resource rows are compared (N14); and the item name, bindings and DisplayValue rows of `FileParameterProcess.FileCollection` are excluded (PT-b). Any other difference fails |
| AC-9 | End-to-end consumption by the Process file element's Process-parameter variant is verified in ENG-96506 Generated report + process parameter modes (a single File binds the nested item only, pending stand measurement M1). In this ticket the mapping shape onto `Files` is pinned at design time (unit tests and describe) |
| AC-10 | The clio floor moves to this ticket's final package cut; the create and modify descriptions name the two types; the `process-files` guide exists; `clio.mcp.e2e` covers create, addParameter, describe read-back, the two-level multi-instance mapping and the refusals |

## The binder rules

The rules run inside `ApplyMapping` for **structured sources only** (`sourceElement` + `sourceElementParameter`, or
`processParameter`; without `sourceColumn`: a record column is not a parameter-to-parameter binding). File-consuming
targets in this cut: (a) the `Files` input of ProcessFileProcessingUserTask
(schema `6c620dd2-026e-560c-489f-030c5be5f2c3`, recognised by the element's task schema UId, mirrored as
`ProcessDesignConstants.FileProcessing`), (b) a process-level collection that matches the FileCollection predicate.
ENG-95985 Send email attachments later adds its attachment slots to this set. Every target is resolved and
type-checked before the first value is written. The refusal and notice texts are server-side and cost no description
bytes (catalogue in the ADR, quoted as the code writes them).

As built in the package PR (draft https://creatio.ghe.com/engineering/crt-process-builder/pull/96, 2026-10-14):
plan, then commit, so a refused mapping assigns no value (items it walked may be adopted: their `ContainerUId` and
owning schema are backfilled, as a load of the schema would); the rules are decided in one rule source,
`CollectionBindingRules`, which the write side applies and describe asks; `BindCollection` (explicit pairs, both
levels; `null` pairs mean the P2 policy) and `BindCollectionItems` (the pairs only; the outer value is never written,
and the outer target still passes its guards); notices through `IProcessDesignNotices`, now a constructor dependency
of `ProcessMappingService`.

| Rule | Trigger | Writes | Refusal or notice |
|---|---|---|---|
| P1 | target is an item of a collection AND source is an item of a collection | the item, and the target's parents to the source's parents, paired innermost first, as many levels as the SOURCE chain has: a deeper TARGET is fine, only the matching inner levels are bound (`Messages.Files.File` from `OF1.ObjectFiles.File` binds `Messages.Files` and leaves `Messages`); a parent already bound to its source parent, or the same collection, is left as it is | refuse (F-M1) when a paired parent is bound to anything else; refuse (F-M5) when the SOURCE is nested deeper than the target (outside its row it reads empty); the same source is a no-op; type-agnostic, so it applies to `InputRecordCollection.<F>` too; no notice |
| P2 | outer collection mapping onto a file-consuming target that has a FileLocator item | each target FileLocator item not already bound into the new source to the paired source item: exact name (case-insensitive, type-checked), else the single-FileLocator pair (`Files.File` to `ObjectFiles.ObjectFile`); an item that cannot be paired and is bound outside the new source is reset to None; non-file items are never paired | a notice (F-M4) for every item bound, rebound, reset or left unpaired (a rebind and a reset name the old binding; an unpairable item gets "at run time it will be empty"); an item already bound into the new source is neither written nor reported |
| P2-MI | a mapping that binds a multi-instance `InputRecordCollection` (as the named target, or as a parent P1 binds) while one of its FileLocator items stays unbound | nothing extra | notice (F-M4): the callee parameter will be empty on every iteration unless mapped |
| P3 | plain (non-item) source onto a FileLocator item of a single-instance owner (any element but a multi-instance sub-process, or the process itself: a process-level `Docs.File` counts) | the item; EVERY bound collection above it reset to None | one notice (F-M4) per reset level (owner decision: reset, agreed 2026-10-07) |
| R-M1 | flat FileLocator target (a process parameter, or an element input such as a single-instance sub-process's File parameter) from a collection item (`F` from `OF1.ObjectFiles.File`) | - | refuse (F-M2: "use a FileCollection, or a multi-instance sub-process"); M6 (2026-10-02): the mapping reads null, so the refusal stays |
| R-M2 | file-consuming target collection from a collection with no FileLocator item (`Files` from `ReadData.ResultCompositeObjectList`) | - | refuse (F-M3: the source "has no File item") |
| expression | any `expression` source | as given, to the named target only. Since 1.6.6.79 (ENG-102114) the package checks every meta-path token against this process (a misspelled token, a token from another process and a column of a collection item are refused) and stores its GUIDs lower-cased; nested-item tokens such as `Files.File` are accepted | none of the rules above; the platform validates the formula as before |

## Constraints

- **C1 By reference, never by bytes.** A file parameter is FileLocator. The platform name `"File"` must never reach
  `DataValueTypeManager.GetInstanceByName`; Binary and BLOB stay refused.
- **C2 No `Terrasoft.Configuration` reference.** Package production code and tests must not name
  `Terrasoft.Configuration` types (`ObjectFileProcessingUserTask` and the others): the dev-n8 build has no such DLL.
  Mirror the constants (schema UIds, parameter names, `ResultActionType` integers). Package sources are C# 7.3.
- **C3 One funnel.** The rules live in `ApplyMapping`, so every caller inherits them; the callers whose targets are
  scalar (`setConnections`, approvals) must stay green unchanged.
- **C4 Versions.** CrtProcessBuilder `main` and clio's bundle are at 1.6.6.96; the next cut is 1.6.6.97 or higher: the
  first free number above the highest number claimed anywhere at cut time (1.6.6.87, 1.6.6.89 and 1.6.6.94 are
  burned; re-check with `git ls-remote --tags` in both repositories). The number is claimed BEFORE cutting, in the PR
  description AND in the one shared cut claim, a comment on ENG-92719 File processing element; the shared claim
  guards against the double stamp measured in git history, when two package PRs in review at the same time stamped
  the same number.
  The three `[RequiresPackage]` floors are 1.6.6.92 today and move to this ticket's FINAL cut. The bundle moves only
  by the manual `rebundle-process-builder.ps1 -Version <claimed>`; the version only goes up.
- **C5 Description budget.** On clio master `19e6f9c53` (measured 2026-10-13) create-business-process had 4 B and
  modify-business-process 10 B left under the 35,072 B per-contract ceiling; the type lists cost +30 B and +28 B. On
  clio master `79fab4786` (2026-10-14), ENG-103081 Warn when a process-parameter mapping is bound twice or to a branch
  some start skips re-measured them at 35,061 B (11 B left) and 35,070 B (2 B left), so the trim must free at least
  19 B on create and at least 26 B on modify. The seven process-designer contracts requested together sat at 18,986 B
  of the 19,000 B wire guard (measured 2026-10-13); this ticket adds no descriptor member, so the call shapes and the
  wire guard are unaffected (hypothesis, source). The bytes come from a reviewer-approved trim of existing text; the
  ceiling is not raised (Q20, agreed 2026-10-14: A).
- **C6 Delivery order.** This docs-only set merges before the package PR leaves draft; then package, clio, knowledge,
  each merged by a human only. The knowledge generation that births `process-files` is published before the clio PR of
  ENG-96505 Element readiness and object attachments mode merges.
- **C7 Stand discipline.** Every stand write and every `install-process-builder` needs the user's explicit go-ahead
  (an install restarts the site for every connected user); schema writes and runs go one at a time.
- **C8 Guidance.** Shipped guidance names no Jira key, issue or PR; a version boundary is the clio version that ships
  the change; a CrtProcessBuilder version appears only as inline evidence.

## Success signal

On a stand carrying this ticket's cut: `FileParameterToolE2ETests` passes with Ignored = 0; an item-only
`InputRecordCollection.F <- Docs.File` mapping is read back with both levels bound and a multi-instance run iterates
3 times where the 1.6.6.54 baseline iterated once; the builder's `File`, `FileCollection` declared with no direction
(the Variable default) and `FileCollection` declared with `direction: Out` equal the three shipped captures under the
AC-8 comparison rule.

## Open owner items

**O-TP1. Where do the e2e cases on the Process-parameter variant's `Files` run?** Recommended B: in the clio PR of
ENG-96506 Generated report + process parameter modes, where that variant is built by its own block; until then this
ticket's evidence for that half of AC-5 and AC-7 is package-unit. Alternative A: in this clio PR through the generic
`userTask` route, which ENG-96506 later refuses and rewrites.

**Open item (recorded 2026-10-14, not decided): a single-instance sub-process's FileCollection input.** A
single-instance sub-process's FileCollection-shaped `In` parameter is not a file-consuming target (the list above, as
written), so an outer-only `SP.Docs <- OF1.ObjectFiles` gets neither P2 nor R-M2. Stand measurement M3 measured an
outer-only binding onto a process parameter only. A stand measurement (M27 of the analysis) settles whether the callee
reads the files; the owner decides afterwards whether the set widens. No code change is planned for it in this story.

**Q20. Where do the description bytes come from?** (opened 2026-10-13; **agreed 2026-10-14: A**). Options: **A**
(recommended) trim existing text per tool, each trim approved by the reviewer and measured by lowering the ceiling;
candidate on create: drop the member list `{enabled, executionMode, ignoreErrors}` from the floor clause (about 39 B),
the modify candidate chosen at cut; **B** a reviewed ceiling raise; **C** move the type lists out of the descriptions
into guidance. **Answer (2026-10-14): A.** B and C are rejected: the ceiling is not raised and the type lists stay in
the descriptions. The create candidate stays a candidate measured at cut, and the modify trim is chosen at cut with the
reviewer. Q20 concerned the clio description edit (story 1, task CL-2) only, never package code; it no longer gates
anything, and the trim itself is work inside the clio PR. The sentence `this clio requires <floor>` and the
`stopped validating formulas` clause stay as they are.

## Companion notes

- **Visible behaviour changes** the package PR lists: (1) an item-only `InputRecordCollection.<F>` mapping from an item
  of a collection now also binds `InputRecordCollection` (one iteration becomes N); (2) describe reports
  `sourceElement` / `sourceElementParameter` for every single-token element-to-element value of every process; (3) a
  collection matching the FileCollection predicate (shipped `FileParameterProcess`, `MarkProcessesToCancel`) now
  describes as `FileCollection`; (4) a dotted mirror source and `addMapping value` onto a collection, both accepted
  before, are refused; (5) the ENG-96230 Collection process parameter type mirror binds items too (on add and on
  re-mirror).
- **Jira links** (owner decision, agreed 2026-10-07): ENG-95984 File process parameter type **blocks** ENG-96506
  Generated report + process parameter modes and **relates to** ENG-96505 Element readiness and object attachments
  mode (today's "blocks" link to ENG-96505 claims a functional dependency that does not exist). ENG-95984 also
  **blocks** ENG-95985 Send email attachments (link L5, together with ENG-96505): that ticket reuses `BindCollection`
  and adds its attachment slots to the file-consuming targets.
- **Day-0 measurements, done on 2026-10-02 (CrtProcessBuilder 1.6.6.54):** M3 refuted the suspected defect that the
  ENG-96230 mirror reads null rows (3 iterations, all values set), so no extra Sub-task exists and binding the mirror's
  items is one parity commit; the item-only multi-instance baseline (MI-0) ran 1 iteration; M6 read a flat File taken
  from a collection item as null, so R-M1 stays a refusal; and a nested-item mapping in `mappings[]` on the create
  path threw a bare NullReferenceException (trap T-68). The package PR root-caused and fixed it (draft, 2026-10-14):
  the nested items of a user task synced in the same request have no owning `ProcessSchema` (the platform's copy
  constructor copies template items owned by a `ProcessUserTaskSchema`), so `GetMetaPath()` threw; the modify path
  loads items through the metadata reader, which owns them. The for-mapping walk now adopts each descended item, and
  the regression test drives the platform's real sync. The stand proof is owed (E2E-06, manual TC-04 on ENG-95984).
- **Estimate** (an AI agent writes the code): about 7.5-15.5 h of agent time and 4.5-10 h of the owner's time; about
  3-5 working days of calendar from day 0.
