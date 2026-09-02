# Business process versioning — implementation plan

> Jira: [ENG-94374](https://creatio.atlassian.net/browse/ENG-94374)
> Companions: [`…-research.md`](business-process-versioning-research.md) ·
> [`…-traps.md`](business-process-versioning-traps.md) ·
> [`…-estimate.md`](business-process-versioning-estimate.md)
>
> **This file is written to be picked up cold on another machine.** Everything needed to start is here: repo
> paths, branch preconditions, ordered work items with the files each touches, the code shapes, the pins that
> must move, and the verification procedure.

---

## 0. Recommendation in one paragraph

Ship the **read half** (Stage A) as ENG-94374 and **record a deliberate deferral** of the two write operations —
which the ticket explicitly permits (*"or a recorded decision to defer with rationale"*). Stage A delivers two of
the ticket's three "not done" items outright (`describe-process` read-back of the version, and reading version
history in the only sense the product supports), needs **no `CrtProcessBuilder` change, no rebundle, no new
endpoint, no new destructive tool and no new permission surface**, and independently fixes a live wrong answer
(see [`…-research.md` §5.1](business-process-versioning-research.md)). Raise create + activate as a follow-up
ticket whose **first item is a mandatory spike**, so the deferred work carries an honest estimate instead of
inheriting a ~1-day one.

---

## 1. Preconditions before any code

### 1.1 Branch hazard — check this first

As of 2026-08-13 **both repos are on ENG-91845 branches with unmerged work**, and `DescribeProcessTool.cs` — the
file Stage A edits — is dirty in clio. See [Trap 18](business-process-versioning-traps.md).

```bash
git -C C:/Projects/clio status --short
git -C C:/Projects/workspace/ProcessBuilder status --short
```

Branch **from `origin/master`** per `docs/agent-instructions/pr-delivery-flow.md`, not from the current feature
branch. If ENG-91845 has not landed yet, either wait or rebase explicitly. Check
`spec/sprint-status.yaml` (`story-process-element-connections-4` / `-5`) before adding a story.

### 1.2 Mandatory reading

* `C:/Projects/clio/AGENTS.md` **and** `C:/Projects/clio/project-context.md` (AGENTS.md line 4 makes both
  mandatory; four of `project-context.md`'s rules bind this work — see §5).
* `C:/Projects/clio/clio/Command/McpServer/AGENTS.md` (read-response deadline, destructive timeout contract).
* `C:/Projects/clio/clio.mcp.e2e/AGENTS.md` (tiering, destructive opt-in).
* `C:/Projects/workspace/ProcessBuilder/CLAUDE.md` and `docs/process-builder-architecture.md` (Stage B+ only).
* Both `./.codex/workspace-diary.md` files.

### 1.3 Skills to invoke (clio `AGENTS.md` requires them explicitly)

| Work | Skill |
|---|---|
| Doc updates | `$document-command` |
| Anything under `McpServer/Tools|Prompts|Resources` | `$create-mcp-tool` |
| `clio.tests` / `clio.mcp.e2e` MCP tests | `$test-mcp-tool` |

---

## 2. Stage A — the ENG-94374 deliverable (clio only)

**Nothing in `C:/Projects/workspace/ProcessBuilder` is touched.** No new `[OperationContract]`, so
`ProcessDesignServiceWireContractTests.OperationContractCount_MustStayPinned` stays at **5** and clio's
`ExpectedOperationContractCount` stays at **5**. No rebundle, no `descriptor.json` bump, no SHA / `ModifiedOnUtc`
re-pin, no `ExpectedAuthorizationGateCallSites` move. **This is the single largest cost avoided.**

### A0 — BMAD artefacts *(1.5 h)*

`spec/prd/prd-process-version-readback.md`, `spec/adr/adr-process-version-readback.md`,
`spec/stories/story-process-version-readback-1.md`, `spec/test-plans/tp-process-version-readback.md`,
`spec/sprint-status.yaml`

AGENTS.md forbids code on a non-trivial feature before a PRD and ADR exist. **No BMAD artefacts exist for
ENG-94374** (`grep -rn 94374 spec/ docs/` returns nothing). The ADR must record three decisions that are
otherwise invisible later:

1. The version read is sourced from `VwProcessLib` **client-side** rather than from the `ProcessDesignService`
   package, and why — no rebundle, no new permission surface, and `ServerProcessDescriber` already reads that view
   for caption resolution (`clio/Command/ProcessModel/IProcessDescriber.cs:99`).
2. The write half is **deferred**, with the rationale from [`…-estimate.md` §4](business-process-versioning-estimate.md).
3. The **estimate correction**: ~1 day → see [`…-estimate.md`](business-process-versioning-estimate.md). Also fix
   the inherited row in `spec/process-design-service/task-list.md:349` so the next planner does not re-read the
   stale number.

### A1 — Spike: confirm the view on a live stand *(1 h)*

In the **product designer**, create a process and "Save new version" so a real family exists. Then read
`VwProcessLib` over DataService filtered on the family and confirm, **for both rows**: `UId`, `Name`, `Caption`,
`Version`, `IsActiveVersion`, `IsMaxVersion`, `VersionParentUId`, `VersionParentId`, `PackageUId`, `Enabled`.

Two things must be **verified, not assumed**:

* **(a)** the view returns **every** version, not only the active one — the `IsActiveVersion` filter is applied
  client-side by the section (`BaseProcessLibSection.js:43-46`), not by the view;
* **(b)** `VersionParentUId` on the **root** row equals its own `UId` (`COALESCE(PS.UId, SS.UId)` in
  `VwProcessSchemaVersion`), so filtering by it returns the family **including** the root.

Also confirm the row survives the view's `Tag <> ''` filter for a process built by `create-business-process`
(`ProcessBuildHandler` sets `SchemaDefaults.BusinessProcessTag`).

> **If any of this is false, Stage A is not viable** and the plan falls back to Stage B (server-side read).

### A2 — `IProcessVersionLibReader` *(2 h)*

`clio/Command/ProcessModel/IProcessVersionLibReader.cs` (+ impl in the same file, house style, cf.
`IProcessDescriber.cs`), registered in `clio/BindingsModule.cs`.

```csharp
/// <summary>
/// Reads a process's version facts and version history from the platform's own process-library view
/// (VwProcessLib), which already carries Version / IsActiveVersion / VersionParentUId / IsMaxVersion.
/// Read-only DataService; requires no CrtProcessBuilder operation.
/// </summary>
public interface IProcessVersionLibReader {
    /// <summary>
    /// Returns the version facts for the schema, or <c>null</c> when they cannot be established (view
    /// unreadable, row absent, DataService failure). Null means NOT ESTABLISHED — never "unversioned",
    /// never "version 0".
    /// </summary>
    ProcessVersionFacts Read(string schemaUId);
}
```

Two `ctx.Models<VwProcessLib>()` reads: the row by `UId`, then the family by `VersionParentUId`. Project, order
ascending by `Version`, mark the active member, mark the root (`UId == VersionParentUId`). **Cap the family at
50.** Register with `AddTransient` — CLIO001 forbids `new` for behaviour classes.

> **Do not write `catch (Exception)`.** `project-context.md` forbids a bare catch in clio — handle the specific
> DataService / ATF failure types or rethrow. (The ProcessBuilder-side equivalent in Stage B *may* use a broad
> catch; that rule is clio's only.)

### A3 — `DescribeProcessResult` version members *(1.5 h)*

`clio/Command/ProcessModel/IProcessDescriber.cs`

```csharp
/// <summary>This schema's version number. Absent (null) = NOT ESTABLISHED — never assume 0.</summary>
[JsonPropertyName("version")]                public int?   Version { get; set; }

/// <summary>Whether THIS schema is the version the runtime starts. Absent (null) = not established.</summary>
[JsonPropertyName("isActiveVersion")]        public bool?  IsActiveVersion { get; set; }

/// <summary>Schema UId of the version that actually RUNS. Differs from schemaUId ⇒ a NON-RUNNING version was read.</summary>
[JsonPropertyName("activeVersionSchemaUId")] public string ActiveVersionSchemaUId { get; set; }

/// <summary>Schema NAME of the version that actually runs, so it can be re-described without a second lookup.</summary>
[JsonPropertyName("activeVersionName")]      public string ActiveVersionName { get; set; }

/// <summary>Version-family ROOT (version 0) schema UId — the identity the history is keyed on.</summary>
[JsonPropertyName("rootSchemaUId")]          public string RootSchemaUId { get; set; }

/// <summary>Version history, ascending by version. Absent when not established.</summary>
[JsonPropertyName("versions")]               public List<DescribedProcessVersion> Versions { get; set; }
```

```csharp
/// <summary>One member of a process's version family, as the process-library view reports it.</summary>
public sealed class DescribedProcessVersion {
    [JsonPropertyName("schemaUId")]       public string SchemaUId { get; set; }
    [JsonPropertyName("name")]            public string Name { get; set; }
    [JsonPropertyName("caption")]         public string Caption { get; set; }
    [JsonPropertyName("version")]         public int    Version { get; set; }
    [JsonPropertyName("isActiveVersion")] public bool   IsActiveVersion { get; set; }
    [JsonPropertyName("isRoot")]          public bool   IsRoot { get; set; }
    [JsonPropertyName("packageUId")]      public string PackageUId { get; set; }
    /// <summary>FAMILY state, not per-version — EnableProcess keys on the ROOT SysSchema Id.</summary>
    [JsonPropertyName("enabled")]         public bool   Enabled { get; set; }
}
```

`ServerProcessDescriber` takes `IProcessVersionLibReader` in its primary constructor and, **after a successful
graph read**, fills these from `reader.Read(result.SchemaUId)`. On `null` it leaves every member `null` so
`JsonIgnoreCondition.WhenWritingNull` drops them. **A version read must never turn a successful describe into an
error.**

### A4 — clio unit tests *(2 h)*

`clio.tests/Command/ProcessModel/ProcessVersionLibReaderTests.cs`,
`clio.tests/Command/ProcessModel/ServerProcessDescriberTests.cs`,
`clio.tests/Command/DescribeProcessCommandTests.cs`

House style: `[TestFixture]` + `[Property("Module","Command")]`, per-test `[Category("Unit")]` + `[Description]`,
explicit Arrange/Act/Assert, a **because-clause on every assertion**, NSubstitute for `IDataProvider`.

> **Never `[Category("UnitTests")]` in clio** — `project-context.md` calls existing occurrences legacy
> violations. (It *is* the correct convention in the ProcessBuilder repo. Do not "harmonise" the two.)

Cases:

1. root-only family → `version = 0`, `isActiveVersion = true`, `versions` has one entry with `isRoot = true`;
2. two-version family with v1 active → v0 reports `isActiveVersion = false` and `activeVersionName` = v1's name;
3. reader returns `null` → describe still succeeds and **every version member is absent from the serialized
   output**;
4. family capped at 50.

> Each new field needs an assertion on the **serialized command output**, not only on the DTO. The recorded defect
> class on this surface is a field promised in a `[Description]`, declared on the DTO, and asserted by nothing.

### A5 — Tool description, prompt, capability map *(1.5 h)*

`clio/Command/McpServer/Tools/ProcessDesigner/DescribeProcessTool.cs`,
`clio/Command/McpServer/Prompts/ProcessDesigner/DescribeProcessPrompt.cs`,
`docs/McpCapabilityMap.md` §11

**Unchanged:** tool name `describe-business-process`; args record; safety flags (`ReadOnly = true,
Destructive = false, Idempotent = true, OpenWorld = false`); `[FeatureToggle("process-designer")]`;
`[RequiresPackage]` staying on `DescribeProcessOptions` and **never** on the args record (pinned by
`ProcessDesignerRequiresPackageAttributeTests`). `McpCoreToolProfile`: no change — the tool is non-resident.
`McpToolCompatibilityCatalog`: no entry (nothing renamed or removed). No new `KnownRoute` (63 stays free).

Only the `[Description]` grows. The load-bearing sentence is the trap:

> Also reports process VERSIONING: `version`, `isActiveVersion`, `activeVersionSchemaUId` / `activeVersionName`
> (the version that actually RUNS), `rootSchemaUId`, and `versions[]`. **Identifying a process by `process-name`
> targets ONE SPECIFIC VERSION, not the running one** — a versioned process has a distinct schema name per
> version (`<base><Package><N>`, e.g. `UsrProcess_0370312Custom1`), so `UsrProcess_0370312` reads version 0 even
> when version 1 is what executes. Whenever `isActiveVersion` is `false`, say so before explaining the process,
> and re-describe `activeVersionName` if the caller meant the running one. `version` and `isActiveVersion` are
> read-only platform state — they are NOT settable through `create-business-process` / `modify-business-process`,
> and this build has no operation that creates or activates a version. Absent (null) means NOT ESTABLISHED, never
> "version 0" and never "unversioned".

Verify `DescribeProcessToolTests`' reflection pin on the four `McpServerTool` flags still passes unchanged.

**Read-deadline check:** `describe-business-process` is `ReadOnly = true` and therefore bounded by
`McpReadDeadlineGate`'s 120 s. Measure that the two extra DataService reads fit; on timeout they must degrade to
absent, not fail.

### A6 — `clio.mcp.e2e` *(2.5 h)*

`clio.mcp.e2e/DescribeProcessToolE2ETests.cs` — extend the existing fixture, keeping `[NonParallelizable]`,
`[Category(ProcessDesignerE2EGate.CategoryName)]`, the `SkipIfFeatureDisabled` arrange step, **and the suite's
Allure attributes** (`[AllureNUnit]` / `[AllureFeature]` / per-test `[AllureTag]` / `[AllureName]` — 121 of 133
files carry them).

Cases:

1. an unversioned process built by `create-business-process` → `version = 0`, `isActiveVersion = true`,
   `versions` has one root entry;
2. after a version is created **in the product designer** on that stand, describing the **root by name** →
   `isActiveVersion = false`, the active version is named, and `versions[]` has both members ascending;
3. describing the new version **by UId** → `isActiveVersion = true`.

> Case 2 needs a stand fixture the harness cannot create itself (there is no create-version op yet). Either seed
> it once by hand and pin its name in the E2E settings, or mark case 2 explicitly manual. **Decide this in A1 and
> record it — do not silently drop it.**

> **These E2E tests buy zero CI signal.** `project-context.md`: the process-designer fixtures do not run in CI
> because `CrtProcessBuilder` is not installed on that stand. E2E here is **manual verification, not a gate** —
> anything load-bearing must be mirrored at unit level.

### A7 — Guidance (third repo) *(1.5 h)*

Guidance content **no longer lives in clio**. Open a PR in
`Advance-Technologies-Foundation/clio-knowledge` adding a **"Process versions"** section to
`guidance/process-modeling`:

* a version is a separate schema in a **flat** family (v2's parent is v0, not v1);
* exactly one version is active and only it is started by name/UId at runtime;
* running instances stay pinned to the version they started on;
* **this build reads versions but cannot create or activate one.**

The PR needs a `libraryVersion` + `sequence` bump — clio rejects a library whose content changed under a reused
sequence. Then re-pin `clio.tests/Command/McpServer/Fixtures/curated-knowledge-names.json` to the new generation,
because `WorkspaceTemplateGuidanceDriftTests` validates the shipped templates against it.

### A8 — ClioRing gate *(0.5 h)*

Search `clio-ring/ClioRing.Ipc`, `clio-ring/ClioRing`, `clio-ring/ClioRing.Desktop/actions.json` for
`describe-business-process` (directly or as a `clio-run` nested command).

Expected: not consumed → record *"ClioRing compatibility reviewed, no Ring-consumed contract changed"* citing the
inspected paths. If it **is** consumed, the change is still additive, but then
`dotnet test clio-ring/ClioRing.Tests/ClioRing.Tests.csproj -c Release` **and** the Windows x64 NativeAOT publish
must both run (+~1.5 h).

### A9 — Docs sweep, regression, review gates *(2 h)*

Docs verdicts to state explicitly (do not just assume "no update required"):

| Target | Verdict |
|---|---|
| `docs/McpCapabilityMap.md` §11 | **Update** — describe row gains the version fields + the by-name caveat |
| `clio/docs/commands/install-process-builder.md`, `clio/help/en/install-process-builder.txt` | **No change for Stage A** (they enumerate the gated tool family; Stage A adds no tool). **Must change in Stage C/D.** |
| `clio/Commands.md`, `clio/Wiki/WikiAnchors.txt` | No change — no new canonical verb |
| `clio/tpl/workspace/AGENTS.md`, `clio/tpl/ui-project*/AGENTS.md` | No change — no tool renamed/added/removed, so `WorkspaceTemplateGuidanceDriftTests` is unaffected |

Regression per the smart-regression policy — **note that `BindingsModule.cs` is an explicit full-suite trigger**,
so the targeted filter is necessary but not sufficient:

```bash
dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&(Module=Command|Module=McpServer|Module=ProcessModel)"
```

```bash
dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit"
```

Then: two mandatory agentic review gates (comprehensive pre-PR, comprehensive final), the Sonar quality-gate
inspection per `docs/agent-instructions/pr-delivery-flow.md` §3.2 (the `sonar-local-check` skill exists for this),
a PR body with explicit notes on tests / docs review / MCP review, and a **workspace-diary entry**.

---

## 3. Stage B — move the read server-side *(optional, deferrable)*

Worth building only when a rebundle is happening anyway, or when [Trap 14](business-process-versioning-traps.md)
(the two-authority divergence) actually bites. **The wire field names are identical to Stage A**, so the DTO, the
tool contract, the prompt and the capability map do not change when the source moves.

New `Files/src/cs/Versioning/{IProcessVersionReader,ProcessVersionReader}.cs`, additive `[DataMember]`s on
`DescribeProcessResponse`, `SchemaDefaults.BaseProcessSchemaUId`, DI in `CrtProcessBuilderApp.cs`, and the
`ProcessDescriber` wiring. **No new `[OperationContract]`** — op count stays 5.

Substitutability constraint (load-bearing): read the version through the **interface** member
`ISchemaManagerItem<T>.FindPropertyValue(x => x.Version, 0)` and derive active-ness by comparing
`GetActiveVersionItem(item).UId`, because `GetVersion` / `GetIsActiveVersion(Guid)` /
`GetActiveVersionItemByUId` are `public` but **not virtual** and cannot be stubbed.

Also required for any ProcessBuilder change: a **ProcessBuilder workspace-diary entry**, and updates to
`docs/process-builder-architecture.md` **and** `docs/process-builder-architecture.puml`.

Rebundle:

```bash
pwsh ./rebundle-process-builder.ps1 -PackageRepoPath C:/Projects/workspace/ProcessBuilder -Version 1.2.0.0
```

The script auto-refreshes `ExpectedArchiveSha256`, `ExpectedArchiveVersion` and `ExpectedDescriptorModifiedOnUtc`,
and **verifies** `ExpectedSchemaDescriptorModifiedOnUtc`. It cannot touch `ExpectedOperationContractCount` or
`ExpectedAuthorizationGateCallSites` — those are the **only two manual pins**.

> `BundledPackages.ProcessBuilderVersion` **does not exist** (deleted; see
> `spec/adr/adr-bundled-package-version-source-of-truth.md`). Use `ExpectedArchiveVersion`.
> `ProcessBuilder/docs/bundling-into-clio.md:122` still names the deleted constant — **fix that stale line in the
> same change** or the next agent walks into it again.

---

## 4. Stages C and D — the write half *(recommend deferring to a follow-up ticket)*

### C0 — mandatory spike *(4 h — everything after it is contingent)*

Answer, against a real stand, by **diffing a package-created version against a designer-created version of the
same source process**, field by field, with every difference explained or eliminated:

1. **The UId rewrite.** Is `MetaSchema.GetMetaItems(Collection<IMetaItem>)` reachable from a configuration
   package, and does the walk actually cover everything?
   → see [BLOCKER 2](business-process-versioning-traps.md). Typed walk **must** be extended to schema
   `Parameters`, `ExecutionContexts` and element `Parameters`, or replaced by targeted `A3` / `A4` surgery.
   **Never** the platform's blunt `metaData.Replace`.
2. **Property persistence.** Prove `Version` / `IsActiveVersion` actually land as `SysSchemaProperty` rows →
   see [BLOCKER 1](business-process-versioning-traps.md). Acceptance: read `SysSchemaProperty` for the new
   schema Id, **and run create-version twice — the second must yield `Version = 2`.**
3. **`ExtendParent`.** What does `InternalCreateSchema` leave on a ProcessSchema created from a non-Def parent?
   Several platform version queries filter `ExtendParent = false`; a version that comes out `true` vanishes from
   them.
4. **`IsInherited` memoisation** → [BLOCKER 4](business-process-versioning-traps.md).
5. **Localizable resources.** The designer client copies them explicitly (`copyLocalizableResources`); an
   in-memory `ProcessSchema.Clone()` carries only what was loaded on that request. Decide: copy, or inherit from
   the parent — and state which.
6. **`SysSchema.ParentId`** after save, and that the new version is discoverable by `GetAllVersionItems`.

### C1..C7 — create a version

Shape (the strongest of the three candidate designs), in
`Files/src/cs/Versioning/ProcessVersionCreateHandler.cs`, following `ProcessBuildHandler` exactly — own guard
call, own rollback, errors as `success:false` + `errorMessage` (**not** `ErrorOr`; that assembly is referenced but
used by no source file):

1. `_guard.EnsureCanManageProcessDesign()`
2. resolve source; **pre-validate the UId with `FindItemByUId`**, never `GetItemByUId` ([Trap 8](business-process-versioning-traps.md))
3. root = `ParentUId` empty or `bb4d6607-026b-4b27-b640-8f5c77c1e89d` ? own UId : `ParentUId`
4. package = explicit `packageName` else the source's package
5. `version = GetMaxProcessVersionInPackage(conn, rootItem.Id, packageUId) + 1` — **`rootItem.Id` is the
   `SysSchema.Id`, NOT the UId** ([Trap 6](business-process-versioning-traps.md))
6. `name = rootItem.Name + Regex.Replace(packageName, @"\W", "") + version` — **`Custom` is the package name**
7. refuse when `ProcessExists(name)`
8. clone via `manager.CreateSchema(name, source, conn, Guid.NewGuid(), false)`, then the UId rewrite from C0
9. **restore `item.Caption` AND `item.Instance.Caption`** ([Trap 7](business-process-versioning-traps.md))
10. `ParentSchemaUId = rootUId` (**flat family**), `IsActiveVersion = false` **explicitly**
    ([Trap 5](business-process-versioning-traps.md)), `IsDelivered = false`
11. persist the properties per [BLOCKER 1](business-process-versioning-traps.md)'s fix
12. `EnsureValidForSave` — `SaveSchema` does **not** validate
13. `Save`; on `false` or throw → `Rollback` + `success:false`
14. warn when `IsInterpretable == false` ([Trap 12](business-process-versioning-traps.md))

**A new `[OperationContract]` named `CreateProcessVersion` — never a new field on `ModifyProcessRequest`**
([Trap 11](business-process-versioning-traps.md)).

### D1..D6 — activate a version

**Must be routed through the package**, not the native endpoint
([BLOCKER 3](business-process-versioning-traps.md)):

```csharp
_guard.EnsureCanManageProcessDesign();      // SetActiveVersionItem performs NO permission check of its own
var target = Manager.FindItemByUId(schemaUId);
Manager.SetActiveVersionItem(target);       // public virtual, BaseProcessSchemaManager.cs:1317
// MANDATORY read-back verify — the platform SWALLOWS sibling deactivation failures (:518-519)
var nowActive = Manager.GetActiveVersionItem(target);
response.Success = nowActive != null && nowActive.UId == target.UId;
```

Destructive tool ⇒ it **owns its own timeout contract** and must not route through the read deadline
([Trap 10](business-process-versioning-traps.md)).

### Pins that move for C/D — the two the rebundle script cannot touch

| Pin | File | C | D |
|---|---|---|---|
| `ExpectedOperationContractCount` | `clio.tests/Common/BundledProcessBuilderPackageTests.cs:239` | 5 → 6 | 6 → 7 |
| `HaveCount(5)` | `ProcessBuilder/tests/…/ProcessDesignServiceWireContractTests.cs:113` | → 6 | → 7 |
| `ExpectedAuthorizationGateCallSites` | `clio.tests/Common/BundledProcessBuilderPackageTests.cs:227` | 3 → 4 | 4 → 5 |

> The count is **per gate call site**, not per operation: an operation routed through the shared
> `ProcessDesigner.Execute` boundary contributes none; a write handler owning its own try/rollback contributes one.

### Additional obligations for C/D that Stage A does not have

* **A `PassthroughToolClassificationRegistry` row per new tool** —
  `clio.tests/Command/McpServer/PassthroughToolClassificationRegistry.cs`. A new `[McpServerToolType]` without a
  row is a **red `Module=McpServer` unit test on the first build**. `NotAudited` is the likely correct bucket;
  `NotApplicable` requires the AC-05 argument.
* **`clio/docs/commands/install-process-builder.md:24-27` and `clio/help/en/install-process-builder.txt:16-19`**
  both enumerate the gated tool family and go stale the moment a tool is added.
* **Destructive-E2E opt-in:** `McpE2E:AllowDestructiveMcpTests=true`, targeting a **dedicated sandbox only**.
* `ProcessDesignerRequiresPackageAttributeTests` — add `[TestCase]`s **and** update the class-level
  `[Description]` that currently says *"the four process-designer command options classes"*.

### Permanently out of scope

**Delete-a-version.** No product concept, and the platform's delete cancels every running instance
([Trap 16](business-process-versioning-traps.md)). "Manage version history" here means **read and activate**.

---

## 5. Rules from `project-context.md` that bind this work

| Rule | Applies |
|---|---|
| No `[Category("UnitTests")]` in clio (legacy violation) — but it **is** correct in ProcessBuilder | A4, B3 |
| No bare `catch (Exception)` in clio — handle specifically or rethrow | A2, A3 |
| MCP E2E in CI is **advisory and non-blocking**; process-designer fixtures **do not run in CI at all** | A6, C6, D5 |
| `Command<TOptions>` + DI services; MediatR is removed — do not use | A2, C4, D3 |
| CLIO001: no `new` for behaviour classes; interface + DI registration | A2, B1, C1 |

---

## 6. Verification procedure on a live stand

```bash
clio install-process-builder --force
```

* `--force` is required whenever the environment already recorded an equal-or-higher version.
* It runs a full configuration build **and** a restart — every iteration costs minutes, and every defect found
  here costs another whole loop.
* **A rebundle has no effect until clio is rebuilt** — install resolves the archive from the *build output*
  directory.
* **Run schema writes sequentially.** A parallel burst trips IIS rapid-fail and downs the .NET Framework app pool
  ([Trap 15](business-process-versioning-traps.md)).
