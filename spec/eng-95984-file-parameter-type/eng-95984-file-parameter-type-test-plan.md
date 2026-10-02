# ENG-95984 File process parameter type: test plan

[ENG-95984](https://creatio.atlassian.net/browse/ENG-95984) File process parameter type · Task · epic
[ENG-92704](https://creatio.atlassian.net/browse/ENG-92704) Create BP via AI Toolkit.

Written 2026-10-01. Read-only: nothing was built, run, committed or written to any repository or to the stand. Code
was read at crt-process-builder `main` `3f4cce50` (CrtProcessBuilder 1.6.6.54, the stand's version), clio `master`
`03ef3944f` and clio-knowledge `master` `d0b5a2b` (libraryVersion 1.15.90); platform claims in core 10.1.37.

**Summary.** This plan proves the ten acceptance criteria of the [implementation plan](eng-95984-file-parameter-type-plan.md)
(section 1.4, AC-1..AC-10) at five levels. Package unit tests in CrtProcessBuilder carry most of the weight: type
resolution (`File` is FileLocator `A33C9252`, never the BLOB type `BA40CFC5` the platform calls "File"), the
`FileCollection` shape and its describe read-back, the refusals, dotted process-parameter paths, the two-level
collection binder (P1, P2, P2-MI, P3, R-M1, R-M2) with its multi-instance regressions, and the describe decode.
clio unit tests pin the DTO pass-through, the tool-description wording, the floor and the ManagerMap arm. A new
`clio.mcp.e2e` fixture proves the same contract over the real MCP path, without the generic `userTask` route for the
Process file schemas. Knowledge tests pin the new `process-files` guide and the version-gated multi-instance
sentences. The stand closes what no unit test can reach: the platform parameter sync, the runtime reader, the
designer. **No Binary test flips**: Binary stays refused (decision D1), the ticket wording that implied otherwise is
corrected, and one probe moves from Binary to another unsupported name so it keeps testing what its name says. Every
case below names the AC it proves; the cases that depend on an open owner decision or an unmeasured runtime claim
are listed in section 10.

Read with: [implementation plan](eng-95984-file-parameter-type-plan.md) (work packages PB-1..PB-10, CL-1..CL-7,
KB-1..KB-3, stand rows W0/V1-V6, owner decisions O-1..O-10),
[decisions](../eng-92719-file-processing-element/eng-92719-file-processing-element-decisions.md) (D1-D9 are this
ticket's; D28 test strategy; D29 measurements M1-M26),
[traps](../eng-92719-file-processing-element/eng-92719-file-processing-element-traps.md),
[serialization capture](../eng-92719-file-processing-element/eng-92719-file-processing-element-serialization-capture.md),
[platform reference](../eng-92719-file-processing-element/eng-92719-file-processing-element-platform-reference.md),
[reuse](../eng-92719-file-processing-element/eng-92719-file-processing-element-reuse.md),
[PR split](../eng-92719-file-processing-element/eng-92719-file-processing-element-pr-split.md),
[open questions](../eng-92719-file-processing-element/eng-92719-file-processing-element-open-questions.md), and the
element's own [test plan](../eng-92719-file-processing-element/eng-92719-file-processing-element-test-plan.md)
(ENG-96505 Element readiness and object attachments mode, ENG-96506 Generated report + process parameter modes).

| Alias | Repository-relative path |
|---|---|
| `PB/` | crt-process-builder `packages/CrtProcessBuilder/Files/src/cs/` |
| `PBT/` | crt-process-builder `tests/UnitTests/CrtProcessBuilder.Tests/` |
| `CORE/` | Creatio core `TSBpm/Src/Lib/` (10.1.37) |
| `PD/` | PackageStore `CrtProcessDesigner/branches/7.8.0/Schemas/` (byte-identical to what the stand serves) |
| `PS/` | PackageStore (the shipped corpus) |
| clio / knowledge | relative to the clio and clio-knowledge repository roots |

Basis labels: **source** = read in code or metadata (a runtime claim with this label is a hypothesis until it is
measured); **measured** = observed on the stand or counted over the corpus, with the date.

---

## 1. Where the coverage lives

| Level | Project | What it can prove | What it cannot |
|---|---|---|---|
| **PU** package unit | crt-process-builder `PBT/` (NUnit 4, NSubstitute, FluentAssertions) | type resolution, the stored parameter shape, every refusal, the metadata each mapping writes (both levels, meta paths, `ModifiedInSchemaUId`), describe projection and decode, short-key serialization | the platform parameter sync on `SchemaUId` (`PBT/UserTaskElementHandlerCreateTests.cs:118-125` documents the wall); the runtime reader (`ProcessInstanceParametersDataReader` is `internal`); the designer |
| **CU** clio unit | `clio.tests` (`Module=ProcessModel`, `McpServer`, `Common`, `Command`) | the describe DTO keeps what the server sends and re-serializes it; description wording; floor literals; archive pins; the ManagerMap arm | anything the server does |
| **E2E** MCP end-to-end | `clio.mcp.e2e`, category `McpE2E.ProcessDesigner` | the JSON member names, the server's refusal envelope and notices over the real MCP path, against a stand on the cut (only this level sees a member the serializer silently drops) | runtime behaviour. TeamCity excludes this category (`clio.mcp.e2e/Support/Configuration/McpE2ECategories.cs:30-38` and clio `docs/knowledge/infra/mcp-e2e-processdesigner-category-is-consumed-only-by-teamcity-job-args.md`, source), so the manual run on the stand is the only e2e evidence |
| **KU** knowledge | clio-knowledge `automation/Clio.Knowledge.Bundle.Tests` | the guide states each rule; size budgets; registration | whether agents follow it |
| **V** stand | stand `Creatio` (core 10.1.37, .NET Framework) | the sync, the runtime reader, files really copied, the designer opening builder output | - |

Each stand case states the level it reaches: **Stored** (metadata written), **Design time** (the designer opens it)
or **Runtime** (the process ran and `SysProcessElementLog` shows it).

---

## 2. Conventions and commands

### 2.1 Enforced conventions

| Rule | Where it is enforced |
|---|---|
| Every new package fixture is `[TestFixture(Category = "UnitTests"), Category("PreCommit")]`; without it Jenkins skips the fixture and the stage stays green | `PBT/CiContractGuardTests.cs`; `PBT/ci.runsettings` (`TreatNoTestsAsError`); crt-process-builder `CLAUDE.md` "/tests" |
| Explicit `// Arrange`, `// Act`, `// Assert`; every assertion has `because:`; every test method has `[Description("...")]` | clio `AGENTS.md` "Test style policy"; applied to the package by convention |
| Package sources and tests are C# 7.3 (no switch expressions, no `is not`, no records) | package build |
| Tests never name `Terrasoft.Configuration` types (`ObjectFileProcessingUserTask` and the others): the dev-n8 build has no such DLL and demotes the missing reference to a message. Mirror the constants instead (schema UIds, parameter names, `ResultActionType` ints) | `PBT/CrtProcessBuilder.Tests.csproj:72-74`; `.build-props/env.dev-n8.props` (source; a portability convention, not a compiler wall) |
| clio unit tests carry `[Category("Unit")]` and the fixture's `[Property("Module", "<module>")]`; the fixtures this plan extends already do (`clio.tests/Command/McpServer/CreateBusinessProcessToolTests.cs:16-23`, `clio.tests/Command/ProcessModel/ManagerMapResolveDataIdTests.cs:12-13`), and that pair is what the `TestCategory=Unit&Module=...` filter selects | clio `AGENTS.md` "Smart regression testing policy" |
| clio command tests: no new command class changes in this ticket, so no `BaseCommandTests<T>` fixture is added | clio `AGENTS.md` |
| e2e: `[NonParallelizable]`, unique `UsrClioBpFile<Case>E2e{Guid:N}` names (the existing `UsrClioBp...E2e` convention; processes stay on the stand, cleanup is a separate batch with the user's go-ahead), no process RUN inside a fixture, arrange through `ProcessDesignerE2EArrange.StartAsync(subject, MinimumPackageVersion)` | pattern `clio.mcp.e2e/RecordColumnSourceToolE2ETests.cs:28-39` |

### 2.2 Commands

**Package** (Windows, net472 = what CI runs). The worktree lives on a short path, `C:/Projects/workspace/<short>`,
with `.application` copied in: a deep path exceeds MAX_PATH and every net472 test then dies in SetUp with a bogus
`FileNotFoundException`.

```bash
cd C:/Projects/workspace/<short>
# targeted, while iterating
dotnet test tests/UnitTests/CrtProcessBuilder.Tests/CrtProcessBuilder.Tests.csproj -c dev-nf \
  --filter "FullyQualifiedName~FileProcessParameterTests|FullyQualifiedName~FileCollectionMappingTests|FullyQualifiedName~ElementSourceDecodeTests|FullyQualifiedName~FileProcessingTemplateDriftTests|FullyQualifiedName~ProcessParameterServiceTests|FullyQualifiedName~ProcessDescriberTests|FullyQualifiedName~ParameterTypeCompatibilityTests|FullyQualifiedName~MultiInstanceMappingTests"
# before every push: the whole suite, because ApplyMapping is the shared funnel of the build, the mirror,
# EntityConnectionBinder and ApprovalApplier (PB/Mappings/ProcessMappingService.cs:48)
dotnet test tests/UnitTests/CrtProcessBuilder.Tests/CrtProcessBuilder.Tests.csproj -c dev-nf
# once per PR, when .application/net-core exists: proves no test names a Terrasoft.Configuration type
dotnet build MainSolution.slnx -c dev-n8
```

CI runs `--filter "TestCategory=PreCommit|TestCategory=Integration"` with `Configuration=dev-nf` exported
(crt-process-builder `docs/ci-cd.md:20, 62-73`).

**clio unit.** Filter by `TestCategory`, never by a `Category=` alias: NUnit3TestAdapter turns a non-TestCategory
filter that selects more than 2,000 tests into an empty filter and silently runs the whole assembly (measured
2026-09-25). Run from bash with `< /dev/null`: a git child process of the test host hangs on an inherited open
stdin. Check the executed count, not the exit code.

```bash
dotnet build clio.tests/clio.tests.csproj -c Debug
dotnet test clio.tests/clio.tests.csproj --no-build \
  --filter "TestCategory=Unit&(Module=ProcessModel|Module=McpServer|Module=Common|Module=Command)" < /dev/null
# the PR touches four modules, so the full unit suite is mandatory before commit (AGENTS.md rule 4)
dotnet test clio.tests/clio.tests.csproj --no-build --filter "TestCategory=Unit" < /dev/null
```

**e2e** (one fixture per run, sequentially; schema writes in parallel crash the stand's app pool):

```bash
McpE2E__Sandbox__EnvironmentName=Creatio dotnet test clio.mcp.e2e/clio.mcp.e2e.csproj -c Debug -f net10.0 \
  --filter "TestCategory=McpE2E.ProcessDesigner&FullyQualifiedName~FileParameterToolE2ETests" < /dev/null
```

Report **Passed with Ignored = 0**: on a package older than `MinimumPackageVersion` the fixture goes to
`Assert.Ignore`, which reads like a pass (`clio.mcp.e2e/Support/Mcp/ProcessDesignerE2EArrange.cs:73-93`, source).
The session's MCP clio must be the PR's clio (`get-tool-contract` for `create-business-process` mentions `File`): a
stale MCP clio silently drops unknown arguments. The `clio-mcp-e2e-live-stand` skill prepares the stand (registration,
prerequisites, the exit-code trap of arrange steps).

**Knowledge:** `dotnet test automation/Clio.Knowledge.Bundle.Tests/Clio.Knowledge.Bundle.Tests.csproj`, in the
repository's own worktree under `.worktrees/<task>/`.

---

## 3. Mocking recipe (package)

The approach of decisions D28: hand-built elements with the platform's real schema UIds and literal parameter
names, because the parameter sync cannot run in the harness. Full harness notes are in the
[reuse](../eng-92719-file-processing-element/eng-92719-file-processing-element-reuse.md) document.

1. **Data value types need no database.** `new DataValueTypeManager()` registers `CompositeObjectList`
   (`CORE/Terrasoft.Core/DataValueTypeManager.cs:485-492`) and FileLocator (`:501-508`), and also the BLOB type
   whose NAME is `"File"` (`BA40CFC5`, `:340-348`), so PU-01 can prove the alias never reaches that name; and
   `SubProcessTestSupport.AParameterOn(..., "FileLocator")` (`PBT/SubProcessTestSupport.cs:346`) and the test-side
   `DataValueType` enum (`PBT/BaseComposableAppTestFixture.cs:34-80`, lists `FileLocator` and `Binary`) work as they are.
   Assign `DataValueType` as an object so a later value write does not re-resolve through the null
   `AppManagerProvider` (`PBT/SendEmailApplierTests.cs:70-74`).
2. **The shared helper `PBT/FileProcessingTestSupport.cs`**, as specified in the element
   [test plan](../eng-92719-file-processing-element/eng-92719-file-processing-element-test-plan.md) (section 3.3): one
   internal static class, for the reason `SubProcessTestSupport` exists (the parameter set is a SET and must be right
   in every fixture). **Owner: this ticket's package PR (PK-PT)**, because its binder and describe tests are the
   first to build these elements and it merges first. PK-PT creates four things: `PBT/FileProcessingTestSupport.cs`
   (the literal template table of all three schemas, element test plan section 3.2: 13 / 8 / 13 parameters with
   names, UIds, types, directions, items; names are literals, never production constants; and the three builders
   with `SyncShape.Server`), `PBT/FileProcessingTemplateDriftTests.cs` (PU-50), the three captures under
   `PBT/Fixtures/FileProcessing/` and their `PROVENANCE.md`. ENG-96505 Element readiness and object attachments mode
   and ENG-96506 Generated report + process parameter modes inherit all four and add the rest
   (`RegisterFileTaskSchemas`, `ArrangePaletteRow`, `ApplyCorpusConfiguration`, `SyncShape.Designer`). The plan's
   PB-9 lists the four files and their 2.5-3.5 h (inference: the literal table of 34 parameters plus their items
   and the three builders, about 1.5-2 h; the guard with its ancestor-search reader, the captures and the SHA-256
   pins, about 1-1.5 h). The parameters this ticket's tests read:

   | Builder | Schema UId (real) | Parameters these tests read (name: type, direction, items) | Evidence |
   |---|---|---|---|
   | `AProcessFileElement` | `6c620dd2-026e-560c-489f-030c5be5f2c3` | `Files`: CompositeObjectList, **In**, required, item `File`: FileLocator, In; `ObjectFiles`: CompositeObjectList, Out, item `ObjectFile`: FileLocator, Out; `CreatedObjectFileIds`: CompositeObjectList, Out, item `Id`: Guid, Out | `PD/ProcessFileProcessingUserTask/metadata.json:16-35, 78-97, 101-121` (source) |
   | `AnObjectFileElement` | `9387c794-8d84-5925-ab77-c47e7d876286` | `ObjectFiles`: Out, item `File`: FileLocator, Out; `CreatedObjectFileIds`: Out, item `Id`: Guid, Out; `ResultActionType`: Integer, required | `PD/ObjectFileProcessingUserTask/metadata.json:70-89, 111-140` (source) |
   | `AReportFileElement` | `c2bf0416-54c6-6c56-58e0-41162c7795f0` | `ReportFiles`: Out, item `File`: FileLocator, Out; `CreatedObjectFileIds`: Out, item `Id`: Guid, Out; `ResultActionType`: Integer, required | `PD/ReportFileProcessingUserTask/metadata.json:46-51, 65-84, 116-133` (source) |

   **Build order (the SchemaUId rule of the element test plan, section 3.1; basis=source).** Assigning `SchemaUId`
   runs the platform's parameter sync (`CORE/Terrasoft.Core/Process/ProcessSchemaUserTask.cs:105-115`), and the sync
   returns at once while the HOST's UId is empty, one of its three early exits (`CORE/Terrasoft.Core/Process/ProcessSchemaActivity.cs:324-326,
   587-590`). So: (1) take a host with an empty UId (`new ProcessSchema(UserConnection.ProcessSchemaManager)`, as
   `PBT/SendEmailApplierTests.cs:48` does, or a `TestProcessSchema` (`PBT/ProcessDesignTestSupport.cs:22`) with
   `UseDataValueTypeManager` and no UId);
   (2) build each element as `new ProcessSchemaUserTask(host) { UId = Guid.NewGuid(), Name = name, Caption = name,
   SchemaUId = <real UId> }`, add its parameters and items by hand (`ItemProperties.Add`) with
   `ContainerUId = element.UId`, and add it to `host.FlowElements`; (3) THEN set `host.UId = Guid.NewGuid()`;
   (4) only then add the process-level parameters through the service (so their `ContainerUId` is the real host UId
   PU-06 asserts) and the `SubProcessTestSupport` elements (that helper never assigns `SchemaUId`,
   `PBT/SubProcessTestSupportTests.cs:267`), and run the binder. Step (3) matters for every assertion of
   `ModifiedInSchemaUId == host.UId` in section 4.5: against an empty host UId it would pass vacuously. Note that
   `SubProcessTestSupport.AHostSchema` sets a UId at construction (`PBT/SubProcessTestSupport.cs:107-112`), so it is
   not the host to build file elements on. An item parameter without `ContainerUId = element.UId` loses the
   `[Element:{...}]` segment of its meta path, and the runtime then looks for it among the process parameters
   (pinned at `PBT/MultiInstanceMappingTests.cs:323-330`).
3. **The schema stub, only where describe or a handler reads the task's name.** Substitute `ProcessUserTaskSchema`
   with the real UId on `UserConnection.SetupProcessUserTaskSchemaManager()` and wire `FindInstanceByName`,
   `FindInstanceByUId`, `FindItemByUId` (describe reads `FindItemByUId(...)?.Name`; pattern
   `PBT/DeleteDataConfigBinderTests.cs:37-70`, `PBT/AddDataConfigBinderTests.cs:79-86`). Arrange in `[SetUp]`: the
   connection is rebuilt per test.
4. **Process-level File and FileCollection** are built through the service under test
   (`ProcessParameterService.AddProcessParameter`), never by hand, except in the cases that reproduce a
   designer-made shape (item `FileCollectionParameter`, `PS/ProcessTests/branches/7.8.0/Schemas/FileParameterProcess/metadata.json:24-52`).
5. **Serialization** goes through the platform writer: `JsonDataWriter` with one `Read()` and no `ReadInto()`
   (`PBT/ProcessParameterServiceItemPropertiesTests.cs:60-85`), assertions on short keys
   (`PBT/MultiInstanceModeFieldTests.cs:64-91`). Keys: `L1` type, `L8` source value (`GS1` source, `GS2` value,
   `GS5` ModifiedInSchemaUId), `L9` reference schema, `L12` direction (omitted for Variable), `L17` Tag, `L18` items
   (`CORE/Terrasoft.Core/Process/ProcessSchemaParameter.cs:123-142`).
6. **Do not try** in a unit test: the `SchemaUId` sync, `ProcessSchemaManager` save, the platform's pre-save
   validation (`IProcessSchemaValidator` is substituted, `PBT/ProcessDesignerRoundTripTests.cs:94-97`), running a
   user task. Those are stand cases.

**Drift guard (PU-50 = TC-01 and TC-02 of the element test plan).** Without it the helper is a second, unverified
copy of the platform contract. It is the design of the element test plan, section 3.4, unchanged: the three
`PD/<Task>/metadata.json` files copied verbatim to `PBT/Fixtures/FileProcessing/<Task>.metadata.json` with a
`PROVENANCE.md` (source path, CrtProcessDesigner 7.8.0, SHA-256 per file), read by ancestor search as
`PBT/PackageDescriptorTests.cs:59-70` reads `descriptor.json` (no csproj change), deserialised with the platform's
`ProcessSchemaParameter.ReadMetaData` (one `Read()`, no `ReadInto()`), a precondition count of 13 / 8 / 13
parameters, then strict equality of the helper's template with the capture, and a check that assigning `SchemaUId`
left the helper's set unchanged. The guard ships in the same PR as the helper it guards, PK-PT, because that PR is
the helper's first user: a helper without its guard is the unverified copy this paragraph exists to prevent. The
element test plan's TC-01 and TC-02 are these cases, added in PK-PT and inherited by PK-OA.

---

## 4. Package unit cases (PU)

New fixtures: `PBT/FileProcessParameterTests.cs` (D1, D2, D3, D6, D7), `PBT/FileCollectionMappingTests.cs` (D4,
D5), `PBT/ElementSourceDecodeTests.cs` (D8 decode), `PBT/FileProcessingTemplateDriftTests.cs` (drift guard, with the captures and `PROVENANCE.md` under
`PBT/Fixtures/FileProcessing/`), and the non-fixture helper `PBT/FileProcessingTestSupport.cs` (section 3 item 2;
all four are PK-PT's, plan PB-9). Extended:
`ProcessParameterServiceTests`, `ParameterTypeCompatibilityTests`, `ProcessDescriberTests`,
`MultiInstanceMappingTests`. All inherit `BaseComposableAppTestFixture` where they need a `UserConnection`. The
plan's PB-9 names the existing fixtures as the home of these cases; new files are preferred because
`PBT/ProcessParameterServiceTests.cs` is already 2,134 lines and `PBT/ProcessMappingServiceTests.cs` 805 (measured
2026-10-01), and either placement satisfies PB-9 as long as every fixture carries the CI categories of section 2.1.
Refusal and notice IDs (F-T1..F-T7, F-M1..F-M4) refer to the catalogue in the decisions' Appendix A; a test asserts
the catalogue's key words (the names it must mention), not the full sentence, so wording can be tuned in review.

A shape of a case, to fix the conventions (C# 7.3):

```csharp
[TestCase("File")]
[TestCase("file")]
[TestCase("FileLocator")]
[TestCase("filelocator")]
[TestCase("file locator")]
[Description("D1: every friendly file spelling creates a FileLocator parameter, and never the BLOB type the platform names 'File'.")]
public void AddProcessParameter_ShouldCreateFileLocator_WhenTypeIsAFileAlias(string typeName) {
	// Arrange
	ProcessSchema schema = CreateSchema();

	// Act
	CreateService().AddProcessParameter(schema, new ProcessParameterDescriptor { Name = "Doc", Type = typeName });

	// Assert
	Guid created = schema.Parameters.Single(p => p.Name == "Doc").DataValueTypeUId;
	created.Should().Be(FileLocatorUId, because: "a process parameter carries a file by reference (IFileLocator)");
	created.Should().NotBe(BlobFileUId,
		because: "GetInstanceByName(\"File\") is the BLOB type BA40CFC5, which no designer page offers and the flow engine cannot store");
}
```

### 4.1 Types and aliases (D1)

| ID | Arrange / Act | Assert | AC |
|---|---|---|---|
| PU-01 | `addParameter` with `File`, `file`, `FileLocator`, `filelocator`, `file locator` (TestCases; the AC-1 alias list) | type UId = `A33C9252-D401-453E-949D-169157067ED9`; NOT `BA40CFC5-F554-4C26-8F57-1BB29CF43C4E`; direction Variable; no Tag; no reference schema | AC-1, AC-2 |
| PU-02 | `addParameter` with `Binary`, `binary`, `Blob` (F-T1) | `ArgumentException` whose message contains `not supported`, the given name, `File` and `FileCollection`; schema has no new parameter. Keeps the two substrings the e2e pins (`not supported`, `Binary`) | AC-1 |
| PU-03 | existing `AddProcessParameter_ShouldThrow_WhenTypeNotSupported` (`PBT/ProcessParameterServiceTests.cs:144-150`, Binary) | **unchanged, stays green** | AC-1 |
| PU-04 | existing `AddProcessParameter_ShouldNameCollection_InUnsupportedTypeMessage` (`:1671-1678`) | the probe type moves from `Binary` to `Image` (or `Color`), with a PRECONDITION assertion that the harness's `DataValueTypeManager` resolves that name. The generic message now also lists `File, FileCollection`; assert `*Collection*` and `*File*`. Without the move the probe would pass only because the new Binary-specific refusal names `FileCollection`, which contains "Collection" (decisions X7 and D1; open questions, the Binary-tests row) | AC-1 |
| PU-05 | describe a File parameter, feed the reported type back into `addParameter` | reported type `FileLocator` (`ProcessParameterService.ResolveDataValueTypeName`); the rebuilt parameter has the same type UId | AC-3 |

### 4.2 FileCollection shape and read-back (D2, D3)

| ID | Arrange / Act | Assert | AC |
|---|---|---|---|
| PU-06 | `addParameter` with `FileCollection`, `file collection`, `collection of files`, on a schema given `UId = Guid.NewGuid()` first (`CreateSchema()`, `PBT/ProcessParameterServiceTests.cs:55-59`, sets no UId, and against an empty UId the `ContainerUId` assertion passes vacuously) | type `CompositeObjectList` `651EC16F`; exactly one item: `Name` `File`, `Caption` `File`, FileLocator, direction Variable, Tag null, `ContainerUId == schema.UId` (non-empty), source None, fresh UId (not the root's); the item's `GetMetaPath()` contains `[Parameter:{<item UId>}]` and no `[Element:` segment (`CORE/Terrasoft.Core/Process/ProcessSchemaParameter.cs:1097-1110`) | AC-1 |
| PU-07 | `ToDescribeParameter` of a process-level `CompositeObjectList` with no Tag and one FileLocator item | `type: "FileCollection"`, `itemProperties` = `[File: FileLocator]` | AC-3 |
| PU-08 | the same projection for (a) a collection with a Tag (a mirror), (b) two items, (c) one non-file item, (d) an ELEMENT parameter of the same shape (the Process variant's `Files`) | `type: "CompositeObjectList"` in all four: the predicate is process-level only, so nobody re-declares element parameters | AC-3 |
| PU-09 | build -> describe -> rebuild from the described `name`, `type`, `direction` (model: `CollectionParameter_ShouldRoundTrip_BuildDescribeBuild`, `PBT/ProcessParameterServiceTests.cs:2089`) | same item name, type and direction; no Tag; new UIds | AC-3 |
| PU-10 | a designer-made FileCollection with item `FileCollectionParameter` (hand-built from the `FileParameterProcess` shape) | described as `FileCollection`, its real item name in `itemProperties`; a rebuild names the item `File`; a replayed mapping onto `Docs.FileCollectionParameter` is refused naming the available item `File` (catalogue F-T7) | AC-3 |
| PU-11 | defaults: `FileCollection` with no direction; with `direction: In`; item direction | root Out (owner decision O-2; the expected value is one constant in the fixture so a different decision is a one-line change); explicit In wins; item Variable | AC-2 |
| PU-12 | `setParameter direction: In` on a FileCollection | allowed (the remedy for an Out callee input, D3) | AC-2 |
| PU-13 | serialization of File, Out FileCollection, Variable FileCollection through `JsonDataWriter`, on a schema given `UId = Guid.NewGuid()` before the parameters are added (as PU-06), so that `IL2` is written as it is on the stand | File: `L1 = a33c9252-...`, no `L12`, no `L17`, no `L9`, `L8` with no `GS1` / `GS2` (no source, no value), **`IL2` = the schema UId** (N9 below: a pin of what the builder writes, not a parity failure). Out FileCollection: `L1 = 651ec16f-...`, `L12 = 1`, `IL2` = the schema UId, one `L18` entry `A2 = File`, `L1 = a33c9252-...`, `IL2` = the schema UId, no `L12`, no `L17` on the item; then equal to `PS/ProcessLibrary/branches/7.8.0/Schemas/MarkProcessesToCancel/metadata.json:74-93` under the AC-8 comparison rule below (PU-13 column). Variable FileCollection: no `L12`, then equal to `FileParameterProcess.FileCollection` (`PS/ProcessTests/.../FileParameterProcess/metadata.json:24-52`) under the same rule, whose row PT-b removes that capture's item name and bindings | AC-8 |

**The AC-8 comparison rule (one finite list, used by PU-13 and V4).** "Equal apart from the UIds" cannot pass as
written: the builder writes `IL2` where the designer writes nothing, and the Variable oracle is a bound collection
with a different item name. The rule is therefore this list, and nothing else. A difference that is not in the list
is a DIFF, and one DIFF fails the comparison. N-numbers are those of
[serialization capture](../eng-92719-file-processing-element/eng-92719-file-processing-element-serialization-capture.md)
section 10; PT-a and PT-b are this ticket's.

| # | Key or row | Designer capture | Builder | Rule | PU-13 | V4 |
|---|---|---|---|---|---|---|
| N5 | parameter and item `UId` | fresh | fresh | ignored: parameters paired by `A2`, items by `A2` under their parent | yes | yes |
| PT-a | schema UIds inside `A3`, `A4`, `GS5` and `IL2` | `<P>` | `<P>` | role-replaced (`<P>`) as in serialization capture section 12.8 step 3. A key present on one side only is a DIFF unless N9 or N15 covers it, so an `A3` / `A4` the stand's save does not fill on the builder's side fails V4 | not compared: the harness writes no `A3` / `A4` (written only when non-empty, `CORE/Terrasoft.Core/MetaItem.cs:261-262`, and the harness sets neither; whether the stand's save does is what V4 sees) and no `GS5` (written only when `ModifiedInSchemaUId` is set, and it defaults to empty: `CORE/Terrasoft.Core/Process/ProcessSchemaParameterValue.cs:258, 467`) | yes |
| N9 | `IL2` on a process parameter and on its items | absent: none of the three captures carries it (read 2026-10-01); serialization capture section 8 counts 0 of 3,335 process parameters and 0 of 678 items over the 7.8.0 corpus | `<P>`: `ContainerUId = schema.UId` (`PB/Parameters/ProcessParameterService.cs:65`; cloned items `:478`), written whenever non-empty (`CORE/Terrasoft.Core/Process/BaseProcessSchemaElement.cs:217`, key `IL2` at `:38`) | normalised: absent equals `<P>`. The meta path is the same either way, and ENG-96230 Collection process parameter type collections already ship it; V5 checks that the designer opens it (M22) | pinned (present) | normalised |
| N15 | "no value" `L8` forms: `{}`, `{"GS5": <P>}`, no `L8` | `{"GS5": <P>}` on every unbound parameter and item of the three captures | no `GS1` / `GS2` | equal | yes | yes |
| N14 | resource culture | authoring culture | request culture | compare the authoring culture's `Caption` rows only | - | yes |
| PT-b | `FileParameterProcess.FileCollection` only (the Variable oracle, `B8` 7.17.3.396): the item's `A2` and Caption row, both levels' `L8`, and the `DisplayValue` resource rows | item `FileCollectionParameter`; both levels bound (`GS1` 3, element tokens in `GS2`); rows `Parameters.FileCollection.DisplayValue` and `Parameters.FileCollection.FileCollectionParameter.DisplayValue` (resource file `:96, :98`) | item `File` (D2); both levels unbound; no `DisplayValue` row | excluded. The item is still compared on count (1), `L1`, absent `L12` and absent `L17`; its name `File` is asserted on its own (PU-06). PT-b falls away when SC-1's `PFiles` is the oracle (V4) | yes | yes |

Not in the list on purpose: the element exceptions of serialization capture section 10 (N1-N4, N6-N8, N10-N13,
N16) and the missing `TargetDataEntitySchemaUId` of the 7-parameter Process-variant captures (G-C2). PU-13 and V4
compare `FJ1` process parameters and their resource rows only and build no element, so none of them can occur, and
an element key in the diff is a DIFF. Those rows belong to the element test plan's TC-41 rule (TC-41, TC-59,
TC-69, DT-02, DT-04), not to this ticket; section 10 names where else this ticket's rule is stated.

### 4.3 Refusals (D2, D6, D7)

Every refusal case also asserts that the schema is unchanged (no parameter added, no value written).

| ID | Arrange / Act | Assert | AC |
|---|---|---|---|
| PU-14 | `referenceSchema: "Contact"` with `File` and with `FileCollection` (catalogue F-T2) | refused, message names `referenceSchema`; without the refusal the lookup branch (`PB/Parameters/ProcessParameterService.cs:93-100`) silently turns File into a Lookup | AC-2 |
| PU-15 | `value` on File through `addParameter` and through `setParameter` (F-T3) | refused, "no constant form"; today it is stored as a text ConstValue (`PB/Parameters/ProcessParameterValueValidator.cs:190-215`) | AC-2 |
| PU-16 | `addMapping value` onto a FileLocator process parameter, onto a FileCollection, onto `Files` and onto `Files.File` of a hand-built Process variant | refused in `ValidateConstantValue` (`ProcessParameterValueValidator.cs:133-221`), the one place every constant route passes | AC-2 |
| PU-17 | `removeParameter Docs` while (a) `PF1.Files.File` alone references `Docs.File`, (b) a formula body references `Docs.File` | refused, the usage named; today only the root UId is tested (`ProcessParameterService.cs:576-647`) | AC-2 |
| PU-18 | `setParameter type: FileCollection` on (a) a Read data mirror collection, (b) a bare collection; control: on an existing FileCollection, and `type: File` on a File | (a), (b) refused (F-T4: "would change this collection's shape"); controls are no-ops that succeed | AC-2 |
| PU-19 | mirror of a file output: `typeFromElement: OF1`, `typeFromElementParameter: ObjectFiles` (items have no Tag) | still refused, the message now says "declare type: FileCollection and map it from OF1.ObjectFiles" (F-T6, `CloneItemProperties` `:456`) | AC-6 |
| PU-20 | dotted mirror: `typeFromElementParameter: "ObjectFiles.File"` on add, and on the setParameter re-mirror path | refused naming the item and its collection (F-T5); **regression**: on 1.6.6.54 this silently created an unbound FileLocator parameter (`ResolveTypeSourceParameter` `:402`); the test's `[Description]` says so | AC-6 |

### 4.4 Dotted process-parameter paths (D4)

| ID | Arrange / Act | Assert | AC |
|---|---|---|---|
| PU-21 | `addMapping` with `processParameter: "Docs.File"` as a source | the stored formula addresses the item: `[Parameter:{<Docs.File UId>}]`, no `[Element:` segment; type-checked against the target | AC-4 |
| PU-22 | `targetProcessParameter: "Docs.File"` from `OF1.ObjectFiles.File` | the item is written; type-checked; the item keeps `ContainerUId = schema.UId` (no backfill) | AC-4 |
| PU-23 | `Docs.Nope` and `Nope.File` | refused, each naming the failing segment; `Docs.Nope` lists `File` as the available item | AC-4 |
| PU-24 | a process parameter literally named `A.B` | resolves flat first, as `ResolveElementParameter_ShouldPreferTheFlatName_WhenItItselfContainsADot` does for element parameters | AC-4 |
| PU-25 | the existing flat process-parameter mapping tests in `ProcessMappingServiceTests` | unchanged and green: the refactor of both `ResolveProcessParameter` callers (`PB/Mappings/ProcessMappingService.cs:221, 280, 425`) is behaviour-neutral for flat names | AC-4 |

### 4.5 The two-level binder (D5)

Elements are Strategy A elements; "PF1" is a hand-built Process variant, "OF1" an Object variant, "RP1" a Report
variant, "RD" a Read data element in collection mode, "SP1" a multi-instance sub-process. Every case asserts both
levels' `SourceValue` (source, formula, `ModifiedInSchemaUId == host.UId`) and, where it says "notice", the exact
notice list the build or modify result returns (`Warnings`, `PB/Contracts/BuildContracts.cs:115`,
`PB/Contracts/ModifyContracts.cs:583`).

| ID | Rule | Arrange / Act | Assert | AC |
|---|---|---|---|---|
| PU-26 | P1 | `PF1.Files.File <- processParameter Docs.File` | item bound; `PF1.Files <- Docs` written too | AC-5 |
| PU-27 | P1 | `PF1.Files.File <- OF1.ObjectFiles.File` | `PF1.Files <- OF1.ObjectFiles` | AC-5 |
| PU-28 | P1 | `targetProcessParameter Docs.File <- OF1.ObjectFiles.File`, `<- RP1.ReportFiles.File`; OF1/RP1 `ResultActionType` = 0 and 1 (TestCases) | `Docs <- OF1.ObjectFiles` / `<- RP1.ReportFiles`, identical for both action values (the builder does not read the action) | AC-5, AC-6 |
| PU-29 | P1 | `SP1.InputRecordCollection.F <- Docs.File`; and the non-file `SP1.InputRecordCollection.Name <- RD.ResultCompositeObjectList.Name` | parent bound in both: P1 is type-agnostic. This is visible behaviour change (1) of the plan | AC-5 |
| PU-30 | P1 refuse | `PF1.Files` already bound to `Other`; then `PF1.Files.File <- Docs.File` | refused (F-M1), names `Files`, `Other` and `Docs`; both levels keep their old values. Control: same source again = no-op, no error | AC-5 |
| PU-31 | P2 name | `PF1.Files <- Docs` | `Files.File <- Docs.File`; notice names `Files.File` | AC-5 |
| PU-32 | P2 single | `PF2.Files <- PF1.ObjectFiles` (item `ObjectFile`) | `Files.File <- ObjectFiles.ObjectFile` by the single-FileLocator fallback; notice | AC-5 |
| PU-33 | P2 never overwrite / stale | (a) `Files.File` already bound to `Docs.File`, then `Files <- Docs`; (b) `Files.File` bound into `OF1.ObjectFiles`, then `Files <- Docs` | (a) item untouched, no rebind notice; (b) item rebound to `Docs.File`, notice says it was stale | AC-5 |
| PU-34 | P2 unpaired | target FileLocator item cannot be paired (source has two FileLocator items, neither named `File`) | outer written; notice "at run time it will be empty"; no item written | AC-5 |
| PU-35 | P2 scope | `Docs2 <- Docs` (both process-level FileCollections) | `Docs2.File <- Docs.File` (a FileCollection is a file-consuming target) | AC-5 |
| PU-36 | P2-MI | `SP1.InputRecordCollection <- RD.ResultCompositeObjectList`, callee has `Id` (Guid) and `F` (File); also over `OF1.CreatedObjectFileIds` | **accepted**; notice names `InputRecordCollection.F` ("empty on every iteration unless you map ..."); no item written. D5 regression (1) | AC-5 |
| PU-37 | regression | the same multi-instance element with explicit outer + per-item mappings | exactly the values 1.6.6.54 writes (fixture literal), nothing extra. D5 regression (2) | AC-5 |
| PU-38 | P3 | single-instance `PF1`: `Files <- Docs`, then `Files.File <- Doc` (a File) | item <- `Doc`; `Files` reset to None; notice "it was cleared because Files.File now takes a single file" (owner decision O-3) | AC-5 |
| PU-39 | nested-only | fresh `PF1`: `Files.File <- Doc` | item bound; `Files` stays Source None (the shipped single-file shape). Metadata only: whether the runtime copies one file is M1, an ENG-96506 Generated report + process parameter modes measurement | AC-5, AC-9 |
| PU-40 | R-M1 | `targetProcessParameter Doc <- OF1.ObjectFiles.File`; `Doc <- Docs.File` | refused (F-M2), names FileCollection and multi-instance as the alternatives. Pending M6 (section 10) | AC-5 |
| PU-41 | R-M2 | `PF1.Files <- RD.ResultCompositeObjectList`; `Docs <- RD.ResultCompositeObjectList`; `Docs <- OF1.CreatedObjectFileIds` | refused (F-M3). Today `ParameterTypeCompatibility` accepts them (`PB/Mappings/ParameterTypeCompatibility.cs:240-242`: item shapes are not compared) | AC-5 |
| PU-42 | R-M2 scope | `RD.ResultCompositeObjectList` onto (a) `SP1.InputRecordCollection` whose callee has no File, (b) another user task's collection | accepted as on 1.6.6.54 | AC-5 |
| PU-43 | expression | `expression` onto `PF1.Files`, then onto `PF1.Files.File` (two describe-style formulas) | both written verbatim; no P1, no P3, no refusal; the other level untouched. D5 regression (3) | AC-5 |
| PU-44 | ordering | a two-level write whose second target does not resolve | nothing written: every target is resolved and type-checked before the first `SourceValue` write | AC-5 |
| PU-45 | `BindCollection` | explicit pairs; null pairs = the P2 policy | pairs written as given; null pairs behave as PU-31/PU-32 | AC-5 |
| PU-46 | dotted source (extend) | `ApplyMapping_ShouldResolveADottedSource_WhenTheValueComesFromAnotherCollection` (`PBT/MultiInstanceMappingTests.cs:294`) | add the P1 assertion: `InputRecordCollection` now bound to `ReadOrders.ResultCompositeObjectList` | AC-5 |

### 4.6 Type compatibility (no code change; pins only)

| ID | Arrange / Act | Assert | AC |
|---|---|---|---|
| PU-47 | TestCases beside `AreCompatible_ShouldRequireExactMatch_WhenTypeIsUnknown` (`PBT/ParameterTypeCompatibilityTests.cs:138`), through both entry points the mapping path uses: `ParameterTypeCompatibility.AreCompatible(target, source, ...)` (`PB/Mappings/ParameterTypeCompatibility.cs:90`) and `AreCompatibleByStoredReference` (`:252`) | FileLocator -> FileLocator true; Binary -> FileLocator false; File (BLOB) -> FileLocator false; FileLocator -> Text false; `CompositeObjectList` -> `CompositeObjectList` true (already `:253`) | AC-1, AC-5 |

### 4.7 Describe (D8)

| ID | Arrange / Act | Assert | AC |
|---|---|---|---|
| PU-48 | `ProcessDescriberTests`: `PF1.Files` unbound, `Files.File` bound in this schema | `Files` is listed (today the root-only provenance filter hides it, `PB/Describe/ProcessDescriber.cs:187-193`); its item carries the value. Controls: nothing bound = not listed; a nested value stamped by another schema = not listed; a sub-process element still lists every parameter, as today (`PB/Describe/ProcessDescriber.cs:176-184`) | AC-7 |
| PU-49 | `ElementSourceDecodeTests`: a stored Script that is exactly one element-parameter token, (a) top-level `[#[Element:{OF1}].[Parameter:{ObjectFiles}]#]`, (b) a nested item `...ObjectFiles.File` | `sourceElement` = `OF1`; `sourceElementParameter` = `ObjectFiles` / `ObjectFiles.File` (named through `ProcessSchemaElementLocator.TryNameNestedParameter`, `PB/ProcessSchemaElementLocator.cs:285`); `sourceColumn` null; `value` unchanged | AC-7 |
| PU-51 | existing record-column decode tests (`RecordColumnSourceTests`) | unchanged and green: the three-segment decode keeps its rules. Audit before the PR: `grep -n "SourceElement.Should().BeNull" PBT/` and re-read every hit, because a two-segment value that used to report no trio now reports one | AC-7 |
| PU-52 | round trip: build PF1/OF1/SP1/Docs with every binding kind of 4.5 -> describe -> replay the described parameters as mappings (structured where decoded, `expression` otherwise) on a fresh copy | both levels of every collection binding are identical to the original. D5 regression (4) | AC-3, AC-5, AC-7 |
| PU-54 | `ElementSourceDecodeTests`: the PU-49 decoder, negative | NO trio for: a multi-token formula; a process-parameter source (decoded only by the proposed Sub-task "Describe: decode process-parameter sources into re-appliable names"); a stale element UId; a dot-ambiguous nested name that the write side would resolve flat-first to a sibling (pattern `PBT/ChangeDataConfigBinderTests.cs:685-700`); a short-form or re-cased prefix (pattern `PBT/RecordColumnSourceTests.cs:415-445`) | AC-7 |

### 4.8 Mirror item binding (D9) and the drift guard

| ID | Arrange / Act | Assert | AC |
|---|---|---|---|
| PU-50 | `FileProcessingTemplateDriftTests`: the drift guard of section 3 (element test plan TC-01, TC-02), added in PK-PT and inherited by PK-OA | precondition 13 / 8 / 13 parameters read from the committed captures; the helper template equals the capture on name, UId, type UId, effective direction, IsRequired, items; each capture's SHA-256 equals `PROVENANCE.md`; the set is unchanged after `SchemaUId` was assigned | supports AC-5, AC-7 |
| PU-53 | extend `AddProcessParameter_ShouldBindMirroredCollection_ToSourceOutput` (`PBT/ProcessParameterServiceTests.cs:1760`) | the mirror binds every item to the source item of the same name, not the root only; correct the premise in the `[Description]` at `:1703` ("keys by SOURCE names", comment K3). Lives in PK-PT: M3 refuted the defect, so this is the D9 parity commit (contingency X4) | D9 (no AC; visible change (5)) |

`PBT/PackageDescriptorTests.cs` must stay green after the descriptor restamp (the last commit of the PR).

---

## 5. clio unit cases (CU)

| ID | Fixture (path) | Arrange / Act | Assert | AC |
|---|---|---|---|---|
| CU-01 | `clio.tests/Command/ProcessModel/ServerProcessDescriberTests.cs` (beside `Describe_ShouldReadParameterTagAndItemProperties_WhenServerReportsThem`, `:450`) | server JSON with a `FileLocator` parameter and a `FileCollection` parameter (direction Out, `itemProperties: [{name: File, type: FileLocator, direction: Variable}]`, no tag) | `DescribedParameter.Type` keeps `FileLocator` and `FileCollection` verbatim (a string, no enum mapping); items kept; re-serialized through `DescribeProcessCommand.OutputOptions` the types survive and `tag` is OMITTED, not null | AC-3 |
| CU-02 | same | element parameter `Files` with `sourceElement: OF1`, `sourceElementParameter: ObjectFiles`, no `sourceColumn`; its item with `sourceElementParameter: "ObjectFiles.File"` | the pair is kept on both levels; `sourceColumn` is null and omitted on output. Update the XML doc of `DescribedParameter.SourceElement` (`clio/Command/ProcessModel/IProcessDescriber.cs:1966-1982`, "Null for every other value" stops being true) | AC-7 |
| CU-03 | `clio.tests/Command/McpServer/CreateBusinessProcessToolTests.cs` (existing fixture, extended; pattern `CreateBusinessProcessTool_ShouldStateTheRecordFilterConsequence_InTheWideningDirection`, `:329`) | read the `[Description]` of `create-business-process` | the `parameters[]` type clause names `File` and `FileCollection` (`clio/Command/McpServer/Tools/ProcessDesigner/CreateBusinessProcessTool.cs:287`), and the constant clause says refused "on a Collection or File". No unit test pins the type list today (measured, grep over `clio.tests`), so a later trim would drop the types unseen | AC-10 |
| CU-04 | `clio.tests/Command/McpServer/ModifyBusinessProcessToolTests.cs` (existing fixture, extended; pattern `:436`) | read the `modify-business-process` description | the `addParameter` type list names `File` and `FileCollection` (`ModifyBusinessProcessTool.cs:140`); the constant clause as CU-03 | AC-10 |
| CU-05 | `clio.tests/Command/McpServer/DescribeProcessToolTests.cs` (existing fixture, extended; pattern `DescribeProcess_ShouldDocumentMultiInstanceOptions_WhenToolContractIsRead`, `:238`) | read the describe description (`DescribeProcessTool.cs:50`) | the decoded-source clause covers "a value that IS another element's output", not only one column of a record | AC-7, AC-10 |
| CU-06 | `clio.tests/Command/McpServer/ToolContractPayloadBudgetTests.cs` | re-measure create, modify, describe | under the 35,072-byte ceiling (`:142`, 137 x 256, unchanged); the figures in the comment at `:136-142` rewritten. If the S1/C6/S2 swap lands in this PR, lower the constant temporarily to measure the real size, then restore it | AC-10 |
| CU-07 | `clio.tests/Common/BundledProcessBuilderPackageTests.cs` | the rebundle to this ticket's final cut | the four pins (`ExpectedArchiveVersion` `:318`, `ExpectedArchiveSha256` `:289` asserted at `:756`, `ExpectedDescriptorModifiedOnUtc` `:356`, `ExpectedProducingCommit`); `EnforcedFloorSentences_ShouldEqualTheRequiresPackageLiteral` (`:1504`) green with the swapped clause (each of the three descriptions still contains `this clio requires <floor>`); `FloorSentences_ShouldNotCreditTheEnforcedFloorWithTheCollapse` (`:1587-1625`) green: "stopped validating formulas" is present in the create and modify descriptions and in `docs/McpCapabilityMap.md`, with no enforced floor literal in the 60 characters before it; `ToolContractVersionLiterals_ShouldNotExceedTheBundledArchiveVersion` (`:1335`); `CapabilityMapVersionLiterals_ShouldNotExceedTheBundledArchiveVersion` (`:1565`); `BundledArchive_ShouldExposeNoUnexpectedOperation` with unchanged counts (no new WCF operation) | AC-10 |
| CU-08 | `clio.tests/Command/ProcessDesignerRequiresPackageAttributeTests.cs` | TestCases `:67-68` (create, modify) and `:106` (modify-as-new-version) | moved to this ticket's final cut; each `[Description]` gains this ticket's reason: P1/P2 change what `addMapping` writes, and an older server mis-refuses `type: File` and silently creates an unbound parameter from a dotted mirror | AC-10 |
| CU-09 | `clio.tests/Command/ProcessModel/ManagerMapResolveDataIdTests.cs` | `[TestCase("fileProcessing")]`, `[TestCase("processFile")]` and their lowercase forms | `EventType.UserTask`, not `Unknown`. Carried in this PR for ENG-96505 Element readiness and object attachments mode, one release before any package emits the token (decision D10); without it `validate-process-graph` reports a hard Error on a described element (`clio/Command/ProcessModel/Schema.cs:1144-1145`) | AC-10 |
| CU-10 | `WorkspaceTemplateGuidanceDriftTests`, `ProcessDesignerEmittedSchemaTests` | unchanged | green: this PR adds no `name=` pointer to any description (decision D25), and the tools' input schemas are unchanged (descriptor and operations are strings) | AC-10 |

---

## 6. MCP end-to-end cases (E2E)

New fixture `clio.mcp.e2e/FileParameterToolE2ETests.cs`: `[TestFixture]`, `[AllureNUnit]`,
`[NonParallelizable]`, `[Category(McpE2ECategories.ProcessDesigner)]`, `MinimumPackageVersion` = this ticket's final
package cut (the first free number at or above 1.6.6.55, claimed in the PR). Each test creates its own processes
(`UsrClioBpFile<Case>E2e{Guid:N}`) and asserts on the tool result JSON and on a `describe-business-process` read-back.
Describe reports a formula `value` as the raw `[#...#]` expression, so "bound to X" below means: the parameter's
`value` contains `[Parameter:{<X uid>}]` (GUIDs compared case-insensitively) and `source` is `Script`.

The fixture deliberately does not use the generic `userTask` route for the three Process file schemas: ENG-96505
Element readiness and object attachments mode refuses it for the Object schema and ENG-96506 Generated report +
process parameter modes for the other two (decision D13), so a test built on it would turn red two PRs later.
The file-consuming targets it exercises are therefore a process-level FileCollection and a multi-instance
`InputRecordCollection`; the Process variant's `Files` is E2E-14 (section 6.2).

### 6.1 In this ticket's clio PR

| ID | Test | Arrange / Act | Assert | AC |
|---|---|---|---|---|
| E2E-01 | `CreateBusinessProcess_Should_DeclareFileAndFileCollection_AndReadThemBack` | create with `parameters`: `Doc` (`File`), `Docs` (`FileCollection`), `InDocs` (`FileCollection`, `direction: In`) | describe: `Doc` type `FileLocator`, direction Variable; `Docs` type `FileCollection`, direction Out, `itemProperties` = one `{name: File, type: FileLocator}`, no `tag`; `InDocs` direction In | AC-1, AC-2, AC-3 |
| E2E-02 | `CreateBusinessProcess_Should_RebuildTheDescribedFileParameters_Unchanged` | project E2E-01's described `parameters[]` to `{name, type, direction}`; create a second process from it | describe of the second equals the first on name, type, direction and item name/type | AC-3 |
| E2E-03 | `ModifyBusinessProcess_Should_AddFileAndFileCollectionParameters` | `addParameter {name: Doc, type: "file"}` and `{name: Docs, type: "FileCollection", direction: "In"}` on an existing process | describe as E2E-01 | AC-1 |
| E2E-04 | `ModifyBusinessProcess_Should_RefuseBinary_AndNameTheFileTypes` | `addParameter {type: "Binary"}` | the result contains `not supported`, `Binary` and `FileCollection`; describe shows no new parameter. The existing `ModifyBusinessProcess_Should_RejectUnsupportedParameterType` (`clio.mcp.e2e/ModifyBusinessProcessToolE2ETests.cs:2178-2205`) keeps its assertions; only its wording changes: the `[Description]` ("an unsupported (complex) type", `:2179`) and the Act comment ("a deferred complex type", `:2191`) become "refused for good: a file is type File" | AC-1 |
| E2E-05 | `ModifyBusinessProcess_Should_RefuseAConstantOnAFile` | arrange a process with `Docs` (`FileCollection`); `addParameter {name: Doc, type: File, value: "x"}`; then `addMapping {targetProcessParameter: Docs, value: "[]"}` | both refused ("no constant form"); describe shows `Doc` absent and `Docs` unbound | AC-2 |
| E2E-06 | `CreateBusinessProcess_Should_BindTheInputCollection_WhenOnlyAFileItemIsMapped` | callee with `F` (`File`, In); caller with `Docs` (`FileCollection`, In) and a multi-instance `subProcess` on the callee; `mappings`: only `{elementName: SubProcess1, elementParameter: "InputRecordCollection.F", processParameter: "Docs.File"}` | describe: `InputRecordCollection` bound to `Docs`; its item `F` bound to `Docs.File`. Proves P1 and the dotted `processParameter` member over the wire | AC-4, AC-5 |
| E2E-07 | `CreateBusinessProcess_Should_AcceptAFileCalleeOverReadDataRows_WithANotice` | callee with `ContactId` (Guid, In) and `F` (`File`, In); caller Read data `collection` mode (Contact, `columns: ["Id", "Name"]`, `numberOfRecords` 3; `Id` is listed explicitly because whether an unlisted primary column becomes an item of `ResultCompositeObjectList` is not established here); `InputRecordCollection <- ReadData1.ResultCompositeObjectList` and `InputRecordCollection.ContactId <- ReadData1.ResultCompositeObjectList.Id` | success; `warnings` contains `InputRecordCollection.F`; describe shows `F` unbound. Not refused: D5 scopes R-M2 to file-consuming targets | AC-5 |
| E2E-08 | `ModifyBusinessProcess_Should_PairTheFileItem_WhenAFileCollectionIsMappedOntoAnother` | process with `Docs` and `Docs2` (both `FileCollection`); `addMapping {targetProcessParameter: Docs2, processParameter: Docs}` | `warnings` names `Docs2.File`; describe: `Docs2` bound to `Docs`, `Docs2.File` bound to `Docs.File` (P2 on a file-consuming target) | AC-5 |
| E2E-09 | `ModifyBusinessProcess_Should_RefuseARecordCollectionIntoAFileCollection` | `Docs` (`FileCollection`) and a Read data `collection` element; `addMapping {targetProcessParameter: Docs, sourceElement: ReadData1, sourceElementParameter: ResultCompositeObjectList}` | refused (R-M2); describe shows `Docs` unbound | AC-5 |
| E2E-10 | `ModifyBusinessProcess_Should_RefuseACollectionItemIntoASingleFile` | `Doc` (`File`), `Docs` (`FileCollection`); `addMapping {targetProcessParameter: Doc, processParameter: "Docs.File"}` | refused (R-M1), the message names FileCollection and multi-instance; pending M6 (section 10) | AC-5 |
| E2E-11 | `ModifyBusinessProcess_Should_RefuseADottedMirrorSource` | a Read data `collection` element; `addParameter {name: P, typeFromElement: ReadData1, typeFromElementParameter: "ResultCompositeObjectList.Name"}` | refused naming the item and its collection; describe shows no `P` | AC-6 |
| E2E-12 | `DescribeBusinessProcess_Should_DecodeAnElementOutputSource` | the E2E-07 caller | describe: `InputRecordCollection` reports `sourceElement: ReadData1`, `sourceElementParameter: ResultCompositeObjectList`; its item `ContactId` reports `sourceElementParameter: "ResultCompositeObjectList.Id"`; no `sourceColumn` on either | AC-7 |
| E2E-13 | `ModifyBusinessProcess_Should_BindOnlyTheItem_WhenAPlainFileFeedsAMultiInstanceInput` | callee with `F` (File, In); caller multi-instance; `addMapping` of `InputRecordCollection.F <- Doc` (a single `File`: not a collection item, so P1 does not fire, and P3 applies only to a single-instance owner) | success with no binder notice (none of P1, P2, P2-MI, P3 applies); describe shows `InputRecordCollection` with source `None` and its item `F` bound to `Doc`. This is a regression check of the binder over the wire, NOT the proof of the nested-only listing (D8): describe lists every parameter of a sub-process element already (`PB/Describe/ProcessDescriber.cs:176-184`, source), so only a user-task input such as the Process variant's `Files` can show that rule, which is E2E-14c | AC-5 |

**Must stay green** on the same stand, run one fixture at a time after the new one, each result recorded in the PR:
`SubProcessMultiInstanceToolE2ETests` (its caller maps both levels explicitly, so P1 is a no-op there,
`clio.mcp.e2e/SubProcessMultiInstanceToolE2ETests.cs:596-600`), `ModifyBusinessProcess_Should_RejectUnsupportedParameterType`,
`RecordColumnSourceToolE2ETests` (the three-segment decode), `DescribeProcessToolE2ETests`, and
`SubProcessElementToolE2ETests` (single-instance sub-process mappings go through the same `ApplyMapping`).

### 6.2 The Process variant's `Files` over the wire (E2E-14)

| ID | Arrange / Act | Assert | AC |
|---|---|---|---|
| E2E-14a | a process with `Docs` (`FileCollection`) and a Process variant `PF1`; map `Files <- Docs` | `warnings` names `Files.File`; describe: `PF1.Files` bound to `Docs`, `PF1.Files.File` bound to `Docs.File` | AC-5 |
| E2E-14b | fresh process; map `Files.File <- Docs.File` | both levels bound (P1) | AC-5 |
| E2E-14c | fresh process; map `Files.File <- Doc` (a single File) | `Files` listed although its outer level is unbound (the only over-the-wire proof of the D8 nested-only listing), item bound to `Doc` | AC-5, AC-7, AC-9 |
| E2E-14d | map `Files <- ReadData1.ResultCompositeObjectList` | refused (R-M2) | AC-5 |

Where it runs is owner decision **O-TP1**:
- **(B), recommended: in ENG-96506 Generated report + process parameter modes' clio PR**, with `PF1` built by the
  `fileProcessing` block's `files` member (D18). Cost of waiting: AC-5's Process-variant half has PU evidence only
  (PU-26, PU-27, PU-31..PU-34, PU-38, PU-39, PU-41) for one ticket, and so has AC-7's nested-only listing (PU-48).
  The risk that leaves is small: the rules run in the same `ApplyMapping` code E2E-06 and E2E-08 exercise over the
  wire, and the drift guard (PU-50) ties the hand-built element to the PD metadata the stand serves. Tracked as
  TC-91 of the element [test plan](../eng-92719-file-processing-element/eng-92719-file-processing-element-test-plan.md)
  (CL-RP).
- **(A): in this ticket's clio PR**, with `PF1` built through `{type: userTask, userTaskName: ProcessFileProcessingUserTask}`,
  legal on this cut and on the cut of ENG-96505 Element readiness and object attachments mode (D13 scopes the refusal
  per cut). Cost: the clio PR of ENG-96506 Generated report + process parameter modes must rewrite the arrange
  step, and the test builds an element that saves green unconfigured (no target, no action), a hazard D13 exists to
  close. It also depends on the generic route saving that element on a real server, which is
  source-traced only (D13; measurement M14 measures the Object variant's generic shape, not this one).

---

## 7. Knowledge cases (KU)

| ID | Test (clio-knowledge `automation/Clio.Knowledge.Bundle.Tests/`) | Assert | AC |
|---|---|---|---|
| KU-01 | NEW `FileParameterGuidanceTests.cs` over `guidance/mcp/guides/processes/files.md` (`process-files`) | the guide states: the two types and FileLocator; the FileCollection read-back and the item-name caveat; "a FileCollection a caller fills is declared `In`"; P1, P2, P2-MI, P3, R-M1, R-M2 and the file-consuming targets; "an `expression` is written verbatim"; the gate sentence "use only when get-tool-contract for create-business-process mentions type File" with a concrete version (no TBD) | AC-10 |
| KU-02 | `CollectionParameterGuidanceTests.cs:24-29` re-pinned (the rationale at `:24-25` amended) | the FileCollection exception to `parameters.md:35-37` is stated; "binary" (not "file") in the not-supported list; the rationale names the alias exception | AC-3, AC-10 |
| KU-03 | NEW pins over the rewritten `sub-process.md:133-137` and `sub-process-when.md:49-51` | the version-gated sentence ("from CrtProcessBuilder <cut>, a per-item value taken from an item of a collection binds that collection too ..."); "send both" kept for older servers | AC-5, AC-10 |
| KU-04 | `ProcessGuideResponseSizeTests` | every article, the new one included, under the enforced ceiling of 27,793 chars (JSON-escaped length + 1,400 envelope, `ProcessGuideResponseSizeTests.cs:43, 131, 156`); the new guide's ENG-95984 File process parameter type part well below the design target of 22,000 chars that the whole guide must still meet after ENG-96506 Generated report + process parameter modes (decisions D25); `parameters.md` within the ceiling (345 chars of headroom before the edit, measured 2026-10-01); sub-process-when re-measured | AC-10 |
| KU-05 | `GuidanceMigrationTests.PostMigrationGuidance`, `ProcessGuideSet.GoLiveFloor`, `ProcessGuideCrossReferenceTests` | the guide is registered (`bundle-source.json` entry, `requirements.itemIds`, `resourceUris`, routing row) and every cross-reference resolves | AC-10 |

---

## 8. Stand verification (V)

Rules for every row: stand `Creatio` (core 10.1.37, .NET Framework). Every write (a saved process, a run, a fixture
record, an upload) needs the user's explicit go-ahead, row by row. Schema writes and runs go one at a time. Cleanup
uses `execute-dataservice-batch` (the stand rejects HTTP DELETE); a remote `delete-schema` takes about six minutes,
so pass the CLI `--timeout`. Runtime evidence comes from `SysProcessElementLog` (a `SysProcessLog` root row with no
`CompleteDate` is a parked process, not a hung one). UI results are checked by the user. Before any
guidance-dependent run, `info-knowledge` must show the expected libraryVersion: a failed `update-knowledge` keeps
serving the old guidance silently.

### 8.1 Before code (W0 of the plan)

| ID | What (basis today) | Method | Write? | Decides | Reaches |
|---|---|---|---|---|---|
| M3 | an ENG-96230 Collection process parameter type mirror bound root-only reads null rows (source; **refuted 2026-10-02**: 3 iterations, all "name set") | as plan W0 and open-questions B.4. Callee `UsrFpM3Callee` records the result in branch captions (`M3 name empty` / `M3 name set`). Probe `UsrFpM3MirrorProbe`: Read data collection -> mirror `P` -> multi-instance sub-process over `P`, plus ONE designer step by the user mapping the callee's `Name` from `P > Name` (1.6.6.54 refuses the dotted `P.Name` from the builder, D4). Control `UsrFpM3Control` on element paths only. Count the caption rows per caller run (EV-4) | yes | O-5 and where PU-53 lives (decided: O-5 moot, PU-53 in PK-PT) | Runtime |
| MI-0 | an item-only multi-instance mapping runs ONE iteration on 1.6.6.54 (measured on 1.6.6.22 per `sub-process.md:133-137`) | `InputRecordCollection.Name <- RD.ResultCompositeObjectList.Name` only, 3 contacts | yes | the baseline for V3 | Runtime |
| M6 | a flat FileLocator from a collection item outside row context: semantics unknown | designer-built probe, one run | yes | PU-40, E2E-10: refusal stays, or "first row" | Runtime |
| V0 | the shipped captures still describe what today's designer writes. They are old: `FileParameterProcess` carries `B8` = `7.17.3.396` (source) | the UO method of 2026-10-01 (a NEW unsaved process in the classic designer at `/0/Nui/ViewModule.aspx?vm=SchemaDesigner#process/`, the route needs `?vm=SchemaDesigner`; read `Terrasoft.ProcessSchemaManager.items[0].instance` in memory, where UO-2..UO-4 read `.flowElements`; confirm the parameters collection's property name on first use): plain Add > Other > File; Add > Other > Collection of records, then the nested Add > Other > File. Read direction, Tag, item name, item direction; close the tab unsaved. Serialization itself is server code shared with the builder, so field values are what this needs to compare (inference) | no | the expected values of PU-11 and PU-13 (Variable, no Tag on plain Add; source `PD/ProcessSchemaPropertiesPage/ProcessSchemaPropertiesPage.js:1204-1236`) | Design time |

M1 (a single File bound on `Files.File` only copies one file) and M2 (unset File gives an element Error) are
ENG-96506 Generated report + process parameter modes measurements; they decide AC-9's wording and the guide's
single-file sentence, not this ticket's code (plan 5.0). If they run earlier, use the probe `UsrG1FilesBindingProbe`
described in the element [test plan](../eng-92719-file-processing-element/eng-92719-file-processing-element-test-plan.md)
(its M1 row; the question itself is decisions D29, M1) and run V8 below on the same fixtures.

### 8.2 After the cut, before the package PR leaves draft

V1-V6 are the plan's rows (section 6), repeated here with their pass conditions; V7 and V8 are additions.

| ID | What | Method | Write? | Pass | Reaches | AC |
|---|---|---|---|---|---|---|
| V1 | instrument check | build the clio PR; `install-process-builder` from the refreshed BUILD OUTPUT (an install never reads the repository copy); `list-packages` shows the cut; `addParameter type: File` on a disposable process succeeds (only the new code answers); `get-tool-contract` for create mentions `File` | yes | all three | Stored | AC-10 |
| V2 | e2e | section 6 fixture, then the must-stay-green list, sequentially | yes | Passed, **Ignored = 0** | Stored | AC-1..AC-7, AC-10 |
| V3 | P1 at run time | the MI-0 process rebuilt on the cut | yes | 3 iterations, against 1 on MI-0 | Runtime | AC-5 |
| V4 | serialization: the PK-PT row of SC-4, the builder twin of [serialization capture](../eng-92719-file-processing-element/eng-92719-file-processing-element-serialization-capture.md) section 12.7 | (1) **SC-0** first (section 12.3, read-only): export a shipped schema and diff it with its PackageStore copy, so a read-path or install artefact is never charged to the builder. (2) `create-business-process` `UsrFpSc4Pt` with `PFile` (`File`), `PFiles` (`FileCollection`, `direction: Variable`) and `PFilesOut` (`FileCollection`, default), one request. (3) Read back stored state by path A or B of section 12.2 (`pull-pkg` or `export-schema`), never describe. (4) Normalise and classify by section 12.8 against the three shipped captures; if SC-1 (section 12.4, the designer capture built for ENG-96505 Element readiness and object attachments mode) has already been saved, ALSO against its `PFile`, `PFiles`, `PFilesOut`, which are current-designer captures with the D2 item name. SC-1 is not a PT gate (G-C7: optional for PT). Record the table in `docs/file-parameter-capture.md` (PB-10) and the PR. About 0.5 h for SC-0 and 1-1.5 h for the rest (inference) | yes (the build) | equal under the AC-8 comparison rule (section 4.2): N5, PT-a, N9, N15, N14, PT-b, nothing else; one DIFF fails ("fix before merge", plan section 6). Against SC-1's captures PT-b does not apply | Stored | AC-8 |
| V5 | designer opens builder output (part of M22) | the user opens the V4 process in the classic designer, opens each parameter, closes; then a no-op save; diff `SysSchema.MetaData` before and after | yes (the save) | no error; no diff | Design time | AC-8 |
| V6 | guidance in force | after the knowledge release, `info-knowledge` shows the new libraryVersion | no | version matches | - | AC-10 |
| V7 | **a file passed through a builder-written FileCollection is consumed** (optional, recommended before the guide teaches the pattern) | fixtures: contact `G1 Probe Source` with 2 attachments (ContactFile rows), contact `G1 Probe Target`. The user builds `UsrPtFilesFlowProbe` in the designer: Start -> `OF1` (Object attachments, source `Contact (Contact attachment)`, filter Contact = G1 Probe Source, Use in process) -> `PF1` (Process parameter source, target `Contact (Contact attachment)`, record = G1 Probe Target, Files bound to `OF1.ObjectFiles.File`, which also binds `Files` to `OF1.ObjectFiles`) -> End; saves. Then the BUILDER writes, through `modify-business-process`: `addParameter {name: Docs, type: FileCollection}`; `addMapping {targetProcessParameter: Docs, sourceElement: OF1, sourceElementParameter: ObjectFiles}` (P2 pairs `Docs.File`); `addMapping {elementName: PF1, elementParameter: Files, processParameter: Docs}` (P2 rebinds the stale `Files.File`, with its notice). Describe, then run once | yes | describe shows both levels of `PF1.Files` on `Docs` and both levels of `Docs` on `OF1.ObjectFiles`; `OF1` and `PF1` Completed in `SysProcessElementLog`; 2 new ContactFile rows on G1 Probe Target, names equal to the sources | Runtime | AC-5, AC-6, AC-9 |
| V8 | a single builder-declared File is consumed (only if M1 passed) | `create-business-process` a callee `UsrPtFileCallee` with `F` (`File`, In); the user opens it in the designer (this is also a V5 check), adds a Process file element with source Process parameter, the "Files" field (caption `FilesCaption`, `CrtProcessDesigner/branches/7.8.0/Resources/ProcessFileProcessingUserTaskPropertiesPage.ClientUnit/resource.en-US.xml`) set to the parameter `F`, which binds `Files.File` only (the nested-only single-file shape), target as V7, and saves. Caller: a designer-built Start -> `OF1` (as V7) -> End; the builder adds a multi-instance `subProcess` on the callee between them (`removeFlow`, `addElement`, two `addFlow`) and only `InputRecordCollection.F <- OF1.ObjectFiles.File`. Describe, then run once | yes | describe: `InputRecordCollection` bound to `OF1.ObjectFiles` (P1); 2 callee iterations; 2 new ContactFile rows on G1 Probe Target | Runtime | AC-5, AC-9 |

Evidence for V3, V7 and V8 follows the read-only recipe EV-0..EV-6 of the element
[test plan](../eng-92719-file-processing-element/eng-92719-file-processing-element-test-plan.md) (section 9), one
`execute-esq` call at a time: an `Id` baseline of the target's ContactFile rows BEFORE the run (never a `CreatedOn`
filter: ESQ resolves dates in the profile's time zone); the instance from `SysProcessLog` by `SysSchema.Name`, newest
first; every element in `SysProcessElementLog` filtered by `SysProcess = <instance>` (`Caption`, `Status.Name`,
`CompleteDate`, `ErrorDescription`), which is the evidence; for V3 and V8 the callee's `SysProcessLog` rows outside
the child baseline, which is the iteration count; after the run, the ContactFile rows NOT IN the baseline (`Name`,
`Size`, `Version`, `Type.Name`) against the source rows for name and size parity. Hazards behind each query are in the
[traps](../eng-92719-file-processing-element/eng-92719-file-processing-element-traps.md) document. Cleanup (go-ahead first): the probe processes through the designer or `delete-schema --timeout`, the two
contacts and the copied ContactFile rows in one `execute-dataservice-batch`, then re-run the counts.

If V7 fails while V3 passes, the binder writes the right metadata and the runtime does not read it the way the
source trace says: hold the clio and knowledge PRs and record the measured outcome before changing any rule.

---

## 9. Traceability

| AC (plan 1.4) | PU | CU | E2E | KU | V |
|---|---|---|---|---|---|
| AC-1 types, aliases, Binary refusal | 01-04, 06, 47 | - | 01, 03, 04 | - | V1, V2 |
| AC-2 defaults and refusals | 01, 11, 12, 14-18 | - | 01, 05 | - | V2 |
| AC-3 describe read-back rebuilds | 05, 07-10, 52 | 01 | 01, 02 | 02 | V2 |
| AC-4 dotted process paths | 21-25 | - | 06 | - | V2 |
| AC-5 two-level binder and refusals | 26-46, 47, 52 | - | 06-10, 13, 14 | 03 | V3, V7, V8 |
| AC-6 mapped from element outputs; mirror refusals | 19, 20, 28 | - | 11 | - | V7 |
| AC-7 describe decode, nested-only listing | 48, 49, 51, 52, 54 (50 supports) | 02, 05 | 12; 14c for the nested-only listing (O-TP1) | - | V2 |
| AC-8 serialization equals shipped captures under the AC-8 comparison rule (section 4.2) | 13 (06 for the non-empty `ContainerUId`) | - | - | - | V0, V4 (SC-0, then SC-4's PK-PT row), V5 |
| AC-9 consumption by the Process variant (design time here) | 39 | - | 14c | - | V7, V8 (M1, M2 in ENG-96506 Generated report + process parameter modes) |
| AC-10 floor, descriptions, guide, e2e | - | 03-10 | all | 01-05 | V1, V2, V6 |
| D9 mirror binds items (no AC) | 53 | - | - | - | M3 |

---

## 10. Cases that move with an open decision or a measurement

| Trigger | Cases affected | If it goes the other way |
|---|---|---|
| O-1 (D2: FileCollection alias, option A) | PU-06..PU-10, PU-13, E2E-01..E2E-03, CU-01 | option B or C rewrites section 4.2 and E2E-01..E2E-03 |
| O-2 (D3: FileCollection default Out) | PU-11, PU-13, E2E-01 | Variable: change the fixture constant; PU-13's default case compares with `FileParameterProcess.FileCollection` (under row PT-b of the AC-8 comparison rule) instead of `MarkProcessesToCancel.FilesCollection` |
| O-3 (P3 resets a stale parent) | PU-38 | designer parity: PU-38 asserts the parent is kept and no notice |
| O-4 (P2 scope: file-consuming targets) | PU-35, PU-36, PU-42, E2E-07 | every collection: PU-36/PU-42 assert pairing, and Read data -> multi-instance mappings change (unmeasured, out of this ticket) |
| O-5 (D9 placement), M3 | PU-53 | refuted, and this applies (M3, 2026-10-02): PU-53 still asserts item binding as parity with 60 of 61 shipped collections; the "defect" wording leaves its `[Description]` |
| M6 | PU-40, E2E-10 | "first row" semantics: R-M1 becomes a notice and both cases flip to accept-with-notice |
| M1, M2 (ENG-96506 Generated report + process parameter modes) | PU-39, E2E-14c, V8 | the single-file path fails: PU-39 stays (metadata), the guide says "wrap one file in a FileCollection", V8 is dropped |
| O-TP1 (this plan: where E2E-14 runs) | E2E-14 | (A): E2E-14 moves into section 6.1 and the clio PR of ENG-96506 Generated report + process parameter modes rewrites its arrange step |
| A foreign cut lands between ours (the PR split's capability probe rule) | V1 | add a capability probe on a named field and run it against both archives |
| The helper and guard owner, PK-PT (plan PB-9; element test plan TC-01/TC-02; reuse section 10.1) | section 3 item 2, PU-50, every PU case that uses `AProcessFileElement`, `AnObjectFileElement` or `AReportFileElement` (PU-08d, PU-16, PU-17, PU-19, PU-20, PU-22, PU-26..PU-46, PU-48, PU-49, PU-52) | the owner moves both to ENG-96505 Element readiness and object attachments mode: PK-PT hand-builds its elements with no guard (not recommended), and PU-50 moves to PK-OA unchanged |
| The AC-8 comparison rule (section 4.2), also stated in plan AC-8 and V4 and in pr-split section 14 | PU-13, V4 | a difference outside the list is a DIFF (fix before merge); a new tolerated deviation needs the owner and a row in [serialization capture](../eng-92719-file-processing-element/eng-92719-file-processing-element-serialization-capture.md) section 10 |

---

## 11. Deliberately not covered here

| Not covered | Why / where |
|---|---|
| The Process file element's configuration (source object, storage, filter, report, action) | ENG-96505 Element readiness and object attachments mode and ENG-96506 Generated report + process parameter modes, [element test plan](../eng-92719-file-processing-element/eng-92719-file-processing-element-test-plan.md) |
| Send email attachments and Creatio.ai file inputs as consumers | ENG-95985 Send email attachments; Creatio.ai is out of scope ([use cases](../eng-92719-file-processing-element/eng-92719-file-processing-element-use-cases.md)) |
| Describe decode of process-parameter sources into `processParameter` | proposed Sub-task "Describe: decode process-parameter sources into re-appliable names"; until then those bindings replay as verbatim expressions, which PU-43 and PU-52 cover |
| A declared item shape for any Collection; an allow-list of mirrorable types | proposed Sub-tasks "Declared item shape for Collection process parameters" and "Allow-list the data types a typeFromElement mirror may copy" |
| Passing a file as a start value through `run-process` | the start-value initializer has no FileLocator-specific branch. `IFileLocator` is an `ISerializableObject` (`CORE/Terrasoft.File.Abstractions/IFileLocator.cs:10`), so a passed string falls into the generic serializable-object branch (`CORE/Terrasoft.Core/Process/ProcessParameterValueInitializer.cs:97-101`), which either builds a CompositeObjectList (`ProcessParameterDataUtils.GetSerializableObject`, `CORE/Terrasoft.Core/Process/ProcessParameterDataUtils.cs:209-218`) or deserializes type-annotated JSON; no tested string form of a file exists (source, unmeasured). Out of scope and not tested: in this ticket a file reaches a process parameter through a mapping only |
| SysFile attachment storage | the proposed Sub-task "SysFile attachment storage in the Process file element" |
| ClioRing | consumes only catalog names, Purpose text and Destructive flags and dispatches non-destructive tools generically (`clio-ring/ClioRing.Ipc/ClioIpcModels.cs:80-95`, `clio-ring/ClioRing/ViewModels/ClioIpcViewModel.cs:144-157`); names, flags and Purpose leads are unchanged, so the PR states "ClioRing compatibility reviewed, no Ring-consumed contract changed" (plan 5.2); no Ring test runs |
