# ADR: Business process versioning — read, create, set actual

**Status**: Proposed (revised after adversarial review 2026-09-02)
**Author**: Architect Agent
**PRD**: [prd-process-versioning.md](../prd/prd-process-versioning.md)
**Created**: 2026-09-02
**stepsCompleted**: [1, 2, 3, 4]

---

## Context

A Creatio process version is not a revision of one schema: each version is a distinct schema in a **flat** family whose `ParentSchemaUId` is always the root, never the previous version. `describe-business-process` resolves by schema Name and therefore answers for whichever version was named, while the runtime redirects execution to the active one — and the response has no field to reveal the difference (measured: `InvoiceVisaProcess` 3 parameters vs the active `InvoiceVisaProcessInvoice1` 7 parameters, same caption). There is also **no server API that creates a version**: the platform composes one entirely client-side (`base-process-schema-manager.js` — `copySchema`, `createNewSchemaVersion:84`, `setNewSchemaVersionName:33`) and persists it through an ordinary schema save; the only server-side clone, `BaseProcessSchemaManager.SaveClonedSchema:892`, is `protected` and deliberately **resets** versioning (`Version = 0`, `ParentSchemaUId = DefSchema.UId`, `IsActiveVersion = true`) — Copy, not Version.

## Decision

Read version facts **client-side in clio** from the platform's own process-library view, and add **two** new package operations — `ModifyProcessAsNewVersion` and `SetActiveProcessVersion` — surfaced as two MCP tools whose safety flags differ.

`ModifyProcessAsNewVersion` takes the same `operations[]` payload as `ModifyProcess` and applies it to a **clone** of the source, saving the result as a new inactive version: edits and version creation are one atomic gesture, so a rejected edit saves nothing at all and there is never an intermediate duplicate to clean up. The agent orchestrates `modify-as-new-version → set-active`; activation stays a separate, explicit operation.

## Alternatives Considered

| Option | Pros | Cons | Status |
|--------|------|------|--------|
| A: `saveAsNewVersion: true` on `ModifyProcessRequest` | one call, no new operation | No contract in the package implements `IExtensibleDataObject` (zero matches across `Files/src`), so an environment on an older package **silently drops** the unknown member and edits the live version while reporting success. The target stand ran 1.2.0.1 while clio bundled 1.3.1.1 — the divergence is normal, not hypothetical | **Rejected**: the failure is invisible and hits the primary use case |
| B: one `ModifyProcessAsNewVersion` operation (clone + apply edits + save) | matches the designer's own gesture exactly (it saves the **edited** schema as a version); no intermediate duplicate can exist; atomicity is free, because a rejected operation already aborts the whole edit and therefore saves no schema | a second entry point over the operation surface | **Chosen** — the divergence objection does not hold: the operation semantics live in `IProcessOperationExecutor`, not in the handler (choice 13) |
| C: a bare `CreateProcessVersion`, then `ModifyProcess` against the new version | each gesture separately auditable | between the two calls an exact duplicate of the source exists; if the edit then fails it is **unremovable**, because deleting a version cancels every `SysProcessLog` row of that schema | **Rejected**: it manufactures garbage the product has no way to clean up |
| C′: C plus a compensating removal of a version this build just created | would clear the duplicate | needs a third gated operation (a third route, tool and pin move), and its headline promise is unenforceable server-side — "created in this session" is not a predicate the package can verify, so the guards would also admit someone else's inactive version | **Rejected** |
| D: clio calls the platform's native `SetIsActualVersion` | no package change at all | that endpoint has **no authorization check** — `BaseProcessSchemaManagerService.cs:56-66` and `SetActiveVersionItem:1317-1328` contain none, and it is reachable as a single `POST …/SetIsActualVersion?schemaUId={uid}` (`IProcessSchemaManagerService.cs:79-82`, `BodyStyle = Bare`) | **Rejected**: ships an ungated privileged write |
| E: read version facts server-side in `CrtProcessBuilder` | one authority; the manager's answer is the runtime's answer | needs a rebundle and pin moves for a read; the view answers today in tens of milliseconds | **Deferred**, wire field names chosen so the source can move without changing the contract |
| F: a separate `list-business-process-versions` read tool | smaller describe response | a new tool costs a classification row, a capability-map entry, docs, guidance and an E2E fixture — and the defect is only cured when the versions are visible **in the same answer** as the graph | **Rejected**: `versions[]` is a member of the describe response |
| G: make `get-process-signature` version-aware in this ticket | closes the last known version-blind read | it is a public `[Verb]`, so it drags help, docs, `Commands.md` and `WikiAnchors` and widens an already 9-10 day ticket | **Rejected**: recorded as a PRD non-goal with a follow-up |

## Key design choices recorded

1. **Vocabulary copies the platform's own split**: `Active` in identifiers, "actual" in human-facing text. `ProcessLibrary-7.8.0/Resources/ProcessVersionsDetail.ClientUnit/resource.en-US.xml` names the menu item `SetActiveVersionMenuCaption` with the caption "Set as actual version". Tool descriptions carry one bridging sentence.
2. **`activate-*` is not used.** The platform already owns `EnableProcess`/`DisableProcess` and `VwProcessLib.Enabled`, which switch a process on and off **for the whole family**. `set-active-…` has no such ambiguity.
3. **Saving a new version never activates it.** Activation stays a separate operation: it is the highest-blast-radius write in the toolkit — it changes what the whole environment executes — and it must never happen as a side effect of an edit. With the edits and the version now saved together, a combined flag would at least be coherent; it is still refused, because the user's two questions ("new version or in place?" and "make it actual?") are answered at different moments and one may be answered no.
4. **Activation verifies by reading back.** `BaseProcessSchemaManager.cs:519` logs and swallows the deactivation failure of every sibling it turns off, which can leave two members flagged active with `PackagePosition` deciding the winner. The operation re-reads through the **manager** (not the view — the manager's cache is invalidated by the event this path raises) and returns `success:false` when the active member is not the requested one. The read-back is a fresh query in the same call; no retry loop, because a stale answer here would oscillate.
5. **The read half reports the view's answer, and says so.** The view ranks by `PackageLevel`, the manager by `PackagePosition` (or package `HierarchyLevel` under `UseNewSchemaHierarchyFolding`, on by default), and the runtime consults the manager. Both agree while the explicit `IsActiveVersion` flag discriminates — verified on three families including two cross-package ones — but a family tying on the first two ordering keys can diverge. The response therefore carries `activeVersionSource: "process-library-view"`, and the DTO doc comment says what that means.
6. **Absent means NOT ESTABLISHED, and a failure says so.** `JsonIgnoreCondition.WhenWritingNull` (`DescribeProcessCommand.cs:43-46`) drops null members. Nullability, corrected against the model file: `Version` → `int?` (`VwProcessLib.cs:76-77`) and `IsActiveVersion` → `bool?` (`:91-92`), because both come from subqueries against `VwProcessSchemaInfo`, which INNER JOINs `SysPackage` and yields NULL when the package does not resolve; and `ParentId` (`[SchemaProperty("Parent")]`, `:34-35`) → `Guid?`, because the raw passthrough is NULL for every root. **`VersionParentUId` stays `Guid`** — it is `COALESCE([PS].[UId], [SS].[UId])`, never NULL, and measured equal to the root's own UId. `SysWorkspaceId` / `SysPackageId` do not exist in the model and would be **new** properties if the read is narrowed to the current workspace; the decision is to read workspace-agnostically and record why, because the tool already targets one environment. An unversioned process reports `version: 0` (a real fact); an unestablished read reports no members **plus** `versionReadWarning`, so the two are distinguishable.
7. **`IsMaxVersion` is not surfaced.** `VwProcessLibMSSql.sql:41-64` compares a character `MAX` over a pool that includes a root's children but excludes a child's root; two versions of one root created in different packages both get `Version 1` and both report `true`, and from version 10 the lexicographic max is wrong.
8. **Version identity is never inferred from a name.** Only two of the twelve stock families follow `<root><PackageName><N>`; the rest are hand-named. The formula applies solely to versions this build creates.
9. **`versionRootSchemaUId`, not `rootSchemaUId`.** The short name already means the root of a page's inheritance chain in nine live places (`ClassicListColumnResolver.cs:193-200`, `GetClassicPageSourcesCommand.cs:590-596`, `GetPageHierarchyCommand.cs:335-340`).
10. **Handlers are named `ProcessVersion<Verb>Handler`**, deviating from the package's `Process<Verb>Handler` convention because the object of the operation is a version.
11. **Session policy lives in guidance.** MCP tools are stateless; "keep creating versions for the rest of this session" is agent behaviour expressed in the `process-versions` article and the prompt, never a field in a request.
12. **DI, stated per artefact — this replaces the earlier blanket "BindingsModule is untouched".** The reader *interface* needs no explicit registration: `RegisterAssemblyInterfaceTypes` (`BindingsModule.cs:233`) auto-registers `Clio`-namespace interfaces, and `IProcessDescriber` has no explicit line anywhere; confirm only that the scan's skip-list does not catch it. The two new **commands do** need explicit registration next to their siblings — `CreateBusinessProcessCommand` at `:425`, `ModifyBusinessProcessCommand` at `:427`, `DescribeProcessCommand` at `:1031` — so `BindingsModule.cs` **is** edited by the write half. That makes the write-half stories a full-suite regression trigger (`AGENTS.md:381`) and forces the full three-lens review; the read-half stories keep the targeted filter.
13. **One applier, two entry points — and the reuse is literal, not aspirational.** `ProcessModifyHandler` does not implement operation semantics: it resolves the schema, opens a design session, and then delegates each descriptor to `IProcessOperationExecutor.Apply(schema, lane, operation)` before running `_layoutEngine.Apply`, `schema.InitializeLocalizableValues()` and `_schemaValidator.EnsureValidForSave` (`Design/ProcessModifyHandler.cs:55-110`, executor at `Operations/ProcessOperationExecutor.cs`). The middle of that pipeline — primary lane, executor loop, layout, localizable values, pre-save validation — is identical whatever schema is being edited. `ModifyProcessAsNewVersion` therefore differs from `ModifyProcess` in exactly two places: it edits a **clone** rather than a design instance of the source, and it persists that clone with the version properties set. Extract the shared middle so both handlers call one implementation, and pin it with a test asserting both paths run the same executor. This is what makes a second entry point safe: the 14 operation semantics have one home, and a new operation reaches both paths at once.

    One caveat found by review and worth stating where the reuse is claimed: `InitializeLocalizableValues()` is only safe to share because choice 17 removes the aliasing that would otherwise make it rebind the **source** schema's `Group` binding. Sharing the pipeline is sound; sharing it over an object-graph clone was not.
14. **Version numbers come from the platform's allocator, and the allocation is re-verified.** `GetMaxProcessVersionInPackage(conn, rootItem.Id, packageUId)` allocates (**`rootItem.Id` is the `SysSchema.Id`, not the UId**); the toolkit never computes a number itself and never asserts one client-side. Because two concurrent creates both read `max = N`, the operation re-reads the family after its save and returns `success:false` naming the observed number when the number it wrote is no longer unique. Schema writes are also serialized by operational necessity — a parallel burst trips IIS rapid-fail and downs the .NET Framework app pool.
15. **File-design mode.** In FSD a process is absent from `VwProcessLib` until an FS→DB load and publish, so the read half degrades to absent members plus the warning (choice 6) rather than reporting "unversioned". The write half is not supported under FSD in this build: the create path saves through the DB schema manager, and the operation refuses with a message naming the mode rather than writing a schema the file system will overwrite.
16. **The version floor travels with the archive.** Both new options classes carry a **versioned** `[RequiresPackage]`, not a presence-only one — presence-only fails open with a transport 404. But `BundledArchive_ShouldCarryAtLeastEveryDeclaredRequirement` (`clio.tests/Common/BundledProcessBuilderPackageTests.cs:633`) fails when a declared floor exceeds the bundled archive's version, so the floor and the rebundle **must land in the same change**; the tracker carries that dependency edge.

17. **The clone is produced by a metadata round-trip, never by the object-graph `Clone()`.** Serialize the source's metadata, replace the source schema's UId throughout, **then repair `CreatedInOwnerSchemaUId` (`BL8`) back to the original**, and materialise the result with `ReadMetaData`. The traps write-up forbade the blunt whole-UId replace precisely because it rewrites `BL8` and turns the version into a copy; repairing that one key afterwards removes the objection and buys three things the typed walk cannot:

    - **The object-graph clone shares mutable state with a live, app-cached instance.** `ProcessSchemaBaseElement`'s copy constructor copies the back-pointer verbatim (`ProcessSchemaBaseElement.cs:86`, `ProcessSchema = source.ProcessSchema`). The collection's `InsertItem` would repair it (`ProcessSchemaFlowElement.cs:297-302`) but only when `ParentMetaSchema` is already known — and `ProcessSchema.cs:149-153` assigns the parent in an **object initializer**, which runs *after* the constructor body that already added every cloned element, so the repair never fires and `MetaItemCollection.cs:97` sets the parent to `null`. Resolution then lands on the source (`ProcessSchemaBaseElement.cs:101`), and it is a **write**, not a read: a sequence flow's `SourceRefUId` setter does `ProcessSchema.GetBaseElementByUId(...).Outgoings.Remove(this)` then `.Add(this)` (`ProcessSchemaSequenceFlow.cs:143-152`). Because the source instance is an app-level cached singleton (`Manager.cs:320-329`, and `ProcessSchemaRepository.LoadForDescribe:141` prefers exactly that path), `addFlow` / `removeFlow` / `removeElement` on the clone would corrupt the **running** process's in-memory graph for every later request — while its database row stays byte-for-byte identical, which is precisely what the acceptance criteria check.
    - **`Group` is aliased outright.** `ProcessSchema.cs:143` does `Group = source.Group`, whose setter is `_group = LocalizableString.Merge(_group, value)`, and `LocalizableString.Merge` returns `source` unchanged when the target is null (`Terrasoft.Common/LocalizableString.cs:270-272`) — so the clone's `_group` **is** the source's object. `InitializeLocalizableValues()` then rebinds that shared object's resource info to the clone's UId (`ProcessSchema.cs:1412`), pointing the source process's Group caption at the clone's empty resources. `Caption` and `Description` are safe only because `Schema.cs:122-123` wraps them in `new LocalizableString(...)`; `Group` is the one that does not.
    - **The typed `GetMetaItems` walk was never parity anyway** (it enumerates neither schema `Parameters` nor `ExecutionContexts`), and the collection walkers are `internal` with `Terrasoft.Configuration` absent from the friendly assemblies. A metadata replace catches `A3`, `A4` and `GS5` together and produces no shared object references at all.

    The platform's own verdict is already in its code: `BaseProcessSchemaManager.CreateSchemaCopy:929-942` calls `Clone()` and then throws that content away in favour of `CloneSchemaUsingMetaData:532-555`. Follow it.

18. **The clone is never opened as a design instance.** The in-place path edits through `OpenDesignSession` + `GetDesignInstance` and persists with `SaveEdited`, which requires a schema that already exists in `SysSchema`; a design session is a serialize/deserialize round-trip through `SessionData` (`SchemaManager.cs:5354-5378`) that survives only inside one request. On an unsaved UId those routes fail badly rather than cleanly: `DesignSchema` dereferences a null design item (`SchemaManager.cs:5679-5713` → `:5342-5347`) and `SaveSchema(Guid, …)` throws `ItemNotFoundException` (`:5240-5248`). The only viable route is `SaveSchema(item, …)` (`:4841-4848`), which needs no session — so the handler applies the operations to the materialised clone in memory and saves the item directly. That is also what keeps FR-16's atomicity true: nothing is persisted until that one save.

    Two gates must be cleared **before** it: the clone starts life carrying the source's `Name` (`MetaItem.cs:108` copies it verbatim), so the rename has to precede both `CheckIsValidSchemaName` (`InternalSaveSchema:2701`) and `SchemaDuplicationDetector.CheckDuplicateSchemaNameExists`, which throws `InvalidNameException` on a case-insensitive name match with a different UId (`SchemaManager.cs:941-945`).

19. **The response's version facts come from a post-save re-read, not from the in-memory instance.** Both production copy paths persist and then re-read through metadata — `ProcessSchemaManager.cs:560-596`, and `GetCopiedSchemaInstance:271-275` forces `ForceUseInstanceFromMetaData = true` to guarantee it. `versionSchemaUId`, `versionName`, `version` and `isActiveVersion` are therefore reported from that re-read, which doubles as the collision check choice 14 requires.

20. **A false save is not an exception.** `SchemaManager.SaveSchema` returns `false` **without committing and without throwing** when `GenerateSchemaSources` fails (`:1666-1685`) — the in-place path already handles that branch explicitly (`ProcessModifyHandler.cs:90-97`). Both new handlers must handle the non-throwing false as well as the throw, and the rollback guard needs fixing while we are there: `ProcessSchemaRepository.cs:82` guards with `FindItemByUId`, which searches `Items`, while the save path registers with `Add(item)` and is looked up through `FindItemByRealUId` over `AllItems` (`Manager.cs:181-187`, `SchemaManager.cs:2596-2621`), so a rolled-back clone can fall between the two collections.

## Implementation Plan

### Files to create

| File | Purpose |
|------|---------|
| `clio/Command/ProcessModel/IProcessVersionLibReader.cs` | interface + implementation (house style, cf. `IProcessDescriber.cs`) |
| `clio.tests/Command/ProcessModel/ProcessVersionLibReaderTests.cs` | unit tests, `[Property("Module","ProcessModel")]` |
| `<pb>/Files/src/cs/Design/ProcessVersionSaveHandler.cs` | metadata-round-trip clone (choice 17) + rename + apply the edits through the shared applier + allocate + save the item directly (choice 18) + post-save re-read (choice 19) |
| `<pb>/Files/src/cs/Design/ProcessVersionActivateHandler.cs` | guard → `SetActiveVersionItem` → read-back verify |
| `<pb>/Files/src/cs/Contracts/VersionContracts.cs` | the request/response contracts below |
| `<pb>/tests/CrtProcessBuilder/ProcessVersionSaveHandlerTests.cs`, `…ActivateHandlerTests.cs` | `[TestFixture(Category = "UnitTests")]` — that repo's convention |
| `clio/Command/McpServer/Tools/ProcessDesigner/ModifyProcessAsNewVersionTool.cs`, `SetActiveProcessVersionTool.cs` | the two MCP tools |
| `clio/Command/ModifyProcessAsNewVersionCommand.cs`, `SetActiveProcessVersionCommand.cs` | `Command<TOptions>` + options carrying the versioned `[RequiresPackage]` |
| `clio-knowledge`: `guidance/mcp/guides/processes/versions.md` | guidance item `process-versions` |

### Files to modify

| File | Change description |
|------|-------------------|
| `clio/Command/ProcessModel/IProcessDescriber.cs` | version members on `DescribeProcessResult` (after `SchemaUId`, `:144-145`), `DescribedProcessVersion`, `ServerProcessDescriber` takes the reader as a 4th primary-constructor parameter and fills them at `return result;` (`:86`) |
| `clio/CreatioModel/VwProcessLib.cs` | `Version` → `int?`, `IsActiveVersion` → `bool?`, `ParentId` → `Guid?` (choice 6). The model declared `MetaData byte[]` and `MetaDataModifiedOn`, which ATF would put in the select of every query over the process library. **Mechanism as implemented in story 1** (better than the narrow model type this ADR first specified, and simpler): the two columns are **removed from the shared model** — no caller had ever read them, verified across `clio`, `clio.tests` and `clio.mcp.e2e`. That needs no parallel model type (which would have been the repository's first duplicated `[Schema]` and its first class-name/schema-name mismatch), and it narrows the two pre-existing queries as well — `IProcessDescriber.cs:99` and `ProcessModelGenerator.cs:85-88` were both carrying the blob on every caption-addressed call. A comment on the model records why the columns must not come back |
| `clio/Common/ServiceUrlBuilder.cs` | **two new `KnownRoute` values (67, 68) plus their `KnownRoutes` entries** — `"/rest/ProcessDesignService/ModifyProcessAsNewVersion"` and `"/rest/ProcessDesignService/SetActiveProcessVersion"`, **with the leading slash**, as all five sibling `/rest/ProcessDesignService/` entries at `:315-322` have. clio reaches the package only through this map (`DescribeProcess = 51` at `:175`) |
| `clio/BindingsModule.cs` | `services.AddTransient<ModifyProcessAsNewVersionCommand>()` and `…<SetActiveProcessVersionCommand>()` next to `:425`/`:427`/`:1031` (choice 12) |
| `clio/Command/ProcessModel/IProcessDescriber.cs` (`:99`) | caption resolution routed through `ProcessLibResolver`, filtered to the active version; when the filter empties the candidate set, fall back to the existing ambiguity error rather than picking one |
| `clio/Command/ProcessModel/ProcessModelGenerator.cs` (`:84-88`), `clio/Command/ProcessModel/ProcessLibResolver.cs` (`:13`) | the same active-version filter, applied **before** the ambiguity check |
| `clio/help/en/generate-process-model.txt`, `clio/docs/commands/generate-process-model.md`, `clio/Commands.md`, `clio/Wiki/WikiAnchors.txt` | the behaviour of a public `[Verb]` changes (PRD CLI Impact) |
| `DescribeProcessTool.cs` (`[Description]`, `:28`) | version paragraph before the "Identify the process by exactly one of…" sentence. Do **not** restore `[FeatureToggle]` — its absence is pinned by `ProcessDesignerGoLiveTests.cs:62` |
| `RunProcessTool.cs` | one sentence: it starts the **active** version |
| `DescribeProcessPrompt.cs` (`:31-37`) | read `isActiveVersion` first; if false, re-describe `activeVersionName` |
| `docs/McpCapabilityMap.md` (`:744`) | describe row gains the members and the caveat; two new tool rows |
| `clio.tests/Command/ProcessModel/ServerProcessDescriberTests.cs` (`:26-31`) | `CreateDescriber` overload taking the reader — first edit, a deliberate compile break |
| `clio.tests/Command/DescribeProcessCommandTests.cs` | assertions on the **serialized** output via capture-then-assert (`:132`, `:176` → `:139-144`) |
| `clio.tests/Command/ProcessLibResolverTests.cs` | the active-version filter cases. Note the asymmetry: the **fixture** is at `clio.tests/Command/`, while the **source** is at `clio/Command/ProcessModel/ProcessLibResolver.cs` |
| `clio.mcp.e2e/DescribeProcessToolE2ETests.cs` | versioned-family cases against the stock `InvoiceVisaProcess`; keep `[Category(McpE2ECategories.ProcessDesigner)]` — `ProcessDesignerE2EGate` was deleted |
| `clio.tests/Command/McpServer/PassthroughToolClassificationRegistry.cs` | one row per new tool, placed with its own tool family rather than beside describe's row |
| `clio.tests/Command/ProcessDesignerRequiresPackageAttributeTests.cs` | the new `[TestCase]`s join the **versioned** requirement list, and the class `[Description]` counts the options classes |
| `clio.tests/Common/BundledProcessBuilderPackageTests.cs` | operation count **+2** and authorization-gate call sites **+2** relative to the baseline at merge time (today 5 and 3 at `:266`/`:254`; two other stories rebundle the same package first), plus archive version/SHA/stamp |
| `<pb>/Files/src/cs/EntryPoints/WebService/ProcessDesignService.cs` (after `:122-129`) | the new `[OperationContract]`s, thin transport only |
| `<pb>/Files/src/cs/Design/ProcessDesigner.cs` | the facade/`IProcessDesigner` gains the new use cases |
| `<pb>/Files/src/cs/Design/ProcessModifyHandler.cs` | the shared middle of its pipeline (`:55-110`) extracted so the new handler calls one implementation — see choice 13 |
| `<pb>/Files/src/cs/Schema/ProcessSchemaRepository.cs` | a clone-materialising method (choice 17), a session-free save (choice 18), and the `Rollback` guard at `:82` corrected to look the item up the way the save path registers it (choice 20) |
| `<pb>/Files/src/CrtProcessBuilderApp.cs` | `AddScoped` registrations for both handlers, through the `Connection(sp)` helper — the container validates scopes |
| `<pb>/tests/…/ProcessDesignServiceWireContractTests.cs` (`:113`) | expected operation count **+2**, in the same commit as the guard calls |
| `<pb>/docs/process-builder-architecture.{md,puml}` | the `.puml` first has to match today's five-dependency `ProcessDesigner`; six of nine drawn edges are already false |
| `<pb>/docs/bundling-into-clio.md` (`:122`) | replace the deleted `BundledPackages.ProcessBuilderVersion` with `ExpectedArchiveVersion` |
| `clio/docs/commands/install-process-builder.md` (`:24-27`), `clio/help/en/install-process-builder.txt` (`:16-19`) | the gated tool family gains two entries |
| `docs/knowledge/ProcessModel/describe-business-process-reads-the-base-version-not-the-active-one.md` | narrowed: the by-name half stays true, the "no field to reveal it" half is removed |
| `clio.tests/Command/McpServer/Fixtures/curated-knowledge-names.json` | re-pin to the published generation and add the process article names the fixture does not know |

`<pb>` = `crt-process-builder` (`packages/CrtProcessBuilder`).

### Key interfaces / contracts

```csharp
// clio — reads version facts from the platform's own process-library view.
// Returns null when they cannot be established; null means NOT ESTABLISHED, never "version 0".
public interface IProcessVersionLibReader {
    ProcessVersionFacts Read(string schemaUId);
}

public sealed class ProcessVersionFacts {
    public int?   Version { get; init; }
    public bool?  IsActiveVersion { get; init; }
    public string ActiveVersionSchemaUId { get; init; }
    public string ActiveVersionName { get; init; }
    public string VersionRootSchemaUId { get; init; }
    public IReadOnlyList<DescribedProcessVersion> Versions { get; init; }
    public string ActiveVersionSource { get; init; }  // "process-library-view" (FR-19)
    public bool   FamilyTruncated { get; init; }   // the 50-member cap was applied
    public string Warning { get; init; }           // why the facts could not be established
}
```

```csharp
// clio — additive members on the PUBLIC result type only. Never on the private wire subclass:
// DescribeProcessCommand serializes by the static type and drops subclass members.
[JsonPropertyName("version")]                public int?   Version { get; set; }
[JsonPropertyName("isActiveVersion")]        public bool?  IsActiveVersion { get; set; }
[JsonPropertyName("activeVersionSchemaUId")] public string ActiveVersionSchemaUId { get; set; }
[JsonPropertyName("activeVersionName")]      public string ActiveVersionName { get; set; }
[JsonPropertyName("versionRootSchemaUId")]   public string VersionRootSchemaUId { get; set; }
[JsonPropertyName("activeVersionSource")]    public string ActiveVersionSource { get; set; } // "process-library-view"
[JsonPropertyName("versionsTruncatedAt")]    public int?   VersionsTruncatedAt { get; set; } // length of the published list, when the cap applied
[JsonPropertyName("versionReadWarning")]     public string VersionReadWarning { get; set; }
[JsonPropertyName("versions")]               public List<DescribedProcessVersion> Versions { get; set; }

/// <summary>One member of a process's version family, as the process-library view reports it.</summary>
public sealed class DescribedProcessVersion {
    [JsonPropertyName("schemaUId")]       public string SchemaUId { get; set; }
    [JsonPropertyName("name")]            public string Name { get; set; }
    [JsonPropertyName("caption")]         public string Caption { get; set; }
    [JsonPropertyName("version")]         public int?   Version { get; set; }
    [JsonPropertyName("isActiveVersion")] public bool?  IsActiveVersion { get; set; }
    [JsonPropertyName("isRoot")]          public bool   IsRoot { get; set; }
    [JsonPropertyName("packageUId")]      public string PackageUId { get; set; }
    /// <summary>FAMILY state, not per-version — EnableProcess keys on the ROOT SysSchema Id.</summary>
    [JsonPropertyName("enabled")]         public bool   Enabled { get; set; }
}
```

```csharp
// package — new operation names, never new fields on ModifyProcessRequest.
[OperationContract] ModifyProcessAsNewVersionResponse ModifyProcessAsNewVersion(ModifyProcessAsNewVersionRequest request);
[OperationContract] SetActiveProcessVersionResponse SetActiveProcessVersion(SetActiveProcessVersionRequest request);

[DataContract] public class ModifyProcessAsNewVersionRequest {
    [DataMember(Name = "name")]        public string Name { get; set; }        // one of name / uid — the SOURCE
    [DataMember(Name = "uid")]         public string Uid { get; set; }
    [DataMember(Name = "packageName")] public string PackageName { get; set; } // optional; see OQ-01
    /// <summary>The same descriptors ModifyProcessRequest takes. Empty or absent = a pure snapshot.</summary>
    [DataMember(Name = "operations")]  public List<ProcessOperationDescriptor> Operations { get; set; }
}
[DataContract] public class ModifyProcessAsNewVersionResponse {
    [DataMember(Name = "success")]              public bool   Success { get; set; }
    [DataMember(Name = "errorMessage")]         public string ErrorMessage { get; set; }
    [DataMember(Name = "versionSchemaUId")]     public string VersionSchemaUId { get; set; }
    [DataMember(Name = "versionName")]          public string VersionName { get; set; }
    [DataMember(Name = "version")]              public int    Version { get; set; }   // platform-allocated
    [DataMember(Name = "isActiveVersion")]      public bool   IsActiveVersion { get; set; } // always false
    [DataMember(Name = "versionRootSchemaUId")] public string VersionRootSchemaUId { get; set; }
    [DataMember(Name = "appliedOperations")]    public int    AppliedOperations { get; set; } // as ModifyProcessResponse reports
    [DataMember(Name = "warnings")]             public List<string> Warnings { get; set; } // as ModifyProcessResponse reports (List, not a single string)
}
[DataContract] public class SetActiveProcessVersionRequest {
    [DataMember(Name = "name")] public string Name { get; set; }  // one of name / uid — the VERSION
    [DataMember(Name = "uid")]  public string Uid { get; set; }
}
[DataContract] public class SetActiveProcessVersionResponse {
    [DataMember(Name = "success")]                  public bool   Success { get; set; }
    [DataMember(Name = "errorMessage")]             public string ErrorMessage { get; set; }
    [DataMember(Name = "activeVersionSchemaUId")]   public string ActiveVersionSchemaUId { get; set; } // read back
    [DataMember(Name = "activeVersionName")]        public string ActiveVersionName { get; set; }
    [DataMember(Name = "deactivationFailureCount")] public int    DeactivationFailureCount { get; set; }
    [DataMember(Name = "warnings")]                 public List<string> Warnings { get; set; } // e.g. a large family re-saved
}

// package — read the version through the INTERFACE member; GetVersion / GetIsActiveVersion(Guid) /
// GetActiveVersionItemByUId are public but NOT virtual and cannot be stubbed.
item.FindPropertyValue(x => x.Version, 0)
// Pre-validate every caller-supplied UId with FindItemByUId; GetItemByUId THROWS.
```

### Tool specification

| Tool | Args | Flags |
|------|------|-------|
| `modify-business-process-as-new-version` | exactly one of `process-name` / `process-uid` (the source), `operations` (the same descriptors `modify-business-process` takes; empty = snapshot), optional `package-name`, `environment-name` | `ReadOnly=false, Destructive=false, Idempotent=false, OpenWorld=false` — **not** destructive, because it leaves the source version untouched |
| `set-active-business-process-version` | exactly one of `version-name` / `version-uid`, `environment-name` | `ReadOnly=false, Destructive=true, Idempotent=true, OpenWorld=false` |

Tool names are `internal const` on the tool class, visible to the E2E project — never duplicated as literals in tests. `[RequiresPackage]` placement follows the tool's shape, not a blanket rule: a `BaseTool<T>` derivative carries it on the options type, a standalone tool calling `EnsureRequirements(args)` carries it on the args record — match the shape of the tool as written and add the `[TestCase]` to the matching list in `ProcessDesignerRequiresPackageAttributeTests`. The destructive tool owns its own timeout contract and must not route through the 120 s read deadline (`clio/Command/McpServer/AGENTS.md`). Both tools get a `McpCoreToolProfile` decision (resident vs long-tail) and a ClioRing MCP-compatibility verdict.

### Test strategy

| Layer | Framework | What to cover | File |
|-------|----------|--------------|------|
| Unit (clio) | NUnit 4 + FluentAssertions + NSubstitute, `[Category("Unit")]` | reader selection/ordering/cap/warning, null-degradation, active-version filtering and its empty-set fallback, serialized-output assertions, tool metadata pins | `clio.tests/Command/ProcessModel/…`, `clio.tests/Command/ProcessLibResolverTests.cs`, `clio.tests/Command/DescribeProcessCommandTests.cs` |
| Unit (package) | `[TestFixture(Category = "UnitTests")]` | handler guard, shared-applier delegation, atomic abort on a rejected operation, allocation via the manager, explicit inactive flag, rollback, read-back verdict | `crt-process-builder/tests/CrtProcessBuilder/…` |
| Integration | `[Category("Integration")]`, `[Explicit]` + self-ignore + `[NonParallelizable]` | two consecutive saves numbering 1 then 2; a rejected edit leaving the family unchanged; activation read-back | `clio.tests/…` against a dedicated sandbox |
| E2E | `clio.mcp.e2e` | describe against the stock versioned family; save-as-new-version and set-active behind the destructive opt-in | `clio.mcp.e2e/…` |

MCP E2E is an **advisory, non-blocking** CI check, path-filtered, and its build can be superseded by a later push; the process-designer fixtures additionally need `CrtProcessBuilder` installed. They do run locally in about a minute. Anything load-bearing is mirrored at unit level. Rows feeding the reader go through `ATF.Repository.Mock`'s items mock (an instance API returning an items mock — check a current call site, e.g. `ApplyEnvironmentManifestCommandTests.cs:62,71`); a bare `Substitute.For<IDataProvider>()` cannot return rows. Never `[Category("UnitTests")]` in clio. Never `catch (Exception)` in clio.

## Consequences

- **Positive**: the describe answer stops being confidently wrong on 12 stock families; the builder can iterate on a live process without stopping its instances, and can roll back; three version-blind paths are closed; the read half costs no rebundle. A rejected edit now saves nothing — there is no intermediate duplicate, no unremovable garbage, and no compensating delete to reason about.
- **Trade-offs**: a second entry point over the operation surface, safe only while both handlers share one applier (choice 13) — the pinning test is load-bearing; a clone mechanism that has to be the metadata round-trip rather than the obvious `Clone()`, for reasons that are invisible to any assertion over persisted rows (choice 17); the read half reports the process library's verdict until the read moves server-side; the write half moves two manual pins, edits `BindingsModule.cs` (full-suite regression + full review), adds two `KnownRoute` values, and forces a rebundle whose version stamp must be sequenced against other in-flight stamps.
- **Breaking change**: **Behavioural, not contractual.** No tool is renamed or removed and every response member is additive, but two resolution paths change outcome for versioned families: `describe --process-caption` picks the active version instead of an arbitrary member, and `generate-process-model` now succeeds where it used to fail with a caption-ambiguity error. Both are stated in the PRD's CLI Impact table; `RELEASE.md` carries the `generate-process-model` note.

## Pre-implementation Checklist

- [ ] All new tool names and flags are kebab-case
- [ ] Two `KnownRoute` values and their route strings added — clio has no other way to reach the package
- [ ] Both new commands registered in `BindingsModule.cs`; the reader interface left to the auto-scan, and the skip-list checked
- [ ] Versioned `[RequiresPackage]` floor on both options classes, landing in the same change as the rebundle
- [ ] No `catch (Exception)` in clio code paths
- [ ] `[RequiresPackage]` placed per tool shape and its `[TestCase]` added to the matching list
- [ ] `PassthroughToolClassificationRegistry` row and a `McpCoreToolProfile` decision per new tool
- [ ] Both modify paths run the same `IProcessOperationExecutor`, pinned by a test — a new operation must reach both at once
- [ ] The clone is materialised through metadata, `BL8` repaired, and the source instance's `Outgoings`/`Incomings` counts and `Group` resource binding asserted unchanged **in memory**, not only on disk
- [ ] The clone is renamed before the save, and the non-throwing `false` save branch is handled
- [ ] ClioRing MCP compatibility verdict recorded for both new tools
- [ ] `docs/McpCapabilityMap.md` updated in the same commit as the tool descriptions
- [ ] Knowledge records whose `applies-to` names a touched file updated or deleted in the same PR
- [ ] Operation-contract count and authorization-gate call sites moved together, on both sides, expressed as deltas from the baseline at merge time

## Notes

This ADR was revised after a five-agent adversarial review (three lenses per artifact plus a pipeline-mechanics pass) that returned BLOCK on its first revision. The review's own record belongs in `spec/reviews/`. Corrections it forced, kept here so they are not re-litigated: the missing `KnownRoute` values; the false "BindingsModule is untouched" claim; the wrong `ProcessLibResolverTests` path; the nullability list (`ParentId`, not `VersionParentUId`); the unprojected `MetaData` column invalidating the latency figure; and the version floor's dependency on the rebundle.

A third review round (2026-09-02) then reversed the write half from a bare create plus a compensating delete to the single `ModifyProcessAsNewVersion` operation recorded above (alternatives B, C and C′), and found the two aliasing defects that forced choice 17. That round's finding is the one most worth re-reading before touching the clone: the object-graph clone shares mutable state with a **live, app-cached** source instance, and both headline safety properties fail in memory while the source's database row stays byte-for-byte identical — the exact thing the acceptance criteria assert.

Three details were settled while implementing story 2 and are recorded so they are not re-decided. `versionsTruncatedAt` reports the length of the list actually published rather than the reader's cap constant: the two are equal whenever the cap applies, and deriving it from the list means the number can never disagree with the list it describes. `DescribedProcessVersion.Enabled` is `bool`, not `bool?` — story 1 established that only `Version`, `IsActiveVersion` and `ParentId` can arrive NULL from the view, and `Enabled` is not one of them. And `DescribedProcessVersion` deliberately carries NO `[JsonExtensionData]` bag, unlike the graph types around it: at this stage clio builds every entry itself from the process library, so there is no server field a bag could catch. That inverts the day the server reports the family — the overlay in `ServerProcessDescriber.ApplyVersionFacts` is where both facts are documented, and adding the bag is part of that change, not of this one.

One consequence of the overlay is load-bearing and has its own regression test: every version member is assigned on the failure path too. The wire result deserializes into the same public type, so a newer `CrtProcessBuilder` that already returns a `version` key would otherwise bind it to the property and leave a server-supplied value standing next to a warning saying the facts were not established.
The ClioRing compatibility gate (`AGENTS.md`) is discharged by inspection rather than by running the Ring suites, and the inspected surface is recorded here because the gate demands it be stated: `clio-ring/ClioRing.Ipc`, `clio-ring/ClioRing` and `clio-ring/ClioRing.Desktop/actions.json` contain no reference to `describe-business-process`, `get-process-signature`, `generate-process-model` or `run-process`, and the nested commands `actions.json` dispatches through `clio-run` are deploy, env-info, import-iis-environments, list-packages, manage-envs, restart, uninstall and version — none of them process-related. So the describe result-content expansion and the caption-resolution change touch no Ring-consumed contract. Re-check this the day Ring gains a process action; the read half is exactly the kind of surface it would reach for.
Story 8 measured the version mechanics on a live stand (core 10.1.448.0, 2026-09-03) and returned one measurement that CONTRADICTS this ADR and the guidance article built on it, which AC-ERR requires be named here rather than quietly corrected.

**The version number and family membership are independent, and only one direction of the implication holds.** The ADR and the article both stated that the family root is version 0. Measured: of the process schemas on the stand, `BulkDuplicatesSearchProcess` carries `Version = 2` and `OrderApprovalBaseSubprocess` carries `Version = 1` while BOTH have `SysSchema.ParentId = NULL` — they are versions of nothing, and the view computes `VersionParentUId` as their own UId. The converse never occurs: no schema with a parent carries `Version = 0` (zero rows). So `Version = 0` DOES imply "this is a root", and "this is a root" does NOT imply `Version = 0`. `Version` is a stamped property, not a derived one. The discriminator chosen for the guidance article survives — family SIZE, not the number, settles whether a process has versions — but the article's V2 wording asserted the false direction and is corrected.

Other measurements, all read-only and none contradicting the design:

- A version's `SysSchema.ParentId` equals the ROOT's `SysSchema.Id`, and `ExtendParent` is 0 on both members, so the parent-name rename path (`Schema.cs:126`) is not taken by versioning (AC-02).
- The root's `SysSchemaProperty` rows survive a version's creation intact — the same eight property names on both members (AC-06). Two differ in VALUE and the difference is worth knowing: `IsActiveVersion` (root False, version True, stored as the strings "False"/"True") and `IsInterpretable` (root False, version True). `Tag` is copied identically and `CreatedInVersion` is `0.0.0.0` on both.
- **OQ-01 is closed by observation rather than by experiment.** A version does NOT have to land in the root's package: 3 of the 14 view-visible families are cross-package — `ChangeBulkEmailStatusDraftToPlanned` (CrtEmailMarketingApp → CrtEmailDesignerInEmailMarketing), `InsertingOpportunityFromLeadSubprocess` (OpportunityManagement → PRMPortal) and `WebhookEntityPostProcessing` (CrtTouchPoint → CrtWebTrackingBase). What decides the package is therefore not the root, and the create handler must take the target package as an input rather than inherit it.
- **OQ-02 is closed.** `UseNewSchemaHierarchyFolding` has `DefaultState = 1` in `Feature` with no `AdminUnitFeatureState` override, so it is ON — the manager's third ordering key is the package `HierarchyLevel`. Note the table names: this platform stores features in `Feature` / `AdminUnitFeatureState`, not `SysFeature` / `SysFeatureState`.
- The view is NOT a census of versioned processes: 18 version schemas exist in `SysSchema` and 14 are visible in `VwProcessLib`. Four are invisible, and for `InvoiceVisaBaseSubprocess`, `ContractVisaBaseSubprocess` and `OrderVisaBaseSubprocess` the ROOT is invisible too, so describe answers "no row for schema" — correct behaviour, and the reason the reader must never present an absent row as "unversioned".
- Every view-visible family holds exactly ONE version, numbered 1 and flagged active. No multi-version family and no two-members-flagged-active family exists on this stand, so those code paths remain defensive rather than observed.

Still unmeasured, because each needs a write to the stand: AC-01's draft create implementation run twice, AC-03 (`GetMaxProcessVersionInPackage` for the same and a different package, which needs server-side C#), AC-04's non-editable-package case, and AC-05 (`validateNamePrefixes` on a root with no prefix). Creating a version is IRREVERSIBLE by this feature's own V6 — the platform exposes no delete — so those steps need an explicit decision and a throwaway root, not a stock one.
