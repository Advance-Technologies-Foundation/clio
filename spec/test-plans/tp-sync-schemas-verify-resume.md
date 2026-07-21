# Test Plan: sync-schemas create-lookup resume — verify against intent

**Feature**: sync-schemas-verify-resume
**Stories**: [story-1](../stories/story-sync-schemas-verify-resume-1.md), [story-2](../stories/story-sync-schemas-verify-resume-2.md), [story-3](../stories/story-sync-schemas-verify-resume-3.md)
**PRD**: [prd-sync-schemas-verify-resume.md](../prd/prd-sync-schemas-verify-resume.md)
**ADR**: [adr-sync-schemas-verify-resume.md](../adr/adr-sync-schemas-verify-resume.md)
**Jira**: ENG-93374
**Author**: QA Planner Agent
**Status**: Draft
**Created**: 2026-07-20

---

## Scope

### In scope

- The `ExecuteCreateSchema` same-target-package create-lookup resume sub-branch in
  `clio/Command/McpServer/Tools/SchemaSyncTool.cs` (L326–342 today) reworked to verify requested
  columns before forcing success (`VerifyRequestedColumns` + `ColumnVerification { Verified, Missing, Unverified }`).
- The three routed outcomes: empty/all-present resume (FR-01/FR-04), confirmed durable collision
  → honest `success:false` + "use update-entity" hint (FR-03), unverifiable read → distinct
  `resumed-existing` status + warning (FR-05).
- Result `Status`/`Messages` consistency: `FinalizeResult(forcedStatus)` + `Classify` `??=` preservation;
  no failed-create Error lines in a success/resumed result (FR-06); `resumed-existing` derives from the
  final post-registration execution (AC-04-terminal).
- The different-package collision guard regression (FR-07 / FR-09) — a dedicated negative test.

### Out of scope

- Convergent "ensure" semantics (read → apply delta → verify) — tracked in **ENG-93807** (PRD Non-goal).
- Auto-applying columns / `update-entity` on a durable collision — the tool returns the hint only (Non-goal).
- Any new CLI flag/verb — this is internal MCP result-semantics only; CLIO001 unaffected.
- Per-column type/length comparison — presence-by-name only (OQ-02; ENG-93807).
- MCP E2E execution in CI — not wired into CI; documented as manual-only below.

---

## Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|-----------|
| `ExecuteCreateSchema` rework regresses the existing empty-columns resume (FR-01 fast-path) | High | High | Keep `SchemaSync_CreateLookup_Should_Complete_Registration_When_Schema_Already_Exists_In_Target_Package` green; add TC-U-01 asserting NO `GetSchemaProperties` read on the empty-columns path |
| `FinalizeResult`/`Classify` change flips `Status` for unrelated ops or double-assigns | Med | High | `Classify` becomes `??=` (preserve pre-set); regression cover all existing status-asserting create/update/seed tests; TC-U-08 pins `resumed-existing` does not stick when registration then fails |
| Durable collision silently forced to success (the ENG-93374 defect this feature fixes) | High (pre-fix) | High | TC-U-05 (AC-03) asserts `success:false` + "use update-entity" hint + `DidNotReceive().EnsureLookupRegistration` |
| Probe fault mis-routed to `success:false`, re-introducing the resume loop | Med | High | TC-U-06 (AC-04) asserts read-failure → `resumed-existing`, `success:true` (never `failed`); routing is read-success/read-failure, classifier is text-only |
| Different-package collision starts skipping create / invoking registration after refactor | Med | High | TC-U-09 (AC-05/FR-09) create invoked once + `Success==false` + `DidNotReceive().EnsureLookupRegistration` + no `GetSchemaProperties` read |
| Second read round-trip added instead of reusing the single collision probe | Med | Med | ADR reuse rule; TC-U-01 + TC-U-09 assert `GetSchemaProperties` NOT called on paths that must not read; collision probe stays the single `FindEntitySchema` call |
| MCP `sync-schemas` E2E not in CI, `resumed-existing` unverified end-to-end | High | Med | Manual E2E gate (TC-E-01/02) added to PR checklist; `docs/McpCapabilityMap.md` documents the additive status |
| Message leakage — failed-create Error lines surface in a `completed`/`resumed-existing` result | Med | Med | TC-U-07 (AC-06) asserts no Error-level messages in a resumed success result |

MCP touch: yes — `sync-schemas` is an MCP tool (`SchemaSyncTool`). No E2E runs in CI (see below).

---

## Test Seam & Reuse Notes (grounded in the real fixture)

All tests extend the existing `clio.tests/Command/McpServer/SchemaSyncToolTests.cs` fixture — no new files.

**Reusable existing helpers / fakes (already in the fixture):**
- `ScriptedCreateEntitySchemaCommand(logger, params AttemptOutcome[])` with `.Invocations` — the create seam; use `Fail("Schema X already exists.")` to force the collision branch (`Fail`/`Success`/`Transient` factories at L2095–2099).
- `FakeFindEntitySchemaCommand(IReadOnlyList<EntitySchemaSearchResult>)` — the collision probe; return `new EntitySchemaSearchResult(schema, package, "Customer", "BaseLookup")`. Same-package → resume branch; different-package → guard.
- `ILookupRegistrationService` substitute — assert `Received(1)` / `DidNotReceive().EnsureLookupRegistration(pkg, schema, title)`.
- `SchemaSyncTool(commandResolver, logger, retryDelay: Substitute.For<IRetryDelay>())` ctor overload — used by all resume/retry tests to avoid real delays.
- `TestLogger`, `Localizations("Genre")`, `GetMessageValues(result)`, `ToJsonElement`.

**NEW column-read seam (per ADR — zero production `virtual` needed):** resolve a REAL
`GetEntitySchemaPropertiesCommand(stubManager, logger)` where
`stubManager = Substitute.For<IRemoteEntitySchemaColumnManager>()`. The real command's
`GetSchemaProperties(options)` delegates to `stubManager.GetSchemaProperties(options)`, so:
- read-success: `stubManager.GetSchemaProperties(Arg.Any<GetEntitySchemaPropertiesOptions>()).Returns(info)`
- read-failure (AC-04): `stubManager.GetSchemaProperties(Arg.Any<GetEntitySchemaPropertiesOptions>()).Throws(new HttpRequestException("No such host is known."))`
- no-read assertion (AC-01/AC-05): `stubManager.DidNotReceive().GetSchemaProperties(Arg.Any<GetEntitySchemaPropertiesOptions>())`

Wire it through the resolver exactly like the find seam:
`commandResolver.Resolve<GetEntitySchemaPropertiesCommand>(Arg.Any<GetEntitySchemaPropertiesOptions>()).Returns(realCommandWithStubManager)`.

`EntitySchemaPropertiesInfo` is a positional record with many params (`EntitySchemaReadModels.cs` L37–60);
add a tiny local builder so tests only specify the column names that matter:

```csharp
private static EntitySchemaPropertiesInfo SchemaWith(params string[] columnNames) =>
    new(
        Name: "UsrColors", Title: "Colors", Description: null, PackageName: "UsrPkg",
        ParentSchemaName: "BaseLookup", ExtendParent: false, PrimaryColumnName: "Id",
        PrimaryDisplayColumnName: "Name", OwnColumnCount: columnNames.Length, InheritedColumnCount: 0,
        IndexesCount: null, TrackChangesInDb: false, DbView: false, SspAvailable: null, Virtual: false,
        UseRecordDeactivation: null, ShowInAdvancedMode: false, AdministratedByOperations: false,
        AdministratedByColumns: false, AdministratedByRecords: false, UseDenyRecordRights: null,
        UseLiveEditing: null,
        Columns: columnNames.Select(n => new EntitySchemaPropertyColumnInfo(
            Name: n, UId: Guid.NewGuid(), Source: "own", Title: n, Description: null,
            Type: "Text", Required: false, Indexed: false, ReferenceSchemaName: null)).ToList());

private static GetEntitySchemaPropertiesCommand ReadReturning(EntitySchemaPropertiesInfo info) {
    var stubManager = Substitute.For<IRemoteEntitySchemaColumnManager>();
    stubManager.GetSchemaProperties(Arg.Any<GetEntitySchemaPropertiesOptions>()).Returns(info);
    return new GetEntitySchemaPropertiesCommand(stubManager, Substitute.For<ILogger>());
}

private static (GetEntitySchemaPropertiesCommand Command, IRemoteEntitySchemaColumnManager Manager)
    ReadThrowing(Exception fault) {
    var stubManager = Substitute.For<IRemoteEntitySchemaColumnManager>();
    stubManager.GetSchemaProperties(Arg.Any<GetEntitySchemaPropertiesOptions>()).Throws(fault);
    return (new GetEntitySchemaPropertiesCommand(stubManager, Substitute.For<ILogger>()), stubManager);
}
```

The durable-collision example from the PRD/ADR is `UsrColors` with a new `UsrHexCode` column against an
already-existing `UsrColors` — used verbatim in TC-U-05.

---

## Unit Tests (`clio.tests/Command/McpServer/SchemaSyncToolTests.cs`)

> All `[Category("Unit")]`, NUnit4 + FluentAssertions + NSubstitute. Naming `MethodName_ShouldBehavior_WhenCondition`.

### TC-U-01 (AC-01, FR-01) — empty columns resume without reading

```csharp
[Test]
[Category("Unit")]
[Description("Empty op.Columns on a same-package collision resumes via the FR-01 fast-path without issuing a column-read round-trip.")]
public async Task ExecuteCreateSchema_ShouldResumeWithoutReading_WhenColumnsEmptyAndSamePackageCollision() {
    // Arrange
    var logger = new TestLogger();
    var scriptedCreate = new ScriptedCreateEntitySchemaCommand(logger, Fail("Schema UsrGenre already exists."));
    ILookupRegistrationService registrationService = Substitute.For<ILookupRegistrationService>();
    var stubManager = Substitute.For<IRemoteEntitySchemaColumnManager>();
    IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
    commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>()).Returns(scriptedCreate);
    commandResolver.Resolve<ILookupRegistrationService>(Arg.Any<EnvironmentOptions>()).Returns(registrationService);
    commandResolver.Resolve<FindEntitySchemaCommand>(Arg.Any<FindEntitySchemaOptions>())
        .Returns(new FakeFindEntitySchemaCommand([new EntitySchemaSearchResult("UsrGenre", "UsrPkg", "Customer", "BaseLookup")]));
    commandResolver.Resolve<GetEntitySchemaPropertiesCommand>(Arg.Any<GetEntitySchemaPropertiesOptions>())
        .Returns(new GetEntitySchemaPropertiesCommand(stubManager, Substitute.For<ILogger>()));
    SchemaSyncTool tool = new(commandResolver, logger, retryDelay: Substitute.For<IRetryDelay>());
    SchemaSyncArgs args = new("dev", "UsrPkg",
        [new SchemaSyncOperation("create-lookup", "UsrGenre", TitleLocalizations: Localizations("Genre"))]); // no Columns

    // Act
    SchemaSyncResponse response = await tool.SchemaSync(args);

    // Assert
    response.Success.Should().BeTrue(because: "an empty-columns same-package collision must still resume (FR-01)");
    response.Results[0].Status.Should().Be("completed", because: "the empty-columns fast-path completes, not resumed-existing");
    scriptedCreate.Invocations.Should().Be(1, because: "resume must not re-run create");
    stubManager.DidNotReceive().GetSchemaProperties(Arg.Any<GetEntitySchemaPropertiesOptions>());
    registrationService.Received(1).EnsureLookupRegistration("UsrPkg", "UsrGenre", "Genre");
}
```

### TC-U-02 (AC-02, FR-04) — all requested columns present → resume succeeds

```csharp
[Test]
[Category("Unit")]
[Description("A same-package collision where every requested column already exists resumes to completed with registration.")]
public async Task ExecuteCreateSchema_ShouldResume_WhenAllRequestedColumnsPresent() {
    // Arrange
    var logger = new TestLogger();
    var scriptedCreate = new ScriptedCreateEntitySchemaCommand(logger, Fail("Schema UsrColors already exists."));
    ILookupRegistrationService registrationService = Substitute.For<ILookupRegistrationService>();
    IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
    commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>()).Returns(scriptedCreate);
    commandResolver.Resolve<ILookupRegistrationService>(Arg.Any<EnvironmentOptions>()).Returns(registrationService);
    commandResolver.Resolve<FindEntitySchemaCommand>(Arg.Any<FindEntitySchemaOptions>())
        .Returns(new FakeFindEntitySchemaCommand([new EntitySchemaSearchResult("UsrColors", "UsrPkg", "Customer", "BaseLookup")]));
    commandResolver.Resolve<GetEntitySchemaPropertiesCommand>(Arg.Any<GetEntitySchemaPropertiesOptions>())
        .Returns(ReadReturning(SchemaWith("UsrHexCode")));
    SchemaSyncTool tool = new(commandResolver, logger, retryDelay: Substitute.For<IRetryDelay>());
    SchemaSyncArgs args = new("dev", "UsrPkg",
        [new SchemaSyncOperation("create-lookup", "UsrColors", TitleLocalizations: Localizations("Colors"),
            Columns: [new CreateEntitySchemaColumnArgs("UsrHexCode", "Text", Localizations("Hex code"))])]);

    // Act
    SchemaSyncResponse response = await tool.SchemaSync(args);

    // Assert
    response.Success.Should().BeTrue(because: "all requested columns are present → legitimate resume (FR-04)");
    response.Results[0].Status.Should().Be("completed", because: "a verified resume is completed, not resumed-existing");
    scriptedCreate.Invocations.Should().Be(1, because: "verified resume must not re-run create");
    registrationService.Received(1).EnsureLookupRegistration("UsrPkg", "UsrColors", "Colors");
}
```

### TC-U-03 (AC-02b, FR-08) — extra existing columns allowed (case-insensitive subset)

```csharp
[Test]
[Category("Unit")]
[Description("Verification is a case-insensitive subset check: an existing schema with the requested column plus extras still resumes.")]
public async Task ExecuteCreateSchema_ShouldResume_WhenRequestedColumnPresentAmongExtrasCaseInsensitive() {
    // Arrange
    var logger = new TestLogger();
    var scriptedCreate = new ScriptedCreateEntitySchemaCommand(logger, Fail("Schema UsrColors already exists."));
    ILookupRegistrationService registrationService = Substitute.For<ILookupRegistrationService>();
    IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
    commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>()).Returns(scriptedCreate);
    commandResolver.Resolve<ILookupRegistrationService>(Arg.Any<EnvironmentOptions>()).Returns(registrationService);
    commandResolver.Resolve<FindEntitySchemaCommand>(Arg.Any<FindEntitySchemaOptions>())
        .Returns(new FakeFindEntitySchemaCommand([new EntitySchemaSearchResult("UsrColors", "UsrPkg", "Customer", "BaseLookup")]));
    // existing columns differ in case and include unrelated extras
    commandResolver.Resolve<GetEntitySchemaPropertiesCommand>(Arg.Any<GetEntitySchemaPropertiesOptions>())
        .Returns(ReadReturning(SchemaWith("usrhexcode", "UsrLegacy", "UsrNote")));
    SchemaSyncTool tool = new(commandResolver, logger, retryDelay: Substitute.For<IRetryDelay>());
    SchemaSyncArgs args = new("dev", "UsrPkg",
        [new SchemaSyncOperation("create-lookup", "UsrColors", TitleLocalizations: Localizations("Colors"),
            Columns: [new CreateEntitySchemaColumnArgs("UsrHexCode", "Text", Localizations("Hex code"))])]);

    // Act
    SchemaSyncResponse response = await tool.SchemaSync(args);

    // Assert
    response.Success.Should().BeTrue(because: "presence-by-name is case-insensitive and allows extra existing columns (FR-08)");
    response.Results[0].Status.Should().Be("completed");
    registrationService.Received(1).EnsureLookupRegistration("UsrPkg", "UsrColors", "Colors");
}
```

### TC-U-04 (AC-ERR) — non-collision failure unchanged (resume never entered)

```csharp
[Test]
[Category("Unit")]
[Description("A create-lookup that fails with no same-name schema found returns the honest failure unchanged — the resume sub-branch is never entered because collision is null.")]
public async Task ExecuteCreateSchema_ShouldReturnHonestFailure_WhenNoCollisionSchemaFound() {
    // Arrange
    var logger = new TestLogger();
    var scriptedCreate = new ScriptedCreateEntitySchemaCommand(logger, Fail("create-lookup failed with exit code 1: network timeout"));
    ILookupRegistrationService registrationService = Substitute.For<ILookupRegistrationService>();
    var stubManager = Substitute.For<IRemoteEntitySchemaColumnManager>();
    IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
    commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>()).Returns(scriptedCreate);
    commandResolver.Resolve<ILookupRegistrationService>(Arg.Any<EnvironmentOptions>()).Returns(registrationService);
    commandResolver.Resolve<FindEntitySchemaCommand>(Arg.Any<FindEntitySchemaOptions>())
        .Returns(new FakeFindEntitySchemaCommand([])); // schema not found → collision is null
    commandResolver.Resolve<GetEntitySchemaPropertiesCommand>(Arg.Any<GetEntitySchemaPropertiesOptions>())
        .Returns(new GetEntitySchemaPropertiesCommand(stubManager, Substitute.For<ILogger>()));
    SchemaSyncTool tool = new(commandResolver, logger, retryDelay: Substitute.For<IRetryDelay>());
    SchemaSyncArgs args = new("dev", "UsrPkg",
        [new SchemaSyncOperation("create-lookup", "UsrColors", TitleLocalizations: Localizations("Colors"),
            Columns: [new CreateEntitySchemaColumnArgs("UsrHexCode", "Text", Localizations("Hex code"))])]);

    // Act
    SchemaSyncResponse response = await tool.SchemaSync(args);

    // Assert
    response.Success.Should().BeFalse(because: "no collision means the create failure stands as-is (AC-ERR)");
    response.Results[0].Status.Should().Be("failed");
    response.Results[0].CollisionInfo.Should().BeNull(because: "no same-name schema was found");
    stubManager.DidNotReceive().GetSchemaProperties(Arg.Any<GetEntitySchemaPropertiesOptions>());
    registrationService.DidNotReceive().EnsureLookupRegistration(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
}
```

### TC-U-05 (AC-03, FR-03) — durable collision, requested column MISSING → honest failure

```csharp
[Test]
[Category("Unit")]
[Description("A same-package collision where a requested column is missing (UsrColors/UsrHexCode) must NOT force success — returns success:false with the use-update-entity hint and does not register.")]
public async Task ExecuteCreateSchema_ShouldReturnFailureWithUpdateEntityHint_WhenRequestedColumnMissing() {
    // Arrange
    var logger = new TestLogger();
    var scriptedCreate = new ScriptedCreateEntitySchemaCommand(logger, Fail("Schema UsrColors already exists."));
    ILookupRegistrationService registrationService = Substitute.For<ILookupRegistrationService>();
    IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
    commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>()).Returns(scriptedCreate);
    commandResolver.Resolve<ILookupRegistrationService>(Arg.Any<EnvironmentOptions>()).Returns(registrationService);
    commandResolver.Resolve<FindEntitySchemaCommand>(Arg.Any<FindEntitySchemaOptions>())
        .Returns(new FakeFindEntitySchemaCommand([new EntitySchemaSearchResult("UsrColors", "UsrPkg", "Customer", "BaseLookup")]));
    // existing schema does NOT contain the requested UsrHexCode column → durable collision
    commandResolver.Resolve<GetEntitySchemaPropertiesCommand>(Arg.Any<GetEntitySchemaPropertiesOptions>())
        .Returns(ReadReturning(SchemaWith("Name", "Code")));
    SchemaSyncTool tool = new(commandResolver, logger, retryDelay: Substitute.For<IRetryDelay>());
    SchemaSyncArgs args = new("dev", "UsrPkg",
        [new SchemaSyncOperation("create-lookup", "UsrColors", TitleLocalizations: Localizations("Colors"),
            Columns: [new CreateEntitySchemaColumnArgs("UsrHexCode", "Text", Localizations("Hex code"))])]);

    // Act
    SchemaSyncResponse response = await tool.SchemaSync(args);

    // Assert
    response.Success.Should().BeFalse(because: "a confirmed durable collision must fail honestly, not force success (FR-03)");
    response.Results[0].Status.Should().Be("failed");
    response.Results[0].CollisionInfo!.Hint.Should().Contain("update-entity",
        because: "the caller must be told to add the columns via update-entity");
    registrationService.DidNotReceive().EnsureLookupRegistration(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    // Optional, if the impl names the missing column for actionability:
    // string.Join(" ", GetMessageValues(response.Results[0])).Should().Contain("UsrHexCode");
}
```

### TC-U-06 (AC-04, FR-05) — column read throws → resumed-existing + warning, not blind success

```csharp
[Test]
[Category("Unit")]
[Description("When the column-verification read throws (transient), the op degrades to status resumed-existing with success:true and a NOT-verified warning — never a plain completed.")]
public async Task ExecuteCreateSchema_ShouldDegradeToResumedExisting_WhenColumnReadThrows() {
    // Arrange
    var logger = new TestLogger();
    var scriptedCreate = new ScriptedCreateEntitySchemaCommand(logger, Fail("Schema UsrColors already exists."));
    ILookupRegistrationService registrationService = Substitute.For<ILookupRegistrationService>();
    IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
    commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>()).Returns(scriptedCreate);
    commandResolver.Resolve<ILookupRegistrationService>(Arg.Any<EnvironmentOptions>()).Returns(registrationService);
    commandResolver.Resolve<FindEntitySchemaCommand>(Arg.Any<FindEntitySchemaOptions>())
        .Returns(new FakeFindEntitySchemaCommand([new EntitySchemaSearchResult("UsrColors", "UsrPkg", "Customer", "BaseLookup")]));
    (GetEntitySchemaPropertiesCommand readCommand, _) = ReadThrowing(new HttpRequestException("No such host is known."));
    commandResolver.Resolve<GetEntitySchemaPropertiesCommand>(Arg.Any<GetEntitySchemaPropertiesOptions>()).Returns(readCommand);
    SchemaSyncTool tool = new(commandResolver, logger, retryDelay: Substitute.For<IRetryDelay>());
    SchemaSyncArgs args = new("dev", "UsrPkg",
        [new SchemaSyncOperation("create-lookup", "UsrColors", TitleLocalizations: Localizations("Colors"),
            Columns: [new CreateEntitySchemaColumnArgs("UsrHexCode", "Text", Localizations("Hex code"))])]);

    // Act
    SchemaSyncResponse response = await tool.SchemaSync(args);

    // Assert
    response.Success.Should().BeTrue(because: "an unverifiable read must not fail the resume (FR-05, OQ-01)");
    response.Results[0].Status.Should().Be("resumed-existing",
        because: "the distinct status flags that columns were NOT verified");
    string warnings = string.Join(" ", GetMessageValues(response.Results[0]));
    warnings.Should().Contain("NOT", because: "the warning must state the requested columns were not verified");
    registrationService.Received(1).EnsureLookupRegistration("UsrPkg", "UsrColors", "Colors");
}
```

### TC-U-07 (AC-06, FR-06) — resumed/forced-success result drops failed-create Error lines

```csharp
[Test]
[Category("Unit")]
[Description("A resumed success result carries only the fresh Info/Warning line — no Error-level messages carried over from the failed create attempt.")]
public async Task FinalizeResult_ShouldDropCreateErrorMessages_WhenResumed() {
    // Arrange
    var logger = new TestLogger();
    // The create fails with an ERROR-level message that must NOT surface in the resumed success result.
    var scriptedCreate = new ScriptedCreateEntitySchemaCommand(logger, Fail("Schema UsrColors already exists."));
    ILookupRegistrationService registrationService = Substitute.For<ILookupRegistrationService>();
    IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
    commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>()).Returns(scriptedCreate);
    commandResolver.Resolve<ILookupRegistrationService>(Arg.Any<EnvironmentOptions>()).Returns(registrationService);
    commandResolver.Resolve<FindEntitySchemaCommand>(Arg.Any<FindEntitySchemaOptions>())
        .Returns(new FakeFindEntitySchemaCommand([new EntitySchemaSearchResult("UsrColors", "UsrPkg", "Customer", "BaseLookup")]));
    commandResolver.Resolve<GetEntitySchemaPropertiesCommand>(Arg.Any<GetEntitySchemaPropertiesOptions>())
        .Returns(ReadReturning(SchemaWith("UsrHexCode"))); // verified resume
    SchemaSyncTool tool = new(commandResolver, logger, retryDelay: Substitute.For<IRetryDelay>());
    SchemaSyncArgs args = new("dev", "UsrPkg",
        [new SchemaSyncOperation("create-lookup", "UsrColors", TitleLocalizations: Localizations("Colors"),
            Columns: [new CreateEntitySchemaColumnArgs("UsrHexCode", "Text", Localizations("Hex code"))])]);

    // Act
    SchemaSyncResponse response = await tool.SchemaSync(args);

    // Assert
    response.Success.Should().BeTrue();
    response.Results[0].Messages.Should().NotContain(
        m => m.LogDecoratorType == LogDecoratorType.Error,
        because: "a completed/resumed success result must not carry failed-create Error lines (FR-06/AC-06)");
    string joined = string.Join(" ", GetMessageValues(response.Results[0]));
    joined.Should().NotContain("already exists", because: "the raw create error text must be dropped from the success result");
}
```

### TC-U-08 (AC-04-terminal) — resumed-existing does not stick if registration then fails

```csharp
[Test]
[Category("Unit")]
[Description("When the read is unverifiable (would degrade to resumed-existing) but the subsequent registration fails, the op is failed and resumed-existing does not stick.")]
public async Task ExecuteCreateSchema_ShouldFail_WhenRegistrationFailsAfterResumedExistingDegrade() {
    // Arrange
    var logger = new TestLogger();
    var scriptedCreate = new ScriptedCreateEntitySchemaCommand(logger, Fail("Schema UsrColors already exists."));
    ILookupRegistrationService registrationService = Substitute.For<ILookupRegistrationService>();
    registrationService
        .When(s => s.EnsureLookupRegistration("UsrPkg", "UsrColors", "Colors"))
        .Do(_ => throw new InvalidOperationException("Lookup registration failed."));
    IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
    commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>()).Returns(scriptedCreate);
    commandResolver.Resolve<ILookupRegistrationService>(Arg.Any<EnvironmentOptions>()).Returns(registrationService);
    commandResolver.Resolve<FindEntitySchemaCommand>(Arg.Any<FindEntitySchemaOptions>())
        .Returns(new FakeFindEntitySchemaCommand([new EntitySchemaSearchResult("UsrColors", "UsrPkg", "Customer", "BaseLookup")]));
    (GetEntitySchemaPropertiesCommand readCommand, _) = ReadThrowing(new HttpRequestException("No such host is known."));
    commandResolver.Resolve<GetEntitySchemaPropertiesCommand>(Arg.Any<GetEntitySchemaPropertiesOptions>()).Returns(readCommand);
    SchemaSyncTool tool = new(commandResolver, logger, retryDelay: Substitute.For<IRetryDelay>());
    SchemaSyncArgs args = new("dev", "UsrPkg",
        [new SchemaSyncOperation("create-lookup", "UsrColors", TitleLocalizations: Localizations("Colors"),
            Columns: [new CreateEntitySchemaColumnArgs("UsrHexCode", "Text", Localizations("Hex code"))])]);

    // Act
    SchemaSyncResponse response = await tool.SchemaSync(args);

    // Assert
    response.Success.Should().BeFalse(because: "status derives from the final post-registration execution (AC-04-terminal)");
    response.Results[0].Status.Should().Be("failed", because: "resumed-existing must NOT stick when registration then fails");
    response.Results[0].Error.Should().Contain("Lookup registration failed");
}
```

### TC-U-09 (AC-05, AC-05-no-verify, FR-09) — different-package collision regression guard

```csharp
[Test]
[Category("Unit")]
[Description("A collision resolving to a DIFFERENT package must not enter the resume branch: create runs once, Success==false, registration is not invoked, and no column-read is issued.")]
public async Task ExecuteCreateSchema_ShouldNotSkipCreateOrRegister_WhenCollisionInDifferentPackage() {
    // Arrange
    var logger = new TestLogger();
    var scriptedCreate = new ScriptedCreateEntitySchemaCommand(logger, Fail("Schema UsrColors already exists."));
    ILookupRegistrationService registrationService = Substitute.For<ILookupRegistrationService>();
    var stubManager = Substitute.For<IRemoteEntitySchemaColumnManager>();
    IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
    commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>()).Returns(scriptedCreate);
    commandResolver.Resolve<ILookupRegistrationService>(Arg.Any<EnvironmentOptions>()).Returns(registrationService);
    // collision.ExistingPackageName ("OtherPackage") != args.PackageName ("UsrPkg")
    commandResolver.Resolve<FindEntitySchemaCommand>(Arg.Any<FindEntitySchemaOptions>())
        .Returns(new FakeFindEntitySchemaCommand([new EntitySchemaSearchResult("UsrColors", "OtherPackage", "Customer", "BaseLookup")]));
    commandResolver.Resolve<GetEntitySchemaPropertiesCommand>(Arg.Any<GetEntitySchemaPropertiesOptions>())
        .Returns(new GetEntitySchemaPropertiesCommand(stubManager, Substitute.For<ILogger>()));
    SchemaSyncTool tool = new(commandResolver, logger, retryDelay: Substitute.For<IRetryDelay>());
    SchemaSyncArgs args = new("dev", "UsrPkg",
        [new SchemaSyncOperation("create-lookup", "UsrColors", TitleLocalizations: Localizations("Colors"),
            Columns: [new CreateEntitySchemaColumnArgs("UsrHexCode", "Text", Localizations("Hex code"))])]);

    // Act
    SchemaSyncResponse response = await tool.SchemaSync(args);

    // Assert
    response.Results[0].Success.Should().BeFalse(because: "a foreign-package collision is a genuine failure (FR-07)");
    scriptedCreate.Invocations.Should().Be(1, because: "create is invoked exactly once and never skipped");
    registrationService.DidNotReceive().EnsureLookupRegistration(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    stubManager.DidNotReceive().GetSchemaProperties(Arg.Any<GetEntitySchemaPropertiesOptions>()); // AC-05-no-verify
}
```

---

## Integration Tests (`clio.tests/`)

None. Per the ADR, the entire behavior is exercised through the `commandResolver` seam with substitutes;
there is no new file system / DB / IIS / K8s surface. No `[Category("Integration")]` tests are required.

---

## E2E Tests (`clio.mcp.e2e/SchemaSyncToolE2ETests.cs`) — NOT in CI (manual only)

> ⚠️ MCP E2E tests do NOT run in CI (project-context Testing Rules). Manual execution against a real
> Creatio environment only. Add to the PR checklist if implemented; optional per Story 1/2.

### TC-E-01 (mirrors AC-03) — durable collision fails honestly against a real environment

- **Tool**: `sync-schemas`
- **Setup**: a package already containing `UsrColors` (BaseLookup) WITHOUT a `UsrHexCode` column.
- **Input**: `{"environment":"<env>","package-name":"UsrPkg","operations":[{"type":"create-lookup","schema-name":"UsrColors","title-localizations":{"en-US":"Colors"},"columns":[{"column-name":"UsrHexCode","type":"Text"}]}]}`
- **Expected output**: `success:false`, `status:"completed"` absent; result carries the "schema already exists — use update-entity to add columns" hint; the lookup is NOT (re)registered.
- **⚠️ CI status**: NOT in CI — manual execution required.
- **Manual gate**: add to PR checklist.

### TC-E-02 (mirrors AC-04) — unverifiable read degrades to resumed-existing

- **Tool**: `sync-schemas`
- **Setup**: same-package collision on `UsrColors`; induce a transient failure of the column-read probe
  (e.g. network interruption during `get-entity-schema-properties`).
- **Input**: same as TC-E-01.
- **Expected output**: `status:"resumed-existing"`, `success:true`, a WarningMessage that the requested columns were NOT verified.
- **⚠️ CI status**: NOT in CI — manual execution required.
- **Manual gate**: add to PR checklist.

---

## Regression Guard

Real existing tests in `clio.tests/Command/McpServer/SchemaSyncToolTests.cs` that MUST stay green
(all touched by the `ExecuteCreateSchema` / `FinalizeResult` / `Classify` changes):

| Test file | Test name | Why at risk |
|-----------|-----------|------------|
| `SchemaSyncToolTests.cs` | `SchemaSync_CreateLookup_Should_Complete_Registration_When_Schema_Already_Exists_In_Target_Package` | The empty-columns resume — must keep passing via the FR-01 fast-path after the rework (DoD, Story 1) |
| `SchemaSyncToolTests.cs` | `SchemaSync_CreateLookup_Should_Include_CollisionInfo_When_Schema_Exists_In_Different_Package` | Foreign-package collision — asserts collision-info only; FR-07 guard must remain (superseded for guard-pinning by TC-U-09) |
| `SchemaSyncToolTests.cs` | `SchemaSync_CreateLookup_Should_Not_Include_CollisionInfo_When_Schema_Not_Found` | AC-ERR baseline: no collision → honest failure, resume branch not entered |
| `SchemaSyncToolTests.cs` | `SchemaSync_Should_Include_Detailed_Command_Error_When_Present` | Failed-create Error surfaced on `success:false` — FR-06 must NOT drop genuine failure diagnostics |
| `SchemaSyncToolTests.cs` | `SchemaSync_CreateLookup_Should_Fail_When_Lookup_Registration_Fails` | Registration-failure path feeds AC-04-terminal status derivation |
| `SchemaSyncToolTests.cs` | `SchemaSync_CreateLookup_Should_Route_Through_CreateEntitySchemaCommand` | Happy-path create + `Status=="completed"` classification unchanged |
| `SchemaSyncToolTests.cs` | `SchemaSync_Should_Assign_Messages_To_The_Correct_Operation` | Per-op message routing — resume message changes must not leak across ops (FR-06 adjacency) |
| `SchemaSyncToolTests.cs` | `SchemaSync_Should_Retry_Transient_Failure_And_Continue_Batch` | `RunAttempts`/`OperationExecution` reshaping in the resume branch must not break retry accounting |
| `SchemaSyncToolTests.cs` | `SchemaSync_Should_Fail_After_Exhausting_Retries_And_Emit_Resume_Plan` | Retry-exhaustion resume-plan; `execution` rewrite in the branch must not corrupt the plan |
| `SchemaSyncToolTests.cs` | `SchemaSync_RetryBudget_Should_Be_Shared_Between_Create_And_Registration` | Shared budget threads through the create→registration path the resume branch modifies |

---

## Coverage Estimate

| Layer | New tests | Modified tests | Notes |
|-------|-----------|---------------|-------|
| Unit | 9 | 0 | TC-U-01…09; extend existing fixture + new `GetEntitySchemaPropertiesCommand` seam + `SchemaWith`/`ReadReturning`/`ReadThrowing` helpers |
| Integration | 0 | 0 | Not applicable (resolver seam with substitutes) |
| E2E | 2 | 0 | Manual only — NOT in CI |

---

## Definition of Done for QA

- [ ] All TC-U-* implemented with `[Category("Unit")]` — NOT `[Category("UnitTests")]`
- [ ] No `[Category("Integration")]` tests needed (documented rationale)
- [ ] Regression-guard tests (table above) all green after the change
- [ ] `SchemaSync_CreateLookup_Should_Complete_Registration_When_Schema_Already_Exists_In_Target_Package` still passes via the FR-01 fast-path
- [ ] MCP E2E tests (TC-E-01/02) documented as manual-only and added to the PR checklist if implemented
- [ ] Test naming follows `MethodName_ShouldBehavior_WhenCondition`
- [ ] New `GetEntitySchemaPropertiesCommand` seam uses a real command + substituted `IRemoteEntitySchemaColumnManager` (no production `virtual` added)
- [ ] `docs/McpCapabilityMap.md` documents the additive `resumed-existing` status (Story 2)
- [ ] Code compiles with no CLIO001–CLIO005 analyzer warnings; PR references the story files
