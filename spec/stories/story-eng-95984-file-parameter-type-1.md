# Story 1: File and FileCollection process parameters, the two-level collection binder, and describe read-back

**Feature**: eng-95984-file-parameter-type
**Jira**: ENG-95984 File process parameter type (Task; epic ENG-92704 Create BP via AI Toolkit)
**Spec**: [spec-eng-95984-file-parameter-type.md](../prd/spec-eng-95984-file-parameter-type.md) - AC-1..AC-10
**ADR**: [adr-eng-95984-file-parameter-type.md](../adr/adr-eng-95984-file-parameter-type.md) - D1-D9, D23, D25, D26
**Test plan**: [tp-eng-95984-file-parameter-type.md](../test-plans/tp-eng-95984-file-parameter-type.md)
**Status**: review (implementation started 2026-10-13; `review` since 2026-10-14, when the clio feature PR https://github.com/Advance-Technologies-Foundation/clio/pull/1814 left draft)
**Size**: L (about 7.5-15.5 h of AI-agent time and 4.5-10 h of the owner's time; about 3-5 working days of calendar)
**Repo**: crt-process-builder, then clio, then clio-knowledge (one feature PR each, titled
`ENG-95984 File process parameter type`, branch `feature/ENG-95984-file-process-parameter-type`)
**Depends on**: this BMAD set (the docs-only clio PR on branch `feature/ENG-95984-file-parameter-type-spec`) merged
**before the package PR leaves draft**. Q20 = A (2026-10-14): trim, reviewer-approved; it concerned the clio PR's
description edit (CL-2) only and gates nothing now.
**Blocks**: ENG-96506 Generated report + process parameter modes (functionally) and ENG-95985 Send email attachments
(link L5, together with ENG-96505: it reuses `BindCollection` and adds its slots to the file-consuming targets).
ENG-96505 Element readiness and object attachments mode is not blocked functionally but merges after this story, and
its clio PR merges only after this story's knowledge generation is published. Follow-up stories 2, 3 and 4.

> **Done 2026-10-14** (Q9: the follow-ups are created on day 0): the three follow-up Sub-tasks of ENG-95984 exist
> in Jira as ENG-103162 (story 2), ENG-103163 (story 3) and ENG-103164 (story 4), and their keys are in the story
> headers and the `jira` fields of the three follow-up rows in `spec/sprint-status.yaml`.

---

## As a

coding agent building a business process through the clio MCP process tools

## I want

to declare process parameters that carry one file (`File`) or several files (`FileCollection`), map files onto and
from them the way the designer does, and read them back with `describe-business-process` in a form that rebuilds

## So that

a process can take files from a parameter, hand a file collection to the Process file element or to a multi-instance
sub-process per file, and never save a binding green that fails at run time

---

## Acceptance Criteria

The full text is the spec's AC table (it replaces the Jira A1-A4; agreed 2026-10-07). Each line below is that criterion
in short.

- [ ] **AC-1** - `File` (aliases `file`, `filelocator`, `file locator`; FileLocator `A33C9252`) and `FileCollection`
  (aliases `filecollection`, `file collection`, `collection of files`; a `CompositeObjectList` with exactly one item
  `File`: FileLocator, Variable, no Tag) are accepted by create `parameters[]` and modify `addParameter`; the name
  `"File"` never reaches `DataValueTypeManager`; `Binary` / `Blob` are refused naming `File` and `FileCollection`,
  keeping "not supported" and "Binary".
- [ ] **AC-2** - File and FileCollection default to Variable, an explicit `direction` wins; no constant form (`value`
  on File, `addMapping value` onto any FileLocator or `CompositeObjectList` target refused); `referenceSchema` refused
  on both; `setParameter type: FileCollection` on another shape refused; `removeParameter` refused while any item is
  referenced.
- [ ] **AC-3** - describe reports File as `FileLocator` and the FileCollection predicate as `FileCollection` (plus
  `itemProperties`); feeding that back rebuilds the same shape.
- [ ] **AC-4** - `processParameter` / `targetProcessParameter` accept `Docs.File`; a process-level item's meta path
  has no element segment.
- [ ] **AC-5** - P1 binds both levels (including a multi-instance `InputRecordCollection`); P2 pairs the file item on
  file-consuming targets, and a file item it cannot pair gets a notice (cleared if bound elsewhere), not a refusal;
  R-M1 and R-M2 refuse; P2-MI and P3 give notices; an `expression` triggers none of them (only the meta-path check,
  since 1.6.6.79).
- [ ] **AC-6** - a FileCollection maps from `ObjectFiles` / `ObjectFiles.File` and `ReportFiles` / `ReportFiles.File`
  whatever the action (verified on hand-built elements for both `ResultActionType` values); a file-output mirror stays
  refused pointing to `FileCollection`; a dotted mirror source is refused.
- [ ] **AC-7** - a value that is exactly one element-output token is described as `sourceElement` +
  `sourceElementParameter` (dotted for an item) when replaying those names changes no other level; an input bound only
  on a nested item is listed.
- [ ] **AC-8** - the serialization of a File, a FileCollection declared with no direction (the Variable default) and a
  FileCollection declared with `direction: Out` equals the three shipped captures under the test plan's AC-8
  comparison rule (N5, PT-a, N9, N15, N14, PT-b; any other difference fails).
- [ ] **AC-9** - the mapping shape onto the Process-parameter variant's `Files` is pinned at design time here;
  end-to-end consumption is verified in ENG-96506 Generated report + process parameter modes.
- [ ] **AC-10** - the floor moves to the final cut; the create and modify descriptions name the two types;
  `process-files` exists; `clio.mcp.e2e` covers create, addParameter, describe read-back, the two-level
  multi-instance mapping and the refusals.

---

## Tasks

`PB/` = crt-process-builder `packages/CrtProcessBuilder/Files/src/cs/`; `PBT/` =
`tests/UnitTests/CrtProcessBuilder.Tests/`. CL-0 (this BMAD set) is the docs-only clio PR and is not repeated here.

**CrtProcessBuilder** (base `main`; planned as ten commits (the analysis's pr-split section 5.2), each mapped to its
decision in the PR description: D1; D2 with D3; D4; D5 with `BindCollection`; D6; D7; D8; D9;
`docs/file-parameter-capture.md`; the restamp last - the ADR's implementation outline. The draft began as one
commit, `b6dcd2c1`, and has four since 2026-10-14, head `d6ca4d79`; the split before leaving draft is the owner's call)

- [ ] **PB-1** (D1) `PB/Parameters/ProcessParameterService.cs` `NormalizeParameterTypeName`: file aliases to the
  canonical FileLocator name; dedicated Binary / Blob refusal (F-T1); generic list adds `File, FileCollection`; doc
  comment corrected.
- [ ] **PB-2** (D2, D3) `ProcessParameterService.cs`: `FileCollection` aliases beside the Collection aliases;
  `AddProcessParameter` creates the single `File` item with a Variable default; the wrong "designer writes Out" comment
  corrected; `ToDescribeParameter` applies the FileCollection predicate (process-level only); doc comments in
  `PB/Contracts/ProcessDescriptorContracts.cs` (`ProcessParameterDescriptor`).
- [ ] **PB-3** (D4) `PB/ProcessSchemaElementLocator.cs` (`DescendItemProperties` over a root collection plus owner
  label; new `ResolveProcessParameterPath`, no `ContainerUId` backfill); both `ResolveProcessParameter` callers in
  `PB/Mappings/ProcessMappingService.cs` switched.
- [ ] **PB-4** (D5) `PB/Mappings/ProcessMappingService.cs` `ApplyMapping`: P1, P2, P2-MI, P3, R-M1, R-M2 after the
  existing guards, all targets resolved before the first write; `BindCollection` on
  `PB/Mappings/IProcessMappingService.cs`; notices through `IProcessDesignNotices`. Root-cause T-68 (nested-item
  mapping in `mappings[]` on the create path, `PB/Design/ProcessBuildHandler.cs`) first, and run the binder on create
  as well as modify.
- [ ] **PB-5** (D6, D2) `ProcessParameterService.cs` `ResolveTypeSourceParameter` flat only (F-T5; the re-mirror path
  inherits it); `CloneItemProperties` gives an untagged FileLocator item the FileCollection hint (F-T6).
- [ ] **PB-6** (D7) `PB/Parameters/ProcessParameterValueValidator.cs` `ValidateConstantValue` refuses a constant on
  `IFileLocator` or `CompositeObjectList`; `ProcessParameterService.cs`: "no constant form", `referenceSchema` refusal
  extended to File / FileCollection, `SetProcessParameter` FileCollection-shape refusal, `FindParameterUsages` over
  item UIds recursively (`WithNestedParameters`).
- [ ] **PB-7** (D8) `PB/Describe/ProcessDescriber.cs` lists an input whose nested item carries a value stamped with
  this schema; single-token element-source decode beside `DecodeRecordColumnSource`; `PB/Contracts/DescribeContracts.cs`
  doc comment ("Null for every other value" no longer true).
- [ ] **PB-8** (D9) `ProcessParameterService.cs` `BindMirroredCollection` calls `BindCollection` with the clone pairs;
  the comment that predicted null rows narrowed (parity commit, contingency X4). The re-mirror path binds its items
  too and keeps a caller's formula on the outer level (`BindCollectionItems`; ADR D9).
- [ ] **PB-9** (all) tests: `PBT/FileProcessParameterTests.cs`, `PBT/FileCollectionMappingTests.cs`,
  `PBT/ElementSourceDecodeTests.cs` (new), extended `ProcessParameterServiceTests`, `ParameterTypeCompatibilityTests`,
  `ProcessDescriberTests`, `MultiInstanceMappingTests`; the probe `AddProcessParameter_ShouldNameCollection_InUnsupportedTypeMessage`
  moved from `Binary` to `Image` or `Color`; the shared helper `PBT/FileProcessingTestSupport.cs`, the drift guard
  `PBT/FileProcessingTemplateDriftTests.cs`, the three captures under `PBT/Fixtures/FileProcessing/` with their
  `PROVENANCE.md` (SHA-256 pins). This PR owns the helper and guard; ENG-96505 and ENG-96506 inherit them.
- [ ] **PB-10** (AC-8) NEW `docs/file-parameter-capture.md` (the capture convention, the three shipped captures, the
  fields compared); `docs/process-builder-architecture.md` rows for `IProcessParameterService` /
  `IProcessMappingService`; restamp `packages/CrtProcessBuilder/descriptor.json` LAST to the number claimed before
  cutting, in the PR description AND in the one shared cut claim (a comment on ENG-92719 File processing element);
  tag `crtprocessbuilder-<version>` pushed and verified with `git ls-remote --tags origin`;
  `.codex/workspace-diary.md` entry.

**clio** (base `master`; one feature PR, because the guards bind archive, pins, floors and descriptions to one tree)

- [ ] **CL-1** Rebundle and floor: `pwsh ./rebundle-process-builder.ps1 -PackageRepoPath <package at PR head>
  -Version <claimed>`, where `<claimed>` is the number claimed before cutting in the PR description AND in the one
  shared cut claim on ENG-92719 File processing element (comment 550565; the next free number in the claim list at
  cut time); `clio/CrtProcessBuilder/CrtProcessBuilder.gz` and the four pins in
  `clio.tests/Common/BundledProcessBuilderPackageTests.cs`; floor literals in `clio/Command/CreateBusinessProcessCommand.cs`,
  `ModifyBusinessProcessCommand.cs`, `ModifyProcessAsNewVersionCommand.cs` with their comment blocks;
  `clio.tests/Command/ProcessDesignerRequiresPackageAttributeTests.cs`; the enforced-floor sentence in three
  descriptions. Install from the refreshed BUILD OUTPUT, each install after the user's explicit approval. A longer
  floor literal (a new digit) costs a byte in three descriptions: the budget test catches it, this PR trims (ADR D25).
- [ ] **CL-2** Descriptions (Q20 = A (2026-10-14): trim, reviewer-approved): type lists in
  `clio/Command/McpServer/Tools/ProcessDesigner/CreateBusinessProcessTool.cs` and `ModifyBusinessProcessTool.cs`
  (+30 / +28 B) paid by trimming existing text, each trim approved by the reviewer and measured by lowering the ceiling
  (the create candidate measured at cut, the modify trim chosen at cut with the reviewer; no ceiling raise);
  `DescribeProcessTool.cs` decoded-source clause (+40 B);
  `ModifyProcessAsNewVersionTool.cs` floor-history parenthetical rewritten once without its enumeration; the figures in
  the comment of `clio.tests/Command/McpServer/ToolContractPayloadBudgetTests.cs` re-measured; no `name=` pointer, no
  binder rules in any description; count words grepped in description and guide together.
- [ ] **CL-3** `ManagerMap` arm: `clio/Command/ProcessModel/Schema.cs` adds `"fileprocessing" or "processfile"` to the
  `EventType.UserTask` arm; `[TestCase]`s in `clio.tests/Command/ProcessModel/ManagerMapResolveDataIdTests.cs`; update
  `docs/knowledge/ProcessModel/subprocess-build-token-needs-a-managermap-arm.md`.
- [ ] **CL-4** `docs/McpCapabilityMap.md` rows (type names, FileCollection read-back, describe decode, floor), the
  collapse sentence left as it is; in `clio.mcp.e2e/ModifyBusinessProcessToolE2ETests.cs`, the existing
  `ModifyBusinessProcess_Should_RejectUnsupportedParameterType` keeps its assertions, and both its `[Description]`
  ("an unsupported (complex) type", `:2402`) and its Act comment ("a deferred complex type", `:2414`) are reworded to
  "refused for good: a file is type File".
- [ ] **CL-5** `clio.tests/Command/ProcessModel/ServerProcessDescriberTests.cs` (FileLocator / FileCollection
  read-back, decoded pair); the `DescribedParameter` doc comments `Type`, `SourceElement` and `ItemProperties` in
  `clio/Command/ProcessModel/IProcessDescriber.cs` rewritten; no DTO member change.
- [ ] **CL-6** NEW `clio.mcp.e2e/FileParameterToolE2ETests.cs` (`[Category(McpE2ECategories.ProcessDesigner)]`,
  `MinimumPackageVersion` = the floor, every describe read-back with `mode=full`), without the generic `userTask`
  route for the Process file schemas: test-plan cases TC-I-01..TC-I-13. The cross-ticket cases XT-14a..d (the
  Process-parameter variant's `Files` over the wire) are planned in ENG-96506's clio PR (O-TP1) and are not part of
  this story.
- [ ] **CL-7** Knowledge records (internal `docs/knowledge/`, in the clio feature PR): NEW
  `platform/the-platform-type-name-file-is-the-blob-type.md`, `platform/a-collection-row-is-rebuilt-from-the-target-items.md`,
  `ProcessModel/a-file-collection-is-described-by-its-alias.md`, `platform/builder-process-parameters-carry-il2.md`
  (after V4), `ProcessModel/corpus-scan-b8-is-the-creation-version.md`; UPDATE the ManagerMap-arm record; REVIEW the
  records `make check-knowledge` reports. Status flips in `spec/sprint-status.yaml` (see "Status convention").

**clio-knowledge** (base `master`, own worktree under `.worktrees/<task>/`)

- [ ] **KB-1** NEW `guidance/mcp/guides/processes/files.md` (`process-files`, topic `creatio.process-files`, bundle path
  `resources/process-files.md`), banner-less, not in `ProcessGuideSet.GoLiveFloor`; registered in `bundle-source.json`
  (entry, `requirements.itemIds`, `resourceUris`), `GuidanceMigrationTests.PostMigrationGuidance`, a routing row in the
  same generation; pin test `automation/Clio.Knowledge.Bundle.Tests/FileParameterGuidanceTests.cs`. Text rules: every
  read-back says `describe-business-process mode=full` (or `element=<name>`) with the gate "on clio older than
  8.1.0.141, omit `mode`" that `process-modeling.md` owns (`ClioToolSyncGuidanceTests`); consent wording within 80
  characters of every `install-process-builder` mention; no Jira key, issue or PR link; a "from clio <version>" line
  with the real number at merge, a CrtProcessBuilder version only as inline evidence.
- [ ] **KB-2** `guidance/mcp/guides/processes/parameters.md`: type list gains File and FileCollection (its
  not-supported tail drops "file"); the FileCollection exception to "never by re-typing the shape";
  `CollectionParameterGuidanceTests.cs` rationale amended and the new sentences pinned.
- [ ] **KB-3** `guidance/mcp/guides/processes/sub-process.md` and `sub-process-when.md`: the multi-instance passages
  rewritten under the "from clio <version>" gate, "send both" kept, read-backs with `mode=full` under the 8.1.0.141
  gate; pinned.
  libraryVersion above master and every branch claim at merge (1.15.144 or higher on 2026-10-14: master is 1.15.143,
  released 2026-10-13).

---

## Stand verification

Day-0 measurements are done (2026-10-02, CrtProcessBuilder 1.6.6.54): M3 refuted H-1, MI-0 gave 1 iteration, M6 read
a flat File from a collection item as null, T-68 reproduced. Owed before the package PR leaves draft (the code
already exists): V0 (the shipped captures still describe today's designer; read-only, it fixes the expected
defaults). After the cut, before the package PR leaves draft: V1 (instrument check: the stand is on the cut and the
session's MCP clio is the PR's clio), V2 (e2e, Passed with Ignored = 0), V3 (3 iterations against MI-0's 1), V4
(serialization against the three captures under the AC-8 rule, after SC-0), V5 (the designer opens the V4 process; a
no-op save changes nothing), V6 (`info-knowledge` shows the new libraryVersion); V7 optional, V8 only if M1 passed.
Every write and every `install-process-builder` needs the user's explicit go-ahead; one at a time. Details: the test
plan's manual cases.

**Results, 2026-10-14** (the .NET Framework stand): V0 PASS (read-only, 1.6.6.96 / 1.6.6.97); V1 PASS on 1.6.6.97
and again on 1.6.6.98; V3 PASS on 1.6.6.97 (3 callee runs); V4 FAIL on 1.6.6.97 (the item stamps, ADR D2), PASS on
1.6.6.98; V5 PASS on 1.6.6.98 (only the 6 generic differences of any designer save); V7 PASS on 1.6.6.98; V2 PASS on the clio PR's build
(MCP e2e 13/13 on net10.0 and net8.0, Ignored = 0); V6 and V8 not run. T-68: no exception on 1.6.6.97 (2 processes). The 12 manual cases of ENG-95984 comment 550561 passed as
direct tool calls and designer steps. V2 ran again on the cut that superseded 1.6.6.98: Dev Agent Record.

## Status convention

The tracker lives in clio, so a status flip needs a clio commit: `in-progress` was flipped in this BMAD PR on
2026-10-14, because implementation started on 2026-10-13 (AGENTS.md: `in-progress` on start); `review` when the clio
feature PR leaves draft (deliberately not when it opens as a draft, because the chain is stacked); `done` only after
the knowledge PR merges, carried in the next clio PR of the chain (ENG-96505 Element readiness and object attachments
mode) and never pushed after an approval. The follow-up stories 2-4 do not gate `done`.

## Definition of Done

**CrtProcessBuilder**
- [ ] `File` / `FileCollection` admitted with the D1 aliases; Binary and Blob refused with the "use File" text; the doc
      comment of `NormalizeParameterTypeName` corrected.
- [ ] FileCollection creates exactly one item `File` (FileLocator, Variable, no Tag, `ContainerUId = schema.UId`);
      describe reports `FileCollection` for the predicate; the round trip holds.
- [ ] Dotted process-parameter paths on both sides; meta path without an element segment.
- [ ] P1, P2, P2-MI, P3, R-M1, R-M2 in `ApplyMapping`, structured sources only (without `sourceColumn`: a record
      column is not a parameter-to-parameter binding); `expression` as given (only the package's meta-path check
      applies); `BindCollection` on `IProcessMappingService`.
- [ ] D6 and D7 refusals; item-aware delete guard; the D9 mirror item binding.
- [ ] D8 describe listing and single-token element-source decode.
- [x] T-68 root-caused; a nested-item mapping in ONE create request binds as it does through `addMapping` (no bare
      NullReferenceException), pinned by a regression test. (Ticked 2026-10-14: root cause and regression test in the
      package PR; on the stand no exception on 1.6.6.97 for 2 processes, and manual TC-04 passed. E2E-06 passed
      with V2.)
- [ ] All new package fixtures carry the crt-process-builder house rule
      `[TestFixture(Category = "UnitTests"), Category("PreCommit")]` and nothing else (a crt-process-builder rule only:
      clio's `project-context.md` bans `UnitTests` for clio fixtures); AAA, `because:`, `[Description]`; the Binary
      probe moved to `Image` or `Color`; the Binary refusal pin unchanged;
      `dotnet test tests/UnitTests/CrtProcessBuilder.Tests/CrtProcessBuilder.Tests.csproj -c dev-nf` green from a
      short-path worktree with `.application` copied in (a deep path exceeds MAX_PATH and every net472 test then
      fails in SetUp).
- [ ] `FileProcessingTestSupport`, `FileProcessingTemplateDriftTests`, the three captures and `PROVENANCE.md`
      committed; ENG-96505 inherits them.
- [ ] `docs/file-parameter-capture.md` committed and matching V4; restamp last; tag pushed and verified on the remote
      before the merge.
- [ ] `.codex/workspace-diary.md` entry added (the package's "Workspace diary" rule).

**clio**
- [ ] Rebundled with a strictly increasing `-Version`; four pins, three floor literals, the enforced-floor sentences,
      capability-map rows and the e2e `MinimumPackageVersion` all at the final cut.
- [ ] Type lists and describe clause, paid by the byte swap Q20 settled (Q20 = A (2026-10-14): trim,
      reviewer-approved; the enforced-floor sentence and the `stopped validating formulas` clause left as they are);
      budget comment re-measured; ceiling unchanged.
- [ ] Must stay green: `FloorSentences_ShouldNotCreditTheEnforcedFloorWithTheCollapse`,
      `EnforcedFloorSentences_ShouldEqualTheRequiresPackageLiteral`, the seven process-designer contracts within
      19,000 B on the wire (`GetToolContracts_ShouldFitTheSevenProcessDesignerContractsInline_WhenRequestedTogether`),
      and the three call-shape drift tests in `ToolContractShortFormTests`.
- [ ] `ManagerMap` arm with `[TestCase]`s.
- [ ] The three `DescribedParameter` doc comments rewritten; no new `CLIO*` analyzer warning in a touched file.
- [ ] Every new or changed test in `clio.tests` and `clio.mcp.e2e`: explicit Arrange / Act / Assert, a `because` on
      every assertion, `[Description]` on every test, runnable on macOS, Linux and Windows (no OS-specific paths or
      commands); clio unit tests carry `[Category("Unit")]`, never `UnitTests`.
- [ ] `ServerProcessDescriberTests` cases; full unit suite green:
      `dotnet test clio.tests/clio.tests.csproj --filter "TestCategory=Unit" < /dev/null` (run from bash; the change
      spans four modules).
- [ ] `FileParameterToolE2ETests` and the must-stay-green fixtures run against the stand on the cut with Ignored = 0.
- [ ] Knowledge records added or updated; `make check-knowledge` report reviewed in full.
- [ ] "MCP reviewed" (tools create-business-process, modify-business-process, describe-business-process,
      modify-process-as-new-version floor, validate-process-graph's `ManagerMap` arm; prompts, resources and `clio/tpl`:
      no update required), "docs reviewed, no update required" (`clio/help/en`, `clio/docs/commands`,
      `clio/Commands.md`, `clio/Wiki/WikiAnchors.txt`: these MCP-only tools have none) and "ClioRing compatibility
      reviewed, no Ring-consumed contract changed" (inspected `clio-ring/ClioRing.Ipc`, `clio-ring/ClioRing`,
      `clio-ring/ClioRing.Desktop/actions.json`) stated in the PR description, with the re-measured budget figures and
      the `Validated:` commands.
- [ ] ClioRing: the Purpose leads of the touched tools are confirmed unchanged in `get-tool-contract`
      `detail=index-entries` (the object index Ring reads), because a Q20 trim could change a lead.

**clio-knowledge**
- [ ] `process-files` born and fully registered (banner-less, not in `ProcessGuideSet.GoLiveFloor`); `parameters.md`
      edits within budget; the two multi-instance passages rewritten under the version gate; read-backs say
      `describe-business-process mode=full` (or `element=<name>`); consent wording at every `install-process-builder`
      mention; no Jira key or PR link; pin tests; `ProcessGuideResponseSizeTests`, `GuidanceInlineReplyBudgetTests`,
      `ProcessDigestTests`, `ProcessBuilderInstallConsentGuidanceTests`, `ClioToolSyncGuidanceTests`,
      `ProcessSplitCoreRuleTests` and `InternalTaskReferenceTests` green; libraryVersion bumped; release published.

**Process**
- [ ] This BMAD set merged before the package PR leaves draft.
- [ ] Status flips as in "Status convention"; this Definition of Done has no unchecked item before `done`.
- [ ] W0 measurements recorded with dates and basis; V0, SC-0 and V1-V6 executed and recorded (V7, V8 when run).
- [ ] Review gate 1 (comprehensive) before each PR opens; gate 2 on every new commit with the tier stated, skipped
      only for a commit limited to docs, `*.gz` archives or the descriptor restamp (a clio rebundle commit moves
      floor literals, pins and descriptions, so it gets at least the single-lens review); gate 3 before
      ready-to-merge; Blocker and High findings resolved.
- [ ] Jira: AC replaced with AC-1..AC-10; the description edit ("File (FileLocator) slice"); links changed (D23,
      including ENG-95985 Send email attachments blocked by ENG-95984).
- [ ] Only a human merges; hand-over order: package (tag verified on the remote), clio, knowledge.

## Dev Agent Record

- Implementation started: 2026-10-13 (package, PK-PT).
- Package cut claimed (where, date): the number lives in ENG-92719 comment 550565 (updated in place at each re-cut),
  the tag and `descriptor.json`; first claimed 2026-10-14. Cut history: 1.6.6.97 (`260bb8ce`) was installed once and
  burned. 1.6.6.98 (stamped in `d6ca4d79`) was installed on the stand and superseded the same day: the package PR's
  gate-3 review found two gaps (M1: the element dependency guards read process parameters at the top level only, so a
  retarget could strand a reference held by an item such as `Docs.File`; M2: a re-mirror gave every item a new UId,
  so a reference to an unchanged column dangled); their fix (`11b8088c`, `21d0b016`, `dcfdc83f`) was cut as the next
  claimed number (ADR D26 record).
- Bytes trimmed per Q20 = A (2026-10-14: trim, reviewer-approved), which text per tool and the reviewer's approval:
- Tests passing (package, clio unit, e2e with Ignored count, knowledge): package 3424 passed at `b6dcd2c1`
  (`dotnet test tests/UnitTests/CrtProcessBuilder.Tests/CrtProcessBuilder.Tests.csproj -c dev-nf`, 187 of them new),
  3426 at `b92547cc`, 3435 with the gate-3 fixes; clio (with the MCP e2e) and knowledge opened as drafts on
  2026-10-14.
- Stand records (V0-V8): 2026-10-14, the .NET Framework stand (ENG-95984 comment 550775). V0 PASS (1.6.6.96 /
  1.6.6.97); V1 PASS (1.6.6.97, again on 1.6.6.98); V3 PASS (1.6.6.97, 3 callee runs against 1 on 1.6.6.54); V4 FAIL
  on 1.6.6.97 (item `A3` / `A4` missing), PASS on 1.6.6.98; V5 on 1.6.6.97: 4 item differences and 6 generic ones, on
  1.6.6.98 PASS with only the 6 generic ones; V7 PASS (1.6.6.98); V2 PASS (the clio PR's build); V6, V8 not run.
  On the cut that superseded 1.6.6.98 (1.6.6.99, 2026-10-14): `FileParameterToolE2ETests` 13/13,
  `ProcessParameterBindingToolE2ETests` 4/4, `SubProcessElementToolE2ETests` 10/12 on net10.0 and net8.0 (the two
  failures are the `DescribeBusinessProcess_Should_ShowANewCalleeParametersCaption_*` cases, a clio master regression
  since ENG-103023 clio: spill-free reads and post-write summaries, bisected to clio `3715cd23c`, not this
  ticket's), and the must-stay-green fixtures 20/20 on net10.0; package tests 3435.
- Notes: PK-PT is a draft PR, https://creatio.ghe.com/engineering/crt-process-builder/pull/96, opened 2026-10-14
  (first commit b6dcd2c1; review gate 1 run, three lenses, all findings applied; then `260bb8ce`, the burned 1.6.6.97
  restamp, `b92547cc`, the item-stamp fix V4 found, `d6ca4d79`, the superseded 1.6.6.98 restamp, and the gate-3 fixes
  `11b8088c`, `21d0b016`, `dcfdc83f`, cut as the next claimed number). T-68 is
  root-caused and fixed in code (the nested items of a user task synced in the same request had no owning schema; the
  for-mapping walk adopts them; the regression test drives the platform's real sync); on the stand manual TC-04 on
  ENG-95984 passed on 2026-10-14, and E2E-06 passed with V2. Open, not decided: whether a
  single-instance sub-process's FileCollection-shaped `In` parameter joins the file-consuming targets (spec "Open
  owner items"). CL-PT (https://github.com/Advance-Technologies-Foundation/clio/pull/1814) and KB-PT
  (https://github.com/Advance-Technologies-Foundation/clio-knowledge/pull/285) were opened as drafts on 2026-10-14.
