# ENG-100077 — stale / blank callee parameter captions on a Sub-process element: plan

Ticket: **ENG-100077** "Sub-process resync persists a stale callee parameter caption, and a new callee parameter
shows a blank caption on the first caller read" (Sub-task of **ENG-92707** "Sub-process element: selection +
parameter sync"). Mechanism and evidence are in `eng-100077-subprocess-caption-sync-research.md`; section numbers
below (§) refer to it.

## 1. Recommendation

Fix the cause where it is created, in **CrtProcessBuilder's save path**, and add the missing report entry:

1. **R — release the saved process schema's resource cache after every package `SaveSchema`.**
   * Two call sites, both in `packages/CrtProcessBuilder/Files/src/cs/Schema/ProcessSchemaRepository.cs`: `:168`
     (create) and `:224` (`SaveEdited`, used by modify and modify-as-new-version).
   * It does exactly what the classic designer does after its own save
     (`ProcessSchemaDesignerUtilities.ReleaseLocalizableValues`, research §3.1 step 6).
   * This alone satisfies acceptance criteria 1, 2 and 3; criterion 3 is the incidental-save one, added from R1.
   * It also closes the designer's server-side caller load showing a stale or blank caption.
2. **B — report caption changes in the resync report**, measured against the caller's STORED caption. This is
   criterion 4.
   * Without R, the caption the load produced is already the stale one. With R, it is already the fresh one.
   * Either way, "before" read from the in-memory element is useless (research §6 trap, agent B §4). It has to come
     from the database.

Do **not** make "overwrite captions from a fresh read" (option a) or "reset the callee's cache before each sync"
(option c) the fix. Both are compared in §2.

## 2. Options

| Option | What it is | Verdict | Why |
|---|---|---|---|
| **R** | After `SaveSchema`, `GetManager(schema.GetResourceManagerName()).ReleaseAllResources()` on the app connection's workspace resource storage | **Do it** | Removes the only source of the stale state found (research §3.1 steps 2–4). It is what the platform's own designer already does, and a designer save measurably cures the state (T1). D2 proves nothing inside the package's save request rebuilds a stale instance: a fresh read straight after the save made the first resync fresh. So releasing after `SaveSchema` returns is late enough. One place, two lines, no read-path coupling. |
| **B** | New `CaptionsChanged {parameter, from, to}` in `SubProcessSyncReport`, where `from` is the caller's stored `SysLocalizableValue` row for the current culture and `to` is the caption about to be saved | **Do it** (scope: explicit `resync` and selection only; see §7 decision 2) | Criterion 3. It must be measured against stored rows, which is a small slice of ENG-99737 "Sub-process resync: report what the called process changed, by diffing the STORED metadata". It is built so ENG-99737 can generalize it. |
| a | After the platform sync, overwrite the element's captions from a fresh read of the callee | Reject as the fix | The only read proven fresh is `TryDesignItem`, the designer card's path. The package reader's `GetInstanceByUId` can be the stale build (pair 2, D3). It covers the explicit resync only, not the incidental save that reverts a correct row (R1), not `describe`, and not the designer. It treats the symptom on every resync while the cause keeps producing it. |
| c | Before syncing, release the callee's cache and evict its `MetaItems` entry | Reject | It needs an eviction the package can only get through `ISchemaManagerItem.Invalidate()`, which also broadcasts a change notification (**A**). It runs on every read path we control and on none we don't (describe, designer). R removes the cause at the writer instead. |
| d | Document only; workaround "save the callee again, then resync" | Reject | The workaround as filed is wrong for clio: a second clio save moves the lag by one (finding A). Only a DESIGNER save of the callee cures it (T1). Silent reverts (R1) cannot be documented away. |

**Pre-existing stale entries** (a callee saved by an older package build): R does not cure an entry that already
exists; the next save of that callee does. Installing the fixed package recompiles and restarts the app pool,
which clears every cache (**inferred**, standard for a C# package install; to be confirmed on the stand at rollout).
The release notes should still name the manual cure: open and save the callee in the designer.

## 3. Design

### 3.1 R — release after save (package)

* **What to call.** Repeat the three steps of the designer's `internal` helper (`ProcessSchemaDesignerUtilities.cs:68-75`)
  using public API only:
  1. `var manager = _userConnection.AppConnection.Workspace.ResourceStorage.GetManager(Schema.GetShemaResourceManagerName(uid))`
     (`IResourceStorage.GetManager` and `IResourceManager.ReleaseAllResources` are public interfaces;
     `Schema.GetShemaResourceManagerName` is `public static`, `Terrasoft.Core/Schema.cs:586-588` **V**).
  2. Point the saved design instance's `ResourceManager` at it, when the instance is at hand.
  3. `manager.ReleaseAllResources()`.

  Guard against a null `ResourceStorage` the way `SchemaManager.cs:1047-1050` does. Do not use
  `IResourceStorage.Invalidate`: an instance still holding the old manager would keep reading its stale sets
  (unit-test agent, Q4).
* **Seam.** Follow the package's own precedent: a `protected virtual` seam on `ProcessSchemaRepository` around the
  manager save, like the existing `RemoveItem` seam (`ProcessSchemaRepositoryTests.cs:59-73`). `SchemaManager.SaveSchema`
  is not virtual (`SchemaManager.cs:4256, 4276`), so the tests cannot run the real save.
* **Concurrency.** `ReleaseAllResources` runs under the manager's `_lockObject` (`SchemaResourceManager.cs:441-448`);
  so do the readers.
* **Call it** in `ProcessSchemaRepository` right after `Manager.SaveSchema(...)` returns, on both sites, whatever
  the returned bool. That matches the designer, which releases unconditionally after `SaveSchema` returns.
  * Do not release in `finally` on an exception path. If the save rolled back, the snapshot in the cache equals
    the committed rows, and releasing is harmless but pointless. Keep the code simple.
* **Not** before `SaveSchema`: the pipeline's own `InitializeLocalizableValues` would refill the cache from the
  still-uncommitted state.
* **XML doc** on the release method states why the release is needed, and names `ReleaseResourcesManagers`
  releasing by `SysSchema.Name` as the platform gap.
* **Knowledge record** (clio, `docs/knowledge/platform/process-resource-cache-not-released-by-save.md`,
  `applies-to: clio/CrtProcessBuilder/CrtProcessBuilder.gz`): the UId-keyed cache, the by-name release and the
  designer's compensation. This is exactly the kind of fact whose failure is silent.

### 3.2 B — caption changes in the resync report (package)

* **Stored captions.** Add `IStoredCaptionReader`. It returns, for the caller schema's `SysSchema.Id` and the
  current culture, every `BaseElements.<element>.Parameters.<param>.Caption` row.
  * One `Select` through the request `UserConnection`, not the NOLOCK resource reader.
  * Read it before `SaveSchema`, inside `SubProcessApplier.SynchronizeAgainstCurrentCallee` next to `Snapshot`.
* **Diff.** After the platform sync, and before `EnsureSynchronizationLanded`, compare each element parameter's
  caption (current culture) with the stored row. A missing row counts as "no caption". Emit
  `CaptionsChanged{Name, From, To}` for every difference, including a parameter added by this sync that now has a
  caption.
  * **Separate "filled in" from "changed"** (`From` null or empty vs non-empty) in the notice. In the shipped
    corpus 303 of the 311 caller-vs-callee caption differences are an EMPTY caller caption (§10); without the split
    the report would mostly announce old blanks being filled.
  * Say plainly in the notice that the value came from the called process. A caller whose captions were
    deliberately edited loses them on any sync (platform behaviour, §10), and the notice is the only place the
    user learns it.
* **Wire it through the report:**
  * `SubProcessSyncReport` gets the new list, and `IsUnchanged` must include it (`SubProcessSyncReport.cs:141-146`,
    its doc says so).
  * `MultiInstanceApplier.DriftOf` copies it (`:768-774`), or multi-instance de-conversion drops it.
  * `SubProcessSyncNotices.Describe` gets one sentence per element, for example "3 parameter captions refreshed from
    the called process: DeliveryDateParameter 'Requested delivery date' → 'Delivery date and time', …". It stays a
    `Warnings` string, so clio forwards it with no clio code change (agent B §2).
* **Leave the structured-field question to ENG-99737.** That ticket owns "structured drift fields"
  (`SubProcessSyncNotices.cs:164-169`).

### 3.3 clio side

* Rebundle with `pwsh ./rebundle-process-builder.ps1 -PackageRepoPath C:\Projects\workspace\ProcessBuilder -Version <next>`.
  * The version MUST go up.
  * The two security counts do not move (no service-surface change), but check them.
  * No `[RequiresPackage]` literal moves. Convergence
    (`clio/Common/BundledPackageConvergence.cs:110-127`) already refuses a stand whose package is older than the
    bundled one, so the new clio will not run process-designer commands against an unfixed stand.
* **Docs:** `clio/docs/commands/modify-business-process.md` and `clio/help/en/modify-business-process.txt`, only if
  they list the resync notices. Today they do not mention resync (grep). Otherwise "docs reviewed, no update
  required".
* **MCP:** `ModifyBusinessProcessTool` description says caveats arrive as `Warning` entries (`:359-361`), which is
  still true. "MCP reviewed, no update required" for the tool itself, plus the E2E below.
* **ClioRing:** it does not consume `modify-business-process` / `describe-business-process` (agent B §2). Record
  "ClioRing compatibility reviewed, no Ring-consumed contract changed" with the inspected paths.

### 3.4 Guidance (clio-knowledge)

* The caption behaviour went into `guidance/mcp/guides/processes/sub-process.md`, as a CAPTIONS bullet next to
  `resync`. It covers:
  * the two notices;
  * the platform's copy-every-sync rule;
  * the floor 1.6.6.16;
  * the designer-save cure for older stands.
* `parameters.md` was left UNCHANGED, although the plan first targeted its notices list (`:95-96`) and its
  "a save of the CALLEE refreshes" line (`:143-144`). That article is at 99.9% of the `get-guidance` response
  budget, and even a 40-character pointer failed `ProcessGuideResponseSizeTests`. The test forbids raising the
  budget. From 1.6.6.16 its sentence is true again as written.
* Bumped `libraryVersion` 1.15.71 → 1.15.72; `dotnet test automation/Clio.Knowledge.Bundle.Tests -c Release`
  202/202.

## 4. Acceptance criteria → evidence

| Criterion (ENG-100077) | Proved by |
|---|---|
| After a callee caption-only change, one `resync` leaves the caller's `SysLocalizableValue` row equal to the callee's caption | E2E-1 (DB row via SQL), plus the stand replay of pair 2 / E2 |
| A new callee parameter shows the callee's caption on every caller, regardless of read order | E2E-3 (D5 order: add → describe callee → describe callers A and B), E2E-3b (pair-4 order: caller B first), plus the stand replay of D5 / Q5d |
| A package save of a caller that is not a resync does not persist a stale caption (Finding C, added 2026-09-23) | E2E-2 (the R1 shape), plus the stand replay of R1 |
| A resync that changes a caption says so in its report | E2E-4 (warnings contain the caption sentence; none when nothing changed), plus unit U3–U5 |
| Regression tests for both, each verified to fail with its fix reverted | §5: every E2E is run against today's package (must fail) and the new one (must pass); every unit test is shown red with its production line reverted |

## 5. Tests

### 5.1 Package unit tests (`tests/UnitTests/CrtProcessBuilder.Tests`)

The harness (`SubProcessTestSupport`) cannot model the platform's shared resource cache. The unit tests therefore
prove the wiring. The E2E tests prove the behaviour.

* **U1** `SaveEdited_Should_ReleaseTheSavedSchemasResourceCache`.
  * Arrange: a test subclass of `ProcessSchemaRepository` overrides the save seam to return true. Set
    `UserConnection.Workspace.ResourceStorage = Substitute.For<IResourceStorage>()`, with
    `GetManager(Schema.GetShemaResourceManagerName(uid))` returning a substituted `IResourceManager`. Precedent:
    `BaseComposableAppTestFixture.MockLczValues`, `:411-426`.
  * Assert: `Received(1).ReleaseAllResources()`, and never `GetManager(<schema Name>)`.
  * The platform's own template for this test is `Terrasoft.Nui.ServiceModel.Tests/WebService/ProcessSchemaDesigner.Tests.cs:564-599`
    (`Save_ReinitializeSchemaResourceManager`).
  * No new test dependency is needed: `CrtProcessBuilder.Tests` already references `Libs\UnitTest.dll` and
    `Libs\Terrasoft.TestFramework.dll`.
* **U2** Same for the create path, plus the save returning `false` (the designer releases then too).
* A behavioural unit test with a real `SchemaResourceManager` is **not** planned. Its reload goes through `internal`
  readers (hierarchy, resource, culture) that fall back to the database, and faking them from the package's test
  assembly means reflection on non-public members, which would test the fake. The behaviour is proved by the E2E
  tests in §5.2.
* **U3** `Synchronize_Should_ReportACaptionChange_WhenTheStoredCaptionDiffers`: a fake `IStoredCaptionReader`
  returns "Old", the callee's caption is "New". Expect `CaptionsChanged` with `Old → New`, `IsUnchanged == false`,
  and the notice sentence.
* **U4** No entry when the stored caption equals the synced one, including `""` against a missing row.
* **U5** Multi-instance: `DriftOf` carries `CaptionsChanged` through de-conversion.

Style: AAA, `because` on every assertion, `[Description("TC-0x: …")]`, `Method_Condition_ShouldOutcome` (the
package's convention).

### 5.2 E2E (`clio.mcp.e2e`, `SubProcessElementToolE2ETests`, category `McpE2E.ProcessDesigner`)

Fresh names per run. Captions are read from `SysLocalizableValue` through the `execute-sql-script` tool. Never
rely on `describe` alone, because read-time sync masks the DB (Q5d).

* **E2E-1** create callee + caller → `setParameter` caption on the callee → `resync` the caller → caller row ==
  new caption. Fails today on every run by the model (pairs 2 and E2, finding A 3 of 3).
* **E2E-2** create → callee caption change → incidental `setParameter` on the CALLER's own parameter → the element
  row == new caption (the R1 shape).
* **E2E-3** create → `addParameter` on the callee → `describe` the callee → `describe` the caller → the new
  parameter's caption == the callee's (D5). **E2E-3b**: the same with the caller read first (pair 4).
* **E2E-4** after a caption-only callee change, the `resync` output carries the caption sentence. A second
  `resync` with no change carries none.

Each E2E must be shown red against the stand's current package (1.6.6.13) before the fix is installed, and green
after. That is the "fails with its fix reverted" evidence for the behaviour. Record both runs in the PR.

### 5.3 Stand replay (manual, in the PR)

Re-run the research's decisive sequences on the new package: pair 2, D3, D4, D5, Q5d, R1. Each must now come out
fresh. D3's "describe fresh / resync stale" split must disappear.

## 6. Risks

* **Race window.** A reader that builds the callee between `SaveSchema`'s eviction and our release still captures
  a stale build, until the next save. The designer has the identical window. Accepted.
* **Web farm / web garden.** Every node keeps its own cache. The release is local. Remote nodes get only an item
  invalidation, and their cache is normally empty at rest (**A**). Not measured, because the stand's IIS worker
  count was not checked.
* **Behaviour change.** After R, an incidental package save of a caller will visibly refresh its captions from the
  callee. That is the platform's intended sync and was happening before, only with stale values. It goes in the
  release notes.
* **Current culture only.** The platform copies one culture; this fix does not change that (research §7).
* **B's stored read.** The caller must be resolved to its `SysSchema.Id` in the current package. The design item
  carries it. Keep the query to the current culture so it matches what the sync writes.
* **Shipped corpus:** see §10. If shipped callers commonly carry captions different from their callee's, B will
  report them on the first resync of such a caller. That is correct, and the report says what changed.

## 7. Decisions for the owner

1. **Scope of B:** DECIDED 2026-09-23. B is done in ENG-100077; criterion 3 stays here.
2. **Where B reports:** DECIDED 2026-09-23. Only on an explicit `resync` or selection. Incidental saves stay silent,
   as they already are for other drift; that belongs to ENG-99737.
3. **Package floor in clio:** WITHDRAWN, there is no decision to make.
   * `BundledPackageConvergence.TryGetConvergenceRefusal` (`clio/Common/BundledPackageConvergence.cs:72-127`)
     already refuses every gated process-designer command when the stand's CrtProcessBuilder is older than the
     bundled one. The rebundle with a higher `-Version` therefore forces the update by itself.
   * The `[RequiresPackage]` literal moves only if clio's own code depends on the new package, and it does not.
4. **Platform follow-up:** DECIDED 2026-09-23, not filed.
5. **Ticket text:** DONE 2026-09-23 with the owner's go-ahead. ENG-100077's description now carries:
   * the root cause;
   * Finding C (R1);
   * the corrected workaround (a DESIGNER save of the callee);
   * the decided fix;
   * the added acceptance criterion for incidental saves.

   Jira keeps the previous description in the issue history.

## 8. Estimate

| Work | Days |
|---|---|
| R + seam + U1/U2 | 0.5 |
| B (stored reader, diff, report, notices, multi-instance) + U3–U5 | 1.0–1.5 |
| Rebundle, E2E-1…4 (red on the old package, green on the new), knowledge record, docs review | 1.0 |
| Stand replay (§5.3) | 0.5 |
| Guidance PR | 0.25 |
| Review gates (pre-PR full fan-out, final gate), CI | 0.5–1.0 |
| **Total** | **3.75–4.75** |

## 9. Branches, PRs, merge order

* **Precondition:** engineering/crt-process-builder#75, Advance-Technologies-Foundation/clio#1659 and
  Advance-Technologies-Foundation/clio-knowledge#223 (the three PRs of ENG-99856 "Sub-process element: support
  MULTI-INSTANCE (running the callee once per item of a collection)") are MERGED. Check before branching. If they
  are not merged, ask whether to wait or to base on the ENG-99856 branches temporarily and rebase.
* **Branches:** `feature/ENG-100077-subprocess-caption-sync` off fresh package `main`, clio `master` and
  clio-knowledge `master`.
* **Order:**
  1. Package PR → `main` (GHE).
  2. clio PR → `master`, with the rebundle, E2E, knowledge record and docs.
  3. clio-knowledge PR → `master`, with the version bump.
  Only a human merges.
* **Validation to state in each PR:**
  * the package's unit filter
  * the clio targeted filter (`Category=Unit&Module=McpServer` and whatever the rebundle touches — the bundled
    package tests run under `Common`)
  * the E2E run (red on the old package, then green)
  * "MCP reviewed", "docs reviewed" and "ClioRing compatibility reviewed" lines

## 10. Shipped corpus (measured 2026-09-23, `C:\Projects\PackageStore`)

What was scanned:
* 1,085 package roots, `7.8.0` branch where present; 1,636 distinct process UIds.
* 246 of those processes contain sub-process elements: 381 elements, 61 of them multi-instance.
* 1,661 en-US element parameters were compared with their callee's caption, by a one-off scan of the resource
  files (not committed).

| en-US outcome | Count |
|---|---|
| Equal | 1,346 (81.0%) |
| **Caption missing on the caller** | **303 (18.2%)**: 286 in 56 old elements (2015–2020) that store no parameter captions at all, 17 partly missing |
| Different | 8 (0.5%): 7 in one caller, `CrtCaseKnowledgeCopilot/CreatioAISuggestCaseResolution`, whose captions were deliberately cleaned up after its callees (for example "Case number" vs "CaseNumber"); 1 typo fixed later in a test callee |
| Missing on the callee | 4 |

What it means for the fix:
* **R changes no shipped data by itself.** It only decides whether a sync copies the current or a stale callee
  caption. The platform already overwrites the caller's captions on every sync, in the designer card too, so
  `CreatioAISuggestCaseResolution`'s edited captions do not survive any save that syncs. That is pre-existing
  platform behaviour and out of scope; B makes it visible.
* **B must split "filled in" from "changed"** (§3.2). Otherwise 97% of what it reports on shipped callers would be
  old blanks being filled.
* **Other cultures:** only ru-RU has real content. It has 6 differences, all in the same AI caller, hand
  translations such as "Номер кейса" vs "CaseNumber". The sync copies the current culture only, so these survive an
  en-US resync.
* 75 callee parameters are absent from their callers' elements, in 34 elements, 11 of them in test packages.
  Any resync adds them; that is existing behaviour, unrelated to captions.
