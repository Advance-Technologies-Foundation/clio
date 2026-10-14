# Test Plan: File process parameter type (ENG-95984)

**Feature**: eng-95984-file-parameter-type
**Jira**: ENG-95984 File process parameter type (Task; epic ENG-92704 Create BP via AI Toolkit)
**Spec**: [spec-eng-95984-file-parameter-type.md](../prd/spec-eng-95984-file-parameter-type.md) - AC-1..AC-10
**ADR**: [adr-eng-95984-file-parameter-type.md](../adr/adr-eng-95984-file-parameter-type.md) - D1-D9, D23, D25, D26
**Stories**: [1](../stories/story-eng-95984-file-parameter-type-1.md) · follow-ups
[2](../stories/story-eng-95984-file-parameter-type-2.md) · [3](../stories/story-eng-95984-file-parameter-type-3.md) ·
[4](../stories/story-eng-95984-file-parameter-type-4.md)
**Source**: the analysis test plan in `spec/eng-95984-file-parameter-type/`, whose PU / CU / E2E / KU / V ids are kept
in the "Orig" column below. The full analysis (`spec/eng-95984-file-parameter-type/`,
`spec/eng-92719-file-processing-element/`) lands with the docs PR titled `ENG-92719 File processing element`; this plan
does not depend on it.
**Status**: Draft (the cases follow the owner-agreed contract; the cases that move with an open item are in "Cases
that move with an open decision")
**Created**: 2026-10-13

Path aliases: `PB/` = crt-process-builder `packages/CrtProcessBuilder/Files/src/cs/`; `PBT/` =
`tests/UnitTests/CrtProcessBuilder.Tests/`; clio and clio-knowledge paths are relative to their repository roots.

---

## Levels and id scheme

| Level | Project | Ids here | Orig ids | What it can prove | What it cannot |
|---|---|---|---|---|---|
| Unit, package | crt-process-builder `PBT/` (NUnit, NSubstitute, FluentAssertions) | TC-U-01..TC-U-55 (TC-U-nn = PU-nn; TC-U-55 = the T-68 regression) | PU-01..PU-54 | type resolution, the stored parameter shape, every refusal, the metadata each mapping writes (both levels, meta paths, `ModifiedInSchemaUId`), describe projection and decode, short-key serialization; the platform's user-task parameter sync on `SchemaUId`, through the harness recipe under "Conventions" (TC-U-55 runs it) | a `ProcessSchemaManager` save, the platform's pre-save validation, the runtime reader (`internal`), the designer |
| Unit, clio | `clio.tests` (`Module=ProcessModel`, `McpServer`, `Common`, `Command`) | TC-U-61..TC-U-70 (TC-U-6n = CU-0n, TC-U-70 = CU-10) | CU-01..CU-10 | the describe DTO keeps what the server sends; description wording; floor literals; archive pins; the ManagerMap arm | anything the server does |
| Unit, knowledge | clio-knowledge `automation/Clio.Knowledge.Bundle.Tests` | TC-U-71..TC-U-75 | KU-01..KU-05 | the guide states each rule; size budgets; registration | whether agents follow it |
| Integration (MCP e2e) | `clio.mcp.e2e`, category `McpE2E.ProcessDesigner` | TC-I-01..TC-I-13 | E2E-01..E2E-13 | JSON member names, the refusal envelope and notices over the real MCP path, against a stand on the cut | run-time behaviour. TeamCity excludes this category, so the manual run is the only e2e evidence |
| Cross-ticket (MCP e2e) | `clio.mcp.e2e`, in the clio PR of ENG-96506 Generated report + process parameter modes | XT-14a..XT-14d: cross-ticket, planned in ENG-96506's clio PR; not in this story's exit criteria | E2E-14a..E2E-14d (element test plan TC-91) | the Process-parameter variant's `Files` rules over the wire | - |
| Manual (stand) | a disposable .NET Framework stand, core 10.1.37 | TC-M-01..TC-M-03 (day 0, done); TC-M-10..TC-M-18 (TC-M-1n = Vn) | M3, MI-0, M6, V0..V8 | the parameter sync, the runtime reader, files really copied, the designer opening builder output | - |

Each manual case states the level it reaches: **Stored** (metadata written), **Design time** (the designer opens it)
or **Runtime** (the process ran and `SysProcessElementLog` shows it).

## Scope

### In scope

- D1-D9 of the ADR at package-unit level; the clio surface (DTO pass-through, descriptions, floor, pins, ManagerMap
  arm, budgets) at clio-unit level; the MCP contract over the wire; the `process-files` guide and the rewritten
  multi-instance passages; the stand questions no unit test can reach.

### Out of scope, with reason

| Excluded | Reason |
|---|---|
| The Process file element's configuration (source object, storage, filter, report, action) | ENG-96505 Element readiness and object attachments mode and ENG-96506 Generated report + process parameter modes |
| The Process-parameter variant's `Files` over the wire (XT-14a..d) | cross-ticket, planned in ENG-96506's clio PR (owner item O-TP1, recommended B): the generic `userTask` route for the file schemas is refused two PRs later, so a test built on it would turn red. Until then AC-5's Process-variant half and AC-7's nested-only listing have package-unit evidence |
| End-to-end file copying through the Process-parameter variant (M1, M2) | ENG-96506 measurements; they decide AC-9's wording, not this ticket's code |
| Send email and Creatio.ai file inputs | ENG-95985 Send email attachments; Creatio.ai is out of scope |
| Describe decode of process-parameter sources | follow-up story 4; until then those bindings replay as expressions (TC-U-43, TC-U-52) |
| A declared item shape for any Collection; an allow-list of mirrorable types | follow-up stories 2 and 3 |
| Passing a file as a `run-process` start value | no tested string form of a file exists (source, unmeasured); in this ticket a file reaches a process parameter through a mapping only |
| A Binary test flip | Binary stays refused (D1); one probe moves to another unsupported name instead (TC-U-04) |
| A ClioRing gate run | Ring consumes only catalog names, Purpose text and Destructive flags, none of which change; the PR states "ClioRing compatibility reviewed, no Ring-consumed contract changed". Still owed: confirm the Purpose leads of the touched tools are unchanged in `get-tool-contract` `detail=index-entries` (a Q20 trim could change a lead) |

## Risk assessment

| # | Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| R-1 | The name `"File"` reaches `GetInstanceByName` and creates the BLOB type `BA40CFC5`, which no designer page offers and the runtime cannot hold | Med | High | TC-U-01 asserts the FileLocator UId AND not BA40CFC5 for every alias |
| R-2 | An outer-only collection mapping onto a file consumer saves green and fails at run time (rows `{File: null}`) | High without P2 | High | TC-U-31..TC-U-35, TC-U-41, TC-I-08, TC-I-09; TC-M-17 at run time |
| R-3 | P1 changes what an existing item-only multi-instance mapping writes (1 iteration becomes N) and nobody notices | Certain (intended) | Med | stated as visible change (1) in the PR; TC-U-29, TC-U-46, TC-I-06; TC-M-13 measures 3 against 1; guidance rewritten under a version gate (TC-U-73) |
| R-4 | The rules leak to callers with scalar targets (`setConnections`, approvals) or to non-file collections | Med | High | the whole package suite before every push; TC-U-36, TC-U-37, TC-U-42 (Read data -> multi-instance untouched) |
| R-5 | A refusal fires after a partial write | Low | High | TC-U-44 (every target resolved and type-checked before the first write); every refusal case asserts the schema unchanged |
| R-6 | The create path still throws on a nested-item mapping (T-68) | Low (fixed in code in the package PR, 2026-10-14; not yet proven on a stand) | Med | TC-U-55 drives the platform's real sync, builds the elements and a nested mapping in ONE create request, and asserts the value `addMapping` stores; TC-I-06 sends one over the wire, and manual TC-04 on ENG-95984 repeats it |
| R-7 | Describe reports a decoded pair the write side would not store the same way, so a replay changes the binding | Med | High | TC-U-49, TC-U-54 (negative set), TC-U-52 round trip; the decode names a source only when replaying it changes no other level (ADR D8) |
| R-8 | The serialization comparison "passes" by a rule nobody wrote down | Med | Med | the AC-8 comparison rule below is a finite list; anything else is a DIFF (TC-U-13, TC-M-14) |
| R-9 | A description edit breaks the floor guards or the budget | High (budget full) | High | TC-U-66, TC-U-67; the swap is a reviewer-approved trim (Q20 = A, 2026-10-14) |
| R-10 | An e2e that went to `Assert.Ignore` on an older package is read as a pass | Med | High | report Passed with Ignored = 0 (TC-M-12) |
| R-11 | The session's MCP clio is older than the PR and silently drops unknown arguments | Med | Med | TC-M-11 checks `get-tool-contract` mentions `File` before any other stand case |
| R-12 | A stale knowledge cache keeps serving the old guidance after a failed `update-knowledge` | Med | Med | TC-M-16 (`info-knowledge` shows the new libraryVersion) |
| R-13 | A package test names a `Terrasoft.Configuration` type and builds only where that DLL exists | Low | Med | mirrored constants; `dotnet build MainSolution.slnx -c dev-n8` once per PR when the net-core application tree exists |
| R-14 | A single-instance sub-process's FileCollection-shaped `In` parameter is not a file-consuming target, so an outer-only `SP.Docs <- OF1.ObjectFiles` gets neither P2 nor R-M2 and may run with `{File: null}` rows (open item, recorded 2026-10-14, not decided) | unknown (M3 measured a process-parameter target only) | High if the rows are null | a stand measurement (M27 of the analysis) on the cut, then an owner decision; no case here changes until it is decided |

## Conventions and commands

- **Package fixtures (crt-process-builder house rule)**: `[TestFixture(Category = "UnitTests"), Category("PreCommit")]`
  and nothing else (without them Jenkins skips the fixture and the stage stays green; a second `[Category]` breaks the
  package's base fixture). This rule belongs to crt-process-builder only: clio's `project-context.md` bans
  `UnitTests` for clio fixtures, which use `[Category("Unit")]`. C# 7.3. Explicit `// Arrange`, `// Act`,
  `// Assert`; `because:` on every assertion; `[Description]` on every test. Tests never name
  `Terrasoft.Configuration` types: mirror schema UIds, parameter names and `ResultActionType` integers.
- **clio.tests and clio.mcp.e2e (clio AGENTS.md test style)**: explicit Arrange / Act / Assert; a `because` on every
  assertion; `[Description]` on every test; every test runnable on macOS, Linux and Windows, with no OS-specific paths
  or commands.
- **Package harness**: elements are hand-built with the platform's real schema UIds and literal parameter names
  (Process variant `6c620dd2-026e-560c-489f-030c5be5f2c3`, Object `9387c794-8d84-5925-ab77-c47e7d876286`, Report
  `c2bf0416-54c6-6c56-58e0-41162c7795f0`), through `PBT/FileProcessingTestSupport.cs`, whose literal template table is
  guarded against the shipped metadata by TC-U-50. Build order: a host with an empty UId, elements with `SchemaUId`,
  then `host.UId = Guid.NewGuid()`, then the process-level parameters through the service, so every
  `ModifiedInSchemaUId == host.UId` assertion is not vacuous. Register every task schema a test needs on ONE
  user-task schema manager. The platform's own user-task sync DOES run in the harness: substitute the
  `ProcessSchemaManager` with an `AppConnection` that answers, give the workspace a `SchemaManagerProvider` that returns
  the user-task manager, and name the element before assigning `SchemaUId` (recipe:
  `FileCollectionMappingTests.WireTheSyncManagers` / `PlaceBySync`); TC-U-55 uses it. Still stand cases: a
  `ProcessSchemaManager` save, the platform's pre-save validation, running a user task. Serialization goes through the platform `JsonDataWriter`; assertions use short keys (`L1`
  type, `L8` source value, `L9` reference schema, `L12` direction, `L17` Tag, `L18` items, `IL2` container).
- **clio unit**: `[Category("Unit")]` with the fixture's `[Property("Module", ...)]`; filter by `TestCategory`, never a
  `Category=` alias; run from bash with `< /dev/null`; check the executed count, not the exit code. No command-class
  change, so no `BaseCommandTests<T>` fixture; a command test added later stubs `IProcessDescriber.Describe` (the write
  commands read the process back on every success).
- **e2e**: `[NonParallelizable]`, unique `UsrClioBpFile<Case>E2e{Guid:N}` names, no process RUN inside a fixture,
  arrange through `ProcessDesignerE2EArrange.StartAsync(subject, MinimumPackageVersion)`, every describe read-back with
  `mode=full` (or `element=<name>`). "Bound to X" means the parameter's `value` contains `[Parameter:{<X uid>}]` (GUIDs
  compared case-insensitively) and `source` is `Script`.

```bash
# package, from a short-path worktree with .application copied in (net472 = what CI runs)
dotnet test tests/UnitTests/CrtProcessBuilder.Tests/CrtProcessBuilder.Tests.csproj -c dev-nf \
  --filter "FullyQualifiedName~FileProcessParameterTests|FullyQualifiedName~FileCollectionMappingTests|FullyQualifiedName~ElementSourceDecodeTests|FullyQualifiedName~FileProcessingTemplateDriftTests|FullyQualifiedName~ProcessParameterServiceTests|FullyQualifiedName~ProcessDescriberTests|FullyQualifiedName~ParameterTypeCompatibilityTests|FullyQualifiedName~MultiInstanceMappingTests"
dotnet test tests/UnitTests/CrtProcessBuilder.Tests/CrtProcessBuilder.Tests.csproj -c dev-nf   # before every push
# clio unit: four modules change, so the full unit suite is mandatory before commit.
# Build first, then run with --no-build; filter by TestCategory, never by a Category= alias
dotnet build clio.tests/clio.tests.csproj -c Debug
dotnet test clio.tests/clio.tests.csproj --no-build --filter "TestCategory=Unit" < /dev/null
# e2e, one fixture per run, sequentially (a parallel schema-write burst downs a .NET Framework app pool)
McpE2E__Sandbox__EnvironmentName=<env> dotnet test clio.mcp.e2e/clio.mcp.e2e.csproj -c Debug -f net10.0 \
  --filter "TestCategory=McpE2E.ProcessDesigner&FullyQualifiedName~FileParameterToolE2ETests" < /dev/null
# knowledge, in its own worktree
dotnet test automation/Clio.Knowledge.Bundle.Tests/Clio.Knowledge.Bundle.Tests.csproj
```

## Regression scope

- **Package**: the whole suite before every push, because `ApplyMapping` is the shared funnel of the build, the
  mirror, `EntityConnectionBinder` and `ApprovalApplier`. Must stay green unchanged: the Binary refusal pin in
  `ProcessParameterServiceTests` (TC-U-03), the flat process-parameter mapping tests (TC-U-25), `RecordColumnSourceTests`
  (TC-U-51), `PackageDescriptorTests` after the restamp, and the existing `setConnections` / approval tests.
- **clio**: the full unit suite; specifically `EnforcedFloorSentences_ShouldEqualTheRequiresPackageLiteral`,
  `FloorSentences_ShouldNotCreditTheEnforcedFloorWithTheCollapse`,
  `GetToolContracts_ShouldFitTheSevenProcessDesignerContractsInline_WhenRequestedTogether`, the three call-shape drift
  tests in `ToolContractShortFormTests`, `WorkspaceTemplateGuidanceDriftTests`, `ProcessDesignerEmittedSchemaTests`,
  and `ProcessParameterBindingWarningsTests` (`clio.tests/Command/ProcessModel/`). Since ENG-103081 Warn when a
  process-parameter mapping is bound twice or to a branch some start skips (on clio master since 2026-10-14),
  `clio/Command/ProcessModel/ProcessParameterBindingWarnings.cs` reads `targetProcessParameter` from every create
  mapping and `addMapping`, so a dotted name such as `Docs.File` is new input to it; it looks a parameter up by its
  whole name (`:233-235`), so a dotted target matches no stored parameter (source).
- **e2e** (same stand, one fixture at a time after the new one, each result recorded in the PR):
  `SubProcessMultiInstanceToolE2ETests` (its caller maps both levels explicitly, so P1 is a no-op there),
  `ModifyBusinessProcess_Should_RejectUnsupportedParameterType`, `RecordColumnSourceToolE2ETests`,
  `DescribeProcessToolE2ETests`, `SubProcessElementToolE2ETests`, `ProcessParameterBindingToolE2ETests` (ENG-103081;
  it triggers process runs, so it needs the user's go-ahead like every stand write).
- **knowledge**: `ProcessGuideResponseSizeTests`, `GuidanceInlineReplyBudgetTests`, `ProcessDigestTests`,
  `ProcessBuilderInstallConsentGuidanceTests`, `ClioToolSyncGuidanceTests`, `ProcessSplitCoreRuleTests`,
  `InternalTaskReferenceTests`, `GuidanceMigrationTests`, `ProcessGuideCrossReferenceTests`.

---

## Unit cases: package (crt-process-builder)

New fixtures: `PBT/FileProcessParameterTests.cs` (D1, D2, D3, D6, D7), `PBT/FileCollectionMappingTests.cs` (D4, D5),
`PBT/ElementSourceDecodeTests.cs` (D8), `PBT/FileProcessingTemplateDriftTests.cs` (drift guard); helper
`PBT/FileProcessingTestSupport.cs`. Extended: `ProcessParameterServiceTests`, `ParameterTypeCompatibilityTests`,
`ProcessDescriberTests`, `MultiInstanceMappingTests`. Refusal and notice ids (F-T1..F-T7, F-M1..F-M5) are the ADR's
catalogue; a test asserts the catalogue's key words, not the full sentence. Every refusal case also asserts that the
schema is unchanged.

### Types and aliases (D1)

| ID | Orig | Arrange / Act | Assert | AC |
|---|---|---|---|---|
| TC-U-01 | PU-01 | `addParameter` with `File`, `file`, `FileLocator`, `filelocator`, `file locator` (TestCases) | type UId `A33C9252-D401-453E-949D-169157067ED9`, NOT `BA40CFC5-F554-4C26-8F57-1BB29CF43C4E`; Variable; no Tag; no reference schema | AC-1, AC-2 |
| TC-U-02 | PU-02 | `addParameter` with `Binary`, `binary`, `Blob` (F-T1) | `ArgumentException` containing `not supported`, the given name, `File` and `FileCollection`; no parameter added | AC-1 |
| TC-U-03 | PU-03 | existing `AddProcessParameter_ShouldThrow_WhenTypeNotSupported` (Binary) | unchanged, stays green | AC-1 |
| TC-U-04 | PU-04 | existing `AddProcessParameter_ShouldNameCollection_InUnsupportedTypeMessage` | probe type moves from `Binary` to `Image` (or `Color`) with a precondition that the harness resolves it; the generic message lists `File, FileCollection`; assert `*Collection*` and `*File*` | AC-1 |
| TC-U-05 | PU-05 | describe a File parameter, feed the reported type back into `addParameter` | reported type `FileLocator`; the rebuilt parameter has the same type UId | AC-3 |

### FileCollection shape and read-back (D2, D3)

| ID | Orig | Arrange / Act | Assert | AC |
|---|---|---|---|---|
| TC-U-06 | PU-06 | `addParameter` with `FileCollection`, `file collection`, `collection of files`, on a schema given a UId first | type `CompositeObjectList` `651EC16F`; exactly one item: `Name` and `Caption` `File`, FileLocator, Variable, Tag null, `ContainerUId == schema.UId` (non-empty), source None, fresh UId; the item's meta path contains `[Parameter:{<item UId>}]` and no `[Element:` segment | AC-1 |
| TC-U-07 | PU-07 | `ToDescribeParameter` of a process-level `CompositeObjectList`, no Tag, one FileLocator item | `type: "FileCollection"`, `itemProperties` = `[File: FileLocator]` | AC-3 |
| TC-U-08 | PU-08 | the same projection for (a) a Tag (a mirror), (b) two items, (c) one non-file item, (d) an ELEMENT parameter of the same shape (`Files`) | `type: "CompositeObjectList"` in all four: the predicate is process-level only | AC-3 |
| TC-U-09 | PU-09 | build -> describe -> rebuild from the described `name`, `type`, `direction` | same item name, type and direction; no Tag; new UIds | AC-3 |
| TC-U-10 | PU-10 | a designer-made FileCollection with item `FileCollectionParameter` | described as `FileCollection` with its real item name; a rebuild names the item `File`; a replayed mapping onto `Docs.FileCollectionParameter` is refused naming the available item `File` (F-T7) | AC-3 |
| TC-U-11 | PU-11 | `FileCollection` with no direction; with `direction: In`; item direction | root Variable (one constant in the fixture); explicit In wins; item Variable | AC-2 |
| TC-U-12 | PU-12 | `setParameter direction: In` on a FileCollection | allowed | AC-2 |
| TC-U-13 | PU-13 | serialization through `JsonDataWriter`, on a schema given a UId before the parameters are added, of: a File; a FileCollection declared with `direction: Out` (explicitly); a FileCollection declared with no direction (the Variable default, D3) | File: `L1 = a33c9252-...`, no `L12`, `L17`, `L9`; `L8` with no source or value; `IL2` = the schema UId. Explicit-Out FileCollection: `L1 = 651ec16f-...`, `L12 = 1`, `IL2`; one `L18` entry `A2 = File`, FileLocator, `IL2`, no `L12`, no `L17`; equal to `MarkProcessesToCancel.FilesCollection` under the AC-8 rule. Default FileCollection: no `L12`; equal to `FileParameterProcess.FileCollection` under the same rule (row PT-b applies) | AC-8 |

### The AC-8 comparison rule

"Equal apart from the UIds" cannot pass as written: the builder writes `IL2` where the designer writes nothing, and
the Variable oracle is a bound collection with a different item name. The rule is this list and nothing else; a
difference not in the list is a DIFF, and one DIFF fails. It is used by TC-U-13 and TC-M-14 (V4). N-numbers are the
analysis's serialization-capture rows; PT-a and PT-b are this ticket's.

| # | Key or row | Designer capture | Builder | Rule | TC-U-13 | TC-M-14 |
|---|---|---|---|---|---|---|
| N5 | parameter and item `UId` | fresh | fresh | ignored; parameters paired by `A2`, items by `A2` under their parent | yes | yes |
| PT-a | schema UIds inside `A3`, `A4`, `GS5` and `IL2` | `<P>` | `<P>` | role-replaced (`<P>`). A key present on one side only is a DIFF unless N9 or N15 covers it | not compared, and what the builder side carries is pinned instead: the harness DOES stamp `A3` = `A4` = the schema UId on a root parameter added to `schema.Parameters` (the platform's collection insert), not on its item; an unbound value carries no `GS5` | yes |
| N9 | `IL2` on a process parameter and its items | absent (0 of 3,335 process parameters and 0 of 678 items in the 7.8.0 corpus) | `<P>` (`ContainerUId = schema.UId`) | normalised: absent equals `<P>`; the meta path is the same either way; TC-M-15 checks that the designer opens it | pinned (present) | normalised |
| N15 | "no value" `L8` forms: `{}`, `{"GS5": <P>}`, no `L8` | `{"GS5": <P>}` | no `GS1` / `GS2` | equal | yes | yes |
| N14 | resource culture | authoring culture | request culture | compare the authoring culture's `Caption` rows only | - | yes |
| PT-b | `FileParameterProcess.FileCollection` only (the Variable oracle): the item's `A2` and Caption row, both levels' `L8`, the `DisplayValue` resource rows | item `FileCollectionParameter`; both levels bound; two `DisplayValue` rows | item `File`; both levels unbound; no `DisplayValue` row | excluded. The item is still compared on count (1), `L1`, absent `L12`, absent `L17`; its name `File` is asserted on its own (TC-U-06). PT-b falls away when a current-designer capture with the D2 item name is the oracle | yes | yes |

Not in the list on purpose: the element exceptions of the analysis's serialization rule (N1-N4, N6-N8, N10-N13, N16).
TC-U-13 and TC-M-14 compare process parameters and their resource rows only and build no element, so an element key in
the diff is a DIFF. A new tolerated deviation needs the owner and a new row here.

### Refusals (D2, D6, D7)

| ID | Orig | Arrange / Act | Assert | AC |
|---|---|---|---|---|
| TC-U-14 | PU-14 | `referenceSchema: "Contact"` with `File` and with `FileCollection` (F-T2) | refused, message names `referenceSchema` (without it File silently becomes a Lookup) | AC-2 |
| TC-U-15 | PU-15 | `value` on File through `addParameter` and through `setParameter` (F-T3) | refused, "no constant form" | AC-2 |
| TC-U-16 | PU-16 | `addMapping value` onto a FileLocator process parameter, a FileCollection, `Files` and `Files.File` of a hand-built Process variant | refused in `ValidateConstantValue` | AC-2 |
| TC-U-17 | PU-17 | `removeParameter Docs` while (a) `PF1.Files.File` alone references `Docs.File`, (b) a formula body references `Docs.File` | refused, the usage named | AC-2 |
| TC-U-18 | PU-18 | `setParameter type: FileCollection` on (a) a Read data mirror collection, (b) a bare collection; controls: on a FileCollection, and `type: File` on a File | (a), (b) refused (F-T4); controls are no-ops that succeed | AC-2 |
| TC-U-19 | PU-19 | mirror of a file output: `typeFromElement: OF1`, `typeFromElementParameter: ObjectFiles` | still refused; the message says declare `type: FileCollection` and map it from `OF1.ObjectFiles` (F-T6) | AC-6 |
| TC-U-20 | PU-20 | dotted mirror `typeFromElementParameter: "ObjectFiles.File"`, on add and on the setParameter re-mirror path | refused naming the item and its collection (F-T5); `[Description]` records the regression (it silently created an unbound parameter on 1.6.6.54) | AC-6 |

### Dotted process-parameter paths (D4)

| ID | Orig | Arrange / Act | Assert | AC |
|---|---|---|---|---|
| TC-U-21 | PU-21 | `addMapping` with source `processParameter: "Docs.File"` | the stored formula addresses `[Parameter:{<Docs.File UId>}]`, no `[Element:` segment; type-checked | AC-4 |
| TC-U-22 | PU-22 | `targetProcessParameter: "Docs.File"` from `OF1.ObjectFiles.File` | the item is written and type-checked; it keeps `ContainerUId = schema.UId` | AC-4 |
| TC-U-23 | PU-23 | `Docs.Nope` and `Nope.File` | refused, each naming the failing segment; `Docs.Nope` lists `File` | AC-4 |
| TC-U-24 | PU-24 | a process parameter literally named `A.B` | resolves flat first | AC-4 |
| TC-U-25 | PU-25 | the existing flat process-parameter mapping tests | unchanged and green (the refactor is behaviour-neutral for flat names) | AC-4 |

### The two-level binder (D5)

"PF1" is a hand-built Process variant, "OF1" an Object variant, "RP1" a Report variant, "RD" a Read data element in
collection mode, "SP1" a multi-instance sub-process. Every case asserts both levels' source value (source, formula,
`ModifiedInSchemaUId == host.UId`) and, where it says "notice", the exact notice list the result returns.

| ID | Orig | Rule | Arrange / Act | Assert | AC |
|---|---|---|---|---|---|
| TC-U-26 | PU-26 | P1 | `PF1.Files.File <- processParameter Docs.File` | item bound; `PF1.Files <- Docs` too | AC-5 |
| TC-U-27 | PU-27 | P1 | `PF1.Files.File <- OF1.ObjectFiles.File` | `PF1.Files <- OF1.ObjectFiles` | AC-5 |
| TC-U-28 | PU-28 | P1 | `targetProcessParameter Docs.File <- OF1.ObjectFiles.File`, `<- RP1.ReportFiles.File`; `ResultActionType` 0 and 1 (TestCases) | `Docs <- OF1.ObjectFiles` / `<- RP1.ReportFiles`, identical for both action values | AC-5, AC-6 |
| TC-U-29 | PU-29 | P1 | `SP1.InputRecordCollection.F <- Docs.File`; and `SP1.InputRecordCollection.Name <- RD.ResultCompositeObjectList.Name` | parent bound in both (P1 is type-agnostic): visible change (1) | AC-5 |
| TC-U-30 | PU-30 | P1 refuse | `PF1.Files` bound to `Other`; then `PF1.Files.File <- Docs.File` | refused (F-M1) naming `Files`, `Other`, `Docs`; both levels keep their values. Control: the same source again is a no-op | AC-5 |
| TC-U-31 | PU-31 | P2 name | `PF1.Files <- Docs` | `Files.File <- Docs.File`; notice names `Files.File` | AC-5 |
| TC-U-32 | PU-32 | P2 single | `PF2.Files <- PF1.ObjectFiles` (item `ObjectFile`) | `Files.File <- ObjectFiles.ObjectFile` by the single-FileLocator fallback; notice | AC-5 |
| TC-U-33 | PU-33 | P2 overwrite / stale | (a) `Files.File` already bound to `Docs.File`, then `Files <- Docs`; (b) `Files.File` bound into `OF1.ObjectFiles`, then `Files <- Docs` | (a) item untouched, no rebind notice; (b) item rebound to `Docs.File`, notice says it was stale | AC-5 |
| TC-U-34 | PU-34 | P2 unpaired | the source has two FileLocator items, neither named `File` | outer written; notice "at run time it will be empty"; no item written | AC-5 |
| TC-U-35 | PU-35 | P2 scope | `Docs2 <- Docs` (both FileCollections) | `Docs2.File <- Docs.File` | AC-5 |
| TC-U-36 | PU-36 | P2-MI | `SP1.InputRecordCollection <- RD.ResultCompositeObjectList`, callee has `Id` (Guid) and `F` (File); also over `OF1.CreatedObjectFileIds` | ACCEPTED; notice names `InputRecordCollection.F`; no item written (regression 1) | AC-5 |
| TC-U-37 | PU-37 | regression | the same element with explicit outer and per-item mappings | exactly the values 1.6.6.54 writes (fixture literal), nothing extra (regression 2) | AC-5 |
| TC-U-38 | PU-38 | P3 | single-instance `PF1`: `Files <- Docs`, then `Files.File <- Doc` (a File) | item <- `Doc`; `Files` reset to None; notice that it was cleared | AC-5 |
| TC-U-39 | PU-39 | nested-only | fresh `PF1`: `Files.File <- Doc` | item bound; `Files` stays Source None (metadata only; the run-time read is M1, ENG-96506's) | AC-5, AC-9 |
| TC-U-40 | PU-40 | R-M1 | `targetProcessParameter Doc <- OF1.ObjectFiles.File`; `Doc <- Docs.File` | refused (F-M2) naming FileCollection and multi-instance | AC-5 |
| TC-U-41 | PU-41 | R-M2 | `PF1.Files <- RD.ResultCompositeObjectList`; `Docs <- RD.ResultCompositeObjectList`; `Docs <- OF1.CreatedObjectFileIds` | refused (F-M3) | AC-5 |
| TC-U-42 | PU-42 | R-M2 scope | `RD.ResultCompositeObjectList` onto (a) `SP1.InputRecordCollection` whose callee has no File, (b) another user task's collection | accepted as on 1.6.6.54 | AC-5 |
| TC-U-43 | PU-43 | expression | `expression` onto `PF1.Files`, then onto `PF1.Files.File` | both written as given (meta-path check passes, GUIDs lower-cased); no P1, P3 or refusal; the other level untouched (regression 3) | AC-5 |
| TC-U-44 | PU-44 | ordering | a two-level write whose second target does not resolve | nothing written | AC-5 |
| TC-U-45 | PU-45 | `BindCollection` | explicit pairs; null pairs | pairs written as given; null pairs behave as TC-U-31 / TC-U-32 | AC-5 |
| TC-U-46 | PU-46 | dotted source | extend `ApplyMapping_ShouldResolveADottedSource_WhenTheValueComesFromAnotherCollection` | `InputRecordCollection` now also bound to `ReadOrders.ResultCompositeObjectList` | AC-5 |
| TC-U-55 | (T-68) | create path | two file elements placed by the platform's REAL sync (the harness recipe under "Conventions") and a nested-item mapping in ONE create request through `ProcessBuildHandler`; precondition: the synced nested item has no owning `ProcessSchema` | no exception, no notice; the create path stores exactly the value `addMapping` stores on the same elements loaded for modify, P1's parent included. A sibling covers `addElement` + `addMapping` in one modify batch. Implemented in the package PR (root cause: the platform's copy of a template item owned by a `ProcessUserTaskSchema` has no owning schema; fix: the for-mapping walk adopts each descended item); TC-I-06 and manual TC-04 on ENG-95984 are the stand proof | AC-5 |

### Type compatibility (no code change; pins only)

| ID | Orig | Arrange / Act | Assert | AC |
|---|---|---|---|---|
| TC-U-47 | PU-47 | TestCases through `ParameterTypeCompatibility.AreCompatible` and `AreCompatibleByStoredReference` | FileLocator -> FileLocator true; Binary -> FileLocator false; File (BLOB) -> FileLocator false; FileLocator -> Text false; `CompositeObjectList` -> `CompositeObjectList` true | AC-1, AC-5 |

### Describe (D8)

| ID | Orig | Arrange / Act | Assert | AC |
|---|---|---|---|---|
| TC-U-48 | PU-48 | `PF1.Files` unbound, `Files.File` bound in this schema | `Files` is listed, its item carries the value. Controls: nothing bound = not listed; a nested value stamped by another schema = not listed; a sub-process element still lists every parameter | AC-7 |
| TC-U-49 | PU-49 | a stored Script that is exactly one element-parameter token: (a) `ObjectFiles`, (b) the nested `ObjectFiles.File` | `sourceElement` = `OF1`; `sourceElementParameter` = `ObjectFiles` / `ObjectFiles.File`; `sourceColumn` null; `value` unchanged | AC-7 |
| TC-U-51 | PU-51 | existing record-column decode tests | unchanged and green; before the PR, re-read every `SourceElement.Should().BeNull` hit, because a two-segment value that reported no pair now reports one | AC-7 |
| TC-U-52 | PU-52 | build PF1 / OF1 / SP1 / Docs with every binding kind above -> describe -> replay (structured where decoded, `expression` otherwise) on a fresh copy | both levels of every collection binding identical (regression 4) | AC-3, AC-5, AC-7 |
| TC-U-54 | PU-54 | the decoder, negative | NO pair for: a multi-token formula; a process-parameter source (follow-up story 4); a stale element UId; a dot-ambiguous nested name the write side resolves flat-first to a sibling; a short-form or re-cased prefix | AC-7 |

### Mirror item binding (D9) and the drift guard

| ID | Orig | Arrange / Act | Assert | AC |
|---|---|---|---|---|
| TC-U-50 | PU-50 | `FileProcessingTemplateDriftTests`: the three shipped user-task metadata files copied to `PBT/Fixtures/FileProcessing/` with `PROVENANCE.md` (source, CrtProcessDesigner 7.8.0, SHA-256), read by ancestor search, deserialised with the platform reader | precondition 13 / 8 / 13 parameters; the helper template equals the capture on name, UId, type UId, effective direction, IsRequired, items; each SHA-256 equals `PROVENANCE.md`; the set is unchanged after `SchemaUId` is assigned | supports AC-5, AC-7 |
| TC-U-53 | PU-53 | extend `AddProcessParameter_ShouldBindMirroredCollection_ToSourceOutput` | the mirror binds every item to the source item of the same name, not the root only; the test's premise in its `[Description]` corrected | D9 (visible change 5) |

---

## Unit cases: clio

| ID | Orig | Fixture | Arrange / Act | Assert | AC |
|---|---|---|---|---|---|
| TC-U-61 | CU-01 | `clio.tests/Command/ProcessModel/ServerProcessDescriberTests.cs` | server JSON with a `FileLocator` parameter and a `FileCollection` parameter (Out, `itemProperties: [{name: File, type: FileLocator, direction: Variable}]`, no tag) | `DescribedParameter.Type` keeps both names verbatim; items kept; re-serialized, the types survive and `tag` is omitted, not null | AC-3 |
| TC-U-62 | CU-02 | same | element parameter `Files` with `sourceElement: OF1`, `sourceElementParameter: ObjectFiles`; its item with `"ObjectFiles.File"` | the pair is kept on both levels; `sourceColumn` null and omitted. The `SourceElement` doc comment is rewritten | AC-7 |
| TC-U-63 | CU-03 | `clio.tests/Command/McpServer/CreateBusinessProcessToolTests.cs` | read the `create-business-process` `[Description]` | the `parameters[]` type clause names `File` and `FileCollection`; the constant clause says refused on a Collection or File | AC-10 |
| TC-U-64 | CU-04 | `clio.tests/Command/McpServer/ModifyBusinessProcessToolTests.cs` | read the `modify-business-process` description | the `addParameter` type list names `File` and `FileCollection`; the constant clause as TC-U-63 | AC-10 |
| TC-U-65 | CU-05 | `clio.tests/Command/McpServer/DescribeProcessToolTests.cs` | read the describe description | the decoded-source clause covers "a value that IS another element's output" | AC-7, AC-10 |
| TC-U-66 | CU-06 | `clio.tests/Command/McpServer/ToolContractPayloadBudgetTests.cs` | re-measure create, modify, describe after the swap | the trimmed text under the 35,072 B ceiling (unchanged: Q20 = A, 2026-10-14); on clio master `79fab4786` create has 11 B and modify 2 B left (ENG-103081's re-measure), so the trims free at least 19 B and 26 B; the comment's figures rewritten; the seven contracts within 19,000 B on the wire and still short; the call-shape drift tests green | AC-10 |
| TC-U-67 | CU-07 | `clio.tests/Common/BundledProcessBuilderPackageTests.cs` | the rebundle to the final cut | the four pins; `EnforcedFloorSentences_ShouldEqualTheRequiresPackageLiteral` with the moved literal; `FloorSentences_ShouldNotCreditTheEnforcedFloorWithTheCollapse` (the phrase present in create, modify and `docs/McpCapabilityMap.md`, no floor literal in the 60 characters before it); the version-literal guards; operation counts unchanged | AC-10 |
| TC-U-68 | CU-08 | `clio.tests/Command/ProcessDesignerRequiresPackageAttributeTests.cs` | TestCases for create, modify and modify-as-new-version | moved to the final cut; each `[Description]` gains this ticket's reason | AC-10 |
| TC-U-69 | CU-09 | `clio.tests/Command/ProcessModel/ManagerMapResolveDataIdTests.cs` | `fileProcessing`, `processFile` and their lowercase forms | `EventType.UserTask`, not `Unknown` | AC-10 |
| TC-U-70 | CU-10 | `WorkspaceTemplateGuidanceDriftTests`, `ProcessDesignerEmittedSchemaTests` | unchanged | green: no `name=` pointer added, input schemas unchanged | AC-10 |

## Unit cases: knowledge (clio-knowledge)

| ID | Orig | Test | Assert | AC |
|---|---|---|---|---|
| TC-U-71 | KU-01 | NEW `FileParameterGuidanceTests.cs` over `guidance/mcp/guides/processes/files.md` | the guide states: the two types and FileLocator; the FileCollection read-back and item-name caveat; "a FileCollection defaults to Variable; a generic Collection to Out"; P1, P2, P2-MI, P3, R-M1, R-M2 and the file-consuming targets; "an `expression` goes through as given; only its meta-path tokens are checked"; the gate sentence with a concrete "from clio <version>" boundary (no placeholder, no Jira key); every read-back says `describe-business-process mode=full` (or `element=<name>`), under the gate "on clio older than 8.1.0.141, omit `mode`" that `process-modeling.md` owns | AC-10 |
| TC-U-72 | KU-02 | `CollectionParameterGuidanceTests.cs` re-pinned | the FileCollection exception to "never by re-typing the shape" stated; "binary" (not "file") in the not-supported list; the rationale names the alias exception | AC-3, AC-10 |
| TC-U-73 | KU-03 | NEW pins over the rewritten `sub-process.md` and `sub-process-when.md` passages | the version-gated sentence; "send both" kept for older servers; read-backs with `mode=full` | AC-5, AC-10 |
| TC-U-74 | KU-04 | `ProcessGuideResponseSizeTests`, `GuidanceInlineReplyBudgetTests`, `ProcessDigestTests` | every article under 27,793 characters; the new guide's part well below the about 21,000-character whole-guide target; process-modeling and the `process-digest` card untouched and green | AC-10 |
| TC-U-75 | KU-05 | `GuidanceMigrationTests` (`PostMigrationGuidance`, `BundleSource_ShouldPublishGuidanceAtItsItemIdPath`), `ProcessGuideCrossReferenceTests`, `ProcessBuilderInstallConsentGuidanceTests`, `ClioToolSyncGuidanceTests`, `ProcessSplitCoreRuleTests`, `InternalTaskReferenceTests` | registered (entry at `resources/process-files.md`, `requirements.itemIds`, `resourceUris`, routing row in the same generation); banner-less and NOT in `ProcessGuideSet.GoLiveFloor`; consent wording within 80 characters of every `install-process-builder`; no plain describe read-back; no Jira key or PR link | AC-10 |

---

## Integration cases: MCP end-to-end

New fixture `clio.mcp.e2e/FileParameterToolE2ETests.cs`: `[TestFixture]`, `[AllureNUnit]`, `[NonParallelizable]`,
`[Category(McpE2ECategories.ProcessDesigner)]`, `MinimumPackageVersion` = this ticket's final cut. It deliberately
avoids the generic `userTask` route for the three Process file schemas.

| ID | Orig | Test | Arrange / Act | Assert | AC |
|---|---|---|---|---|---|
| TC-I-01 | E2E-01 | `CreateBusinessProcess_Should_DeclareFileAndFileCollection_AndReadThemBack` | create with `Doc` (File), `Docs` (FileCollection), `InDocs` (FileCollection, In) | `Doc` FileLocator, Variable; `Docs` FileCollection, Variable, one item `{File: FileLocator}`, no tag; `InDocs` In | AC-1, AC-2, AC-3 |
| TC-I-02 | E2E-02 | `CreateBusinessProcess_Should_RebuildTheDescribedFileParameters_Unchanged` | project TC-I-01's described parameters to `{name, type, direction}`; create a second process | the second equals the first on name, type, direction, item name and type | AC-3 |
| TC-I-03 | E2E-03 | `ModifyBusinessProcess_Should_AddFileAndFileCollectionParameters` | `addParameter {name: Doc, type: "file"}` and `{name: Docs, type: "FileCollection", direction: "In"}` | as TC-I-01 | AC-1 |
| TC-I-04 | E2E-04 | `ModifyBusinessProcess_Should_RefuseBinary_AndNameTheFileTypes` | `addParameter {type: "Binary"}` | the result contains `not supported`, `Binary`, `FileCollection`; no new parameter. The existing `ModifyBusinessProcess_Should_RejectUnsupportedParameterType` keeps its assertions; only its `[Description]` and its Act comment ("a deferred complex type") are reworded to "refused for good: a file is type File" | AC-1 |
| TC-I-05 | E2E-05 | `ModifyBusinessProcess_Should_RefuseAConstantOnAFile` | `addParameter {name: Doc, type: File, value: "x"}`; `addMapping {targetProcessParameter: Docs, value: "[]"}` | both refused ("no constant form"); `Doc` absent, `Docs` unbound | AC-2 |
| TC-I-06 | E2E-06 | `CreateBusinessProcess_Should_BindTheInputCollection_WhenOnlyAFileItemIsMapped` | callee `F` (File, In); caller `Docs` (FileCollection, In), multi-instance `subProcess`; only `InputRecordCollection.F <- processParameter "Docs.File"` in `mappings[]` | `InputRecordCollection` bound to `Docs`; item `F` bound to `Docs.File` (P1, D4, and the create path of T-68) | AC-4, AC-5 |
| TC-I-07 | E2E-07 | `CreateBusinessProcess_Should_AcceptAFileCalleeOverReadDataRows_WithANotice` | callee `ContactId` (Guid, In) and `F` (File, In); Read data collection (Contact, columns `Id`, `Name`, 3 records); outer mapping plus `InputRecordCollection.ContactId <- ...ResultCompositeObjectList.Id` | success; `warnings` contains `InputRecordCollection.F`; `F` unbound (not refused: R-M2 is scoped to file-consuming targets) | AC-5 |
| TC-I-08 | E2E-08 | `ModifyBusinessProcess_Should_PairTheFileItem_WhenAFileCollectionIsMappedOntoAnother` | `Docs`, `Docs2` (FileCollections); `addMapping {targetProcessParameter: Docs2, processParameter: Docs}` | `warnings` names `Docs2.File`; `Docs2` bound to `Docs`, `Docs2.File` to `Docs.File` | AC-5 |
| TC-I-09 | E2E-09 | `ModifyBusinessProcess_Should_RefuseARecordCollectionIntoAFileCollection` | `addMapping {targetProcessParameter: Docs, sourceElement: ReadData1, sourceElementParameter: ResultCompositeObjectList}` | refused (R-M2); `Docs` unbound | AC-5 |
| TC-I-10 | E2E-10 | `ModifyBusinessProcess_Should_RefuseACollectionItemIntoASingleFile` | `addMapping {targetProcessParameter: Doc, processParameter: "Docs.File"}` | refused (R-M1), naming FileCollection and multi-instance | AC-5 |
| TC-I-11 | E2E-11 | `ModifyBusinessProcess_Should_RefuseADottedMirrorSource` | `addParameter {name: P, typeFromElement: ReadData1, typeFromElementParameter: "ResultCompositeObjectList.Name"}` | refused naming the item and its collection; no `P` | AC-6 |
| TC-I-12 | E2E-12 | `DescribeBusinessProcess_Should_DecodeAnElementOutputSource` | the TC-I-07 caller | `InputRecordCollection` reports `sourceElement: ReadData1`, `sourceElementParameter: ResultCompositeObjectList`; item `ContactId` reports `"ResultCompositeObjectList.Id"`; no `sourceColumn` | AC-7 |
| TC-I-13 | E2E-13 | `ModifyBusinessProcess_Should_BindOnlyTheItem_WhenAPlainFileFeedsAMultiInstanceInput` | multi-instance caller; `InputRecordCollection.F <- Doc` (a single File) | no binder notice; `InputRecordCollection` Source None, item `F` bound to `Doc` (a regression check of the binder, not the proof of the nested-only listing) | AC-5 |

### Cross-ticket cases (planned in ENG-96506; not in this story's exit criteria)

The Process-parameter variant's `Files` over the wire. These cases run in the clio PR of ENG-96506 Generated report +
process parameter modes, with `PF1` built by that ticket's `fileProcessing` block (owner item O-TP1, recommended B).
They are listed here for traceability only.

| ID | Orig | Arrange / Act | Assert | AC |
|---|---|---|---|---|
| XT-14a | E2E-14a (element test plan TC-91) | a process with `Docs` (FileCollection) and a Process variant `PF1`; map `Files <- Docs` | `warnings` names `Files.File`; `PF1.Files` bound to `Docs`, `PF1.Files.File` bound to `Docs.File` | AC-5 |
| XT-14b | E2E-14b (element test plan TC-91) | fresh process; map `Files.File <- Docs.File` | both levels bound (P1) | AC-5 |
| XT-14c | E2E-14c (element test plan TC-91) | fresh process; map `Files.File <- Doc` (a single File) | `Files` listed although its outer level is unbound (the only over-the-wire proof of the nested-only listing), item bound to `Doc` | AC-5, AC-7, AC-9 |
| XT-14d | E2E-14d (element test plan TC-91) | map `Files <- ReadData1.ResultCompositeObjectList` | refused (R-M2) | AC-5 |

---

## Manual cases: stand

Rules for every row: a disposable stand (core 10.1.37, .NET Framework). A clio built from master refuses the process
tools against a stand below its bundle, so the stand holds the base package or the cut under test, and every record
names the package version. Every write and every `install-process-builder` needs the user's explicit go-ahead, row by
row (an install restarts the site for every connected user). Schema writes and runs go one at a time. Runtime evidence
comes from `SysProcessElementLog`, read with one `execute-esq` call at a time (a `SysProcessLog` root row with no
`CompleteDate` is parked, not hung). UI results are checked by the user. Cleanup goes through one
`execute-dataservice-batch` after the user's go-ahead.

### Day 0 (before code; done on 2026-10-02, CrtProcessBuilder 1.6.6.54)

| ID | Orig | What | Result | Reaches |
|---|---|---|---|---|
| TC-M-01 | M3 | does an ENG-96230 Collection process parameter type mirror bound root-only read null rows (H-1)? | refuted: 3 iterations, all values set (the outer Script mapping copies the whole value). No Sub-task; TC-U-53 is a parity commit | Runtime |
| TC-M-02 | MI-0 | an item-only `InputRecordCollection.Name <- RD1.ResultCompositeObjectList.Name` over 3 contacts | 1 iteration: the baseline for TC-M-13 | Runtime |
| TC-M-03 | M6 | a flat File mapped from a collection item outside a row context | read null while the source held two files: R-M1 stays a refusal (TC-U-40, TC-I-10) | Runtime |

### V0-V8 before the package PR leaves draft: V0 owed now (the code already exists), V1-V8 after the cut (TC-M-1n = Vn)

| ID | Orig | What | Method | Pass | Reaches | AC |
|---|---|---|---|---|---|---|
| TC-M-10 | V0 | owed before the package PR leaves draft (the code already exists): the shipped captures still describe what today's designer writes (read-only, no write) | in the classic designer, a NEW unsaved process: plain Add > Other > File; Add > Other > Collection of records, then the nested Add > Other > File; read direction, Tag, item name, item direction in memory; close unsaved | Variable and no Tag on a plain Add; the item as the user adds it. If not, adjust TC-U-11 / TC-U-13 expectations, and the code, before the package PR leaves draft | Design time | AC-8 |
| TC-M-11 | V1 | instrument check | build the clio PR; `install-process-builder` from the refreshed BUILD OUTPUT after the user's approval; `list-packages` shows the cut; `addParameter type: File` on a disposable process succeeds; `get-tool-contract` for create mentions `File` | all three; otherwise run nothing else | Stored | AC-10 |
| TC-M-12 | V2 | e2e | the new fixture, then the must-stay-green fixtures, sequentially | Passed with **Ignored = 0** | Stored | AC-1..AC-7, AC-10 |
| TC-M-13 | V3 | P1 at run time | the TC-M-02 process rebuilt on the cut; count callee instances | 3 iterations against 1 on the baseline; otherwise hold the clio and knowledge PRs | Runtime | AC-5 |
| TC-M-14 | V4 | serialization | SC-0 first (export a shipped schema and diff it with its shipped copy, so no read-path artefact is charged to the builder); then `create-business-process` `UsrFpSc4Pt` with `PFile` (File), `PFiles` (FileCollection, NO direction, so the run also exercises the Variable default, D3) and `PFilesOut` (FileCollection, `direction: Out` explicitly); read back by `pull-pkg` or `export-schema`, never describe; normalise and compare against the three captures | equal under the AC-8 comparison rule; one DIFF fails ("fix before merge"); the table recorded in `docs/file-parameter-capture.md` and the PR | Stored | AC-8 |
| TC-M-15 | V5 | the designer opens builder output | the user opens the TC-M-14 process, opens each parameter, closes; then a no-op save; diff `SysSchema.MetaData` before and after | no error; no diff; otherwise a parity defect: fix or record a Sub-task | Design time | AC-8 |
| TC-M-16 | V6 | guidance in force | after the knowledge release, `info-knowledge` shows the new libraryVersion before any guidance-dependent run | version matches; otherwise re-run `update-knowledge` | - | AC-10 |
| TC-M-17 | V7 (optional) | a file passed through a builder-written FileCollection is consumed | fixtures: a source contact with 2 attachments and a target contact; the user builds `OF1` (Object attachments, use in process) -> `PF1` (Process parameter source, save to the target's attachments); the builder adds `Docs` (FileCollection), maps `Docs <- OF1.ObjectFiles` (P2 pairs) and `PF1.Files <- Docs` (P2 rebinds the stale item, with a notice); describe with `mode=full`; run once | both levels of `PF1.Files` on `Docs` and of `Docs` on `OF1.ObjectFiles`; `OF1` and `PF1` Completed; 2 new attachment rows on the target, names equal to the sources | Runtime | AC-5, AC-6, AC-9 |
| TC-M-18 | V8 (only if M1 passed) | a single builder-declared File is consumed | callee `F` (File, In) with a designer-added Process file element bound on `Files.File` only; the builder adds a multi-instance `subProcess` and only `InputRecordCollection.F <- OF1.ObjectFiles.File`; run once | describe: `InputRecordCollection` bound to `OF1.ObjectFiles` (P1); 2 callee iterations; 2 new attachment rows | Runtime | AC-5, AC-9 |

If TC-M-17 fails while TC-M-13 passes, the binder writes the right metadata and the runtime does not read it the way
the source trace says: hold the clio and knowledge PRs and record the measured outcome before changing any rule.

---

## Cases that move with an open decision

| Trigger | Cases affected | If it goes the other way |
|---|---|---|
| Q20 (where the description bytes come from) | TC-U-63, TC-U-64, TC-U-66 | decided 2026-10-14: A. As written, TC-U-66 measures the trimmed text under the unchanged ceiling, and TC-U-63 / TC-U-64 keep the type lists in the descriptions. The rejected options would have moved: B, TC-U-66 asserting a reviewed new ceiling; C, the type lists leaving the descriptions, TC-U-63 / TC-U-64 rewritten and TC-U-71 pinning the type names in the guide |
| O-TP1 (where XT-14 runs) | XT-14a..d | recommended B: in ENG-96506's clio PR, outside this story. A: in this clio PR through the generic `userTask` route, as TC-I cases of this story; ENG-96506's clio PR then rewrites the arrange step, and the test builds an element that saves green unconfigured |
| M1, M2 (ENG-96506 measurements) | TC-U-39, XT-14c, TC-M-18 | the single-file path fails: TC-U-39 stays (metadata), the guide says "wrap one file in a FileCollection", TC-M-18 is dropped |
| A foreign package cut lands between ours | TC-M-11 | add a capability probe on a named field and run it against both archives |
| The ADR D5 open item (a single-instance sub-process's FileCollection-shaped `In` parameter; recorded 2026-10-14, a stand measurement first) | none as written | if the owner widens the file-consuming targets: new TC-U cases for P2 and R-M2 on that target, and a TC-I case beside TC-I-09 (inference) |
| The helper and guard owner (this ticket's package PR) | TC-U-50 and every case that uses the hand-built file elements | if moved to ENG-96505, this ticket hand-builds its elements with no guard (not recommended) and TC-U-50 moves unchanged |

Decided and reflected above: the FileCollection alias (Q10), the Variable default (Q11), P3 reset and the P2 / R-M2
scope (Q12), M3 (no mirror defect), M6 (R-M1 stays a refusal).

## Traceability

| AC | Package unit | clio unit | Knowledge unit | Integration | Manual |
|---|---|---|---|---|---|
| AC-1 types, aliases, Binary refusal | TC-U-01..04, 06, 47 | - | - | TC-I-01, 03, 04 | TC-M-11, 12 |
| AC-2 defaults and refusals | TC-U-01, 11, 12, 14..18 | - | - | TC-I-01, 05 | TC-M-12 |
| AC-3 describe read-back rebuilds | TC-U-05, 07..10, 52 | TC-U-61 | TC-U-72 | TC-I-01, 02 | TC-M-12 |
| AC-4 dotted process paths | TC-U-21..25 | - | - | TC-I-06 | TC-M-12 |
| AC-5 two-level binder and refusals | TC-U-26..46, 47, 52, 55 | - | TC-U-73 | TC-I-06..10, 13 (XT-14a..d in ENG-96506) | TC-M-13, 17, 18 |
| AC-6 mapped from element outputs; mirror refusals | TC-U-19, 20, 28 | - | - | TC-I-11 | TC-M-17 |
| AC-7 describe decode, nested-only listing | TC-U-48, 49, 51, 52, 54 (50 supports) | TC-U-62, 65 | - | TC-I-12 (XT-14c in ENG-96506) | TC-M-12 |
| AC-8 serialization equals the shipped captures | TC-U-13 (06 for the non-empty container) | - | - | - | TC-M-10, 14, 15 |
| AC-9 consumption by the Process variant (design time here) | TC-U-39 | - | - | (XT-14c in ENG-96506) | TC-M-17, 18 |
| AC-10 floor, descriptions, guide, e2e | - | TC-U-63..70 | TC-U-71..75 | all | TC-M-11, 12, 16 |
| D9 mirror binds items (no AC) | TC-U-53 | - | - | - | TC-M-01 |

## Follow-up stories

Stories 2-4 (the three follow-up Sub-tasks) get their own rows in this plan when their criteria are refined; each
story file carries a test sketch until then.

## Exit criteria

- Every TC-U and TC-I case of this ticket implemented and green (TC-I against a stand on the cut, Ignored = 0); the
  package suite, the clio full unit suite and the knowledge suite green. The cross-ticket cases XT-14a..d are not
  part of these exit criteria.
- TC-M-10..TC-M-16 executed and recorded with dates, package version and basis; TC-M-17 and TC-M-18 when run.
- No case reworded to pass: a difference outside the AC-8 comparison rule is fixed, or recorded with the owner as a new
  row of the rule.
