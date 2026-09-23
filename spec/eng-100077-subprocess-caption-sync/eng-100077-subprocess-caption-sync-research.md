# ENG-100077 — stale / blank callee parameter captions on a Sub-process element: research

Ticket: **ENG-100077** "Sub-process resync persists a stale callee parameter caption, and a new callee parameter
shows a blank caption on the first caller read" (Sub-task of **ENG-92707** "Sub-process element: selection +
parameter sync"). Research only; no code, Jira or PR was changed.

A developer stand (.NET Framework, MSSQL 2022, 4 active cultures), CrtProcessBuilder **1.6.6.13**, clio build from the ENG-99856 worktree. Measured 2026-09-23; all times
below are the stand's UTC.

## 1. Answer in five lines

1. **The one-save lag is created by CrtProcessBuilder's own save path, not by the platform's sync and not by the
   database.** A callee saved through clio (`modify-business-process`) leaves its shared, UId-keyed resource cache
   holding the captions from **before** the edit. The first metadata build afterwards copies those captions into
   the `MetaItems` instance, and every caller sync reads that instance until the callee is saved again.
2. **The classic Process Designer does not have the defect.** It releases the callee's resource cache after it
   saves. A callee saved in the designer is fresh on the very first caller resync (4 of 4 runs). A designer save
   with **no change** cures a callee that the package left stale.
3. **It is worse than the ticket says.** Any package save of a caller, not only an explicit `resync`, re-runs the
   load-time sync and persists the stale caption. One incidental save reverted a correct caller row (`D1 date A2` →
   `D1 date A1`) 30 minutes after the callee was saved (R1).
4. **Finding B is the same mechanism.** A parameter added in a package save has no key in the stale cache, so the
   stale instance carries a null caption. A caller that copies it gets `""`. A designer save of that caller
   without opening the card persisted the blank (no resource row) (Q5d).
5. **The root fix is one release call after the package's `SaveSchema`.** It mirrors
   `ProcessSchemaDesignerUtilities.ReleaseLocalizableValues`, and both package save sites are in
   `ProcessSchemaRepository`. See the plan.

## 2. Method

* Every run used new fixtures (`UsrCapD1…D5*`, `UsrCapE1…E4*` in `Custom`). Each step was read on two surfaces: the
  caller's `SysLocalizableValue` row (`probe/capsql.sh`) and `describe-business-process` (`probe/mcpdrv.py`).
* Designer-path reads went through the designer's own request, `POST /0/DataService/json/SyncReply/ProcessSchemaRequest`,
  issued from the designer page in Chrome. The response `resources` carry the captions.
* **Every prediction was written into the log before the step ran.** 18 of 20 predictions held. The two misses are
  recorded as misses: pair 3 was refuted, and Q5c is not explained by the final model (§6).
* Mechanism claims come from platform source `C:\Projects\Creatio2\TSBpm\Src\Lib`. **V** marks a line I read myself.
  **A** marks a claim reported by a second, independent reading of the source: it is consistent with every
  measurement, but I did not read the line myself.

## 3. Mechanism

### 3.1 The chain

1. **One shared resource cache per schema, keyed by UId.** `Schema.GetResourceManagerName()` returns
   `UId.ToString("N")` (`Terrasoft.Core/Schema.cs:586-592` **V**). Only `EntitySchema` overrides it
   (`Entities/EntitySchema.cs:2621` **V**), so a process schema's cache is keyed by UId. The cache is app-pool-wide
   and holds one resource set per culture until `ReleaseAllResources` (`SchemaResourceManager.cs:385-450` **A**).
2. **The package's design session reloads that cache from a pre-edit snapshot.**
   * `ProcessSchemaRepository.OpenDesignSession` → `Manager.DesignSchema`, then `GetDesignInstance`
     (`ProcessBuilder …/Schema/ProcessSchemaRepository.cs:213-220` **V**).
   * The platform stores the design item's metadata and resources in `SessionData` under
     `nameKey = designedItem.GetResourceManagerName()` (`SchemaManager.cs:4795-4806` **V**).
   * `FindDesignItem` then calls `UpdateResourceManager(resourceData, schemaName)`, which does
     `GetManager(schemaName).ReleaseAllResources()` followed by `LoadManagerResources` from the snapshot
     (`SchemaManager.cs:4627-4635`, `:2712-2720` **V**).
   * Result: the shared, UId-keyed cache now holds the callee's captions as they were **before** this edit.
3. **The save does not release that cache.** `SaveSchemaResourcesInDB` writes the rows and calls
   `ReleaseResourcesManagers(sysSchemaId)` (`SchemaManager.cs:2610-2620` **V**). That method releases
   `GetManager(name)` for every `SysSchema.Name` in the hierarchy (`:2622-2636`, `:1121-1128` **V**). A process
   cache is keyed by UId, so it is never released. For entity schemas the same code works, because their manager
   name is not the UId.
   * The save deletes and re-inserts every resource row per culture (`ConfigurationResourceDBWriter.cs:253-268`
     **A**). That is why a caller row's `ModifiedOn` moves even when its value stays old.
   * After commit, `MetaItems` is evicted and the runtime instance is dropped (`SchemaManager.cs:2376`,
     `:2311/2315` **A**).
4. **The first metadata build after the save captures the stale cache.**
   * `BaseProcessSchemaManager.GetItemFromMetaData` (`:949-968` **A**) builds the instance (build #1). Parameter
     captions load from the stale cache and are stored inside the instance.
   * `ReInitializeSchemaLocalizableValues` then reloads from the DB and releases the cache, but it resets only the
     schema-level Caption and Description (`SchemaManager.cs:2528-2538`, `Schema.cs:433-449` **A**).
   * Build #1 goes into `MetaItems`. A second build, which reads the now-fresh DB, is returned to that one call
     only. Every later `GetInstanceFromMetaData` returns build #1 until the next save of that schema.
5. **Every sync reads build #1.** `ProcessSchemaSubProcess` resolves the callee through `GetInstanceFromMetaData`
   for a metadata-deserialized element (`ProcessSchemaSubProcess.cs:160-172`). That is the case on the modify load,
   on the designer's server-side caller load and on describe's build of a caller.
   * `SynchronizeParameterCaption` copies `Caption.Value`, current culture only (`ProcessSchemaActivity.cs:173-177`).
   * A caption that is null in build #1 is never stored (`LocalizableValue.cs:323, 414`). A new parameter is built
     by the copy constructor and re-pointed at the caller's resources, where no key exists, so it reads `""`
     (`ProcessSchemaParameter.cs:199-200`, `ProcessSchema.cs:1397-1403` **A**).
6. **The designer is immune for three reasons.**
   * Its save calls `ProcessSchemaDesignerUtilities.ReleaseLocalizableValues(appConnection, schema)`, which
     releases `GetManager(schema.GetResourceManagerName())`, the UId-keyed cache, right after `SaveSchema`
     (`Terrasoft.Nui.ServiceModel/WebService/BaseProcessSchemaDesigner.cs:181-186`,
     `ProcessSchemaDesignerUtilities.cs:68-75` **V**). The same block carries
     `// TODO Cache management will be added in #CRM-28975`.
   * Its Sub-process card reads the callee through `ProcessSchemaRequest` → `TryDesignItem`, which releases the
     cache before building from the DB (`SchemaManager.cs:5104-5109` **A**). So the card is always fresh.
     Client side (`C:\Projects\PackageStore\CrtProcessDesigner\branches\7.8.0\Schemas\` **V**):
     * `SubProcessPropertiesPage.js:65-83` loads the callee (`getSubProcessSchemaInstanceByUId` →
       `forceGetInstanceByConfig`, `Terrasoft.Nui/Resources/Terrasoft/manager/process-schema-manager/process-schema-manager.js:203-212`
       **V**).
     * `initSchema` (`:145-161`) calls `synchronizeActualSchemaParameters` (`:111-126`), which rebuilds the element's
       parameters from the callee with `element.synchronizeParameters(actualSchema)`.
     * `RootUserTaskPropertiesPage.js:631-654` then carries over only uId, referenceSchemaUId, mapping value and
       isValid from the old parameters. The caption is the callee's.
   * Its caller save writes the client-sent captions as-is, with no server-side re-sync (**A**, consistent with
     pair 1 and Q5d).

### 3.2 Hypotheses considered

| Hypothesis | Verdict | Evidence |
|---|---|---|
| **H1**: re-read inside the uncommitted `Save` transaction from another connection sees the previous committed state | **Refuted as the cause.** | The reader uses `WITH (NOLOCK)` on MSSQL, which reads uncommitted rows (**A**). On the package path nothing re-reads during the transaction, because the cache already holds the snapshot. `ProcessSchemaManager.Save` (`:565-585`) has no callers under `TSBpm/Src` (**A**). The "one behind" comes from the pre-edit snapshot. |
| **H2**: a freshly built caption resolves to null and `SetCultureValue(null)` is ignored | **Refuted for finding A, partly right for B.** | The stale values are real strings. In finding A the second run moved the caller to save #1's value, which H2 cannot produce. For finding B a null in build #1 does produce the blank. |
| **H3**: different cache tiers disagree | **Confirmed, in this specific form.** | `MetaItems` (build #1, stale), the runtime instance (build #1 or #2, depending on the reader order) and the design item (`TryDesignItem`, always fresh) disagree by construction. |

The ticket's two unexplained observations are now explained:

* **The fourth run "without lag"** was an ADD-only save. Existing captions equalled the previous state, and the new
  one resolved fresh on the modify path. The lag was there but invisible. D4 reproduced it: in one save a changed
  caption lagged while an added one arrived.
* **The callee's own describe that stayed stale** depends on which instance became the runtime instance. If a
  sync built `MetaItems` first, describe gets build #1 (pair 2, stale for 10+ minutes). If describe ran first,
  it gets build #2, fresh, while the resync right after is still stale (D3).

## 4. Measurements

Pairs are `UsrCap<tag>Callee` / `UsrCap<tag>Caller`. "pkg" means a clio `modify-business-process`.

| Run | Steps | Prediction (before running) | Result |
|---|---|---|---|
| Pair 2 | pkg caption → pkg resync ×2 | caller row stays A1 | **held**: A1 both times (rows rewritten 11:26:38, 11:27:13) |
| Pair 2 | after the resyncs: designer read of callee, describe callee ×2 | read fresh, describe stale | **held**: read A2, describe A1 (11:27:32, 11:27:58) |
| Pair 2 | describe callee again 11 min later, no D2 writes, other schemas saved | still stale | **held**: A1 at 11:37:41 |
| Pair 1 | pkg caption → designer opens caller | server load stale, card fresh | **held**: load A1, card `D1 date A2` |
| Pair 1 | designer save of caller after opening the card | row becomes A2 | **held**: A2 (DisplayValue rewritten to `[#Delivery date#]`) |
| Pair 3 | designer caption change + save of callee → describe, pkg resync | stale (pre-model guess) | **refuted**: describe A2 and resync A2 on the first try, which led to the model |
| T1 | designer save of the stale D2 callee, no change → describe, pkg resync | fresh | **held**: A2, A2 |
| T2 | designer caption change on callee → designer read of caller, no caller save | load re-syncs → A3 | **held**: A3, while the DB row is still A2 |
| D2 (E1) | pkg caption → designer read of callee first → pkg resync | fresh | **held**: A2 on the first resync |
| D3 (E2) | pkg caption → describe callee → pkg resync → describe callee | fresh / stale / fresh | **held** all three |
| D4 (E4) | one pkg save changes a caption **and** adds a parameter → pkg resync | changed stale, added fresh | **held**: `E4 date A1`, `E4 urgent` |
| D5 (E3) | pkg add → describe callee first → describe callers B, A | callee fresh, both callers `""` | **held** |
| Pair 4 (B) | pkg add → describe caller B, then A, then B | `""` / fresh / `""` | **held** (finding B reproduced) |
| Q5b | pkg add → describe caller B (primed before the add) → pkg incidental edit of B | row present | **held**, but the blank precondition was not met: B's cached instance predated the add (`inSync false`) |
| Q5c | pkg add → **first** reader is a pkg incidental edit of caller A | 50/50 | row present, fresh (`D4 flag4`). **Not explained by the model** (§6) |
| Q5d | pkg add → **first** reader is the designer's caller load → designer save without opening the card | blank persisted | **held**: no `Flag5` caption row. Describe afterwards shows the caption, because read-time sync masks the missing row |
| R1 | D1 caller holds a correct A2 row; pkg incidental edit of the caller (callee untouched for 30 min) | reverted to A1 | **held**: `D1 date A2` → `D1 date A1` at 11:55:46 |

The full log, with timestamps and every prediction as written before its step, is
`eng-100077-subprocess-caption-sync-measurement-log.md`. The rows above were copied from it.

## 5. The questions this research had to answer

1. **Mechanism:** §3. It is established from source, and every non-trivial model prediction on the stand held
   (D2–D5, T1, T2, R1).
2. **Parity with the designer:**
   * The designer by itself does **not** lag. Its card is always current. Its caller save persists what it holds:
     current after the card was opened, and whatever the server load returned if it was not.
   * The designer's **server-side** load of a caller does show a stale caption, and a blank one for a new
     parameter, but only when the callee was last saved by the package.
   * So this is a defect in CrtProcessBuilder's save path. The platform's by-name release is a latent platform
     gap that the designer works around, and the `CRM-28975` TODO acknowledges it.
3. **.NET Core stand:** not checked, by the owner's decision (2026-09-23). The chain lives in `Terrasoft.Core`,
   which is shared by both runtimes, so the same behaviour is expected but unmeasured. A web farm keeps one cache
   per node. Remote nodes only get an item invalidation, and their cache is normally empty at rest (**A**).
4. **Fix options:** in the plan.
5. **Can an incidental save pin `""`?**
   * **Yes, through the designer** (Q5d): open the caller before anything else has read the callee, then save
     without opening the Sub-process card.
   * Through clio: not observed in 2 runs (Q5b, Q5c). Q5b did not meet the precondition. Q5c is the unexplained
     miss in §6.
   * A persisted blank is invisible to `describe` and to the card, because both re-sync on read. It is still wrong
     in the DB and in any package export, which carries the resource rows.
6. **Tests:** in the plan.
   * The package's unit harness (`SubProcessTestSupport`, there is no `TestSchemaManager`) cannot model the shared
     resource cache, so the regression must be an E2E.
   * By the model the failure is deterministic today: package set → package resync fails every time unless
     something calls `TryDesignItem` on the callee in between.

## 6. What is still open

* **Q5c.** The model predicts a blank for a parameter added in a package save when the first reader is a package
  modify of a caller. The measured result was the fresh caption. The describe path (pair 4, D5) and the
  designer's load (Q5d) did give the blank.
  * Candidate explanation, not verified: something on the modify path fills build #1's null caption before the
    element copies it, for example a reader call to `GetInstanceByUId(callee)`, which creates the runtime
    instance from build #1 and fills its nulls from the now-fresh cache.
  * To settle it, repeat Q5c three times on fresh fixtures, and once with the package's describe-side reader
    disabled.
  * **The plan does not depend on it.** The root fix removes build #1's staleness, whatever the order of readers.
* The number of IIS worker processes on the stand was not checked, because there is no server access. A web
  garden would give each worker its own cache.
* Feature `KeepProcessSchemaInstanceInProcessSchemaSubProcess` is not registered on the stand, so the default
  applies (it caches the callee on the element, `ProcessSchemaSubProcess.cs:100-103`). Its effect with the feature
  on was not measured.
* **Known limitation of the caption report, not fixed (peer review of the delivered PRs, finding 6).** The stored
  side is read by the caller's `SysSchema.Id` alone, with no `SysPackageId` filter, unlike the platform's own
  `HierarchySchemaResourceReader` (`:210-238`). A schema saved with `ExtendParent` keeps its resources under the
  PARENT's `SysSchemaId` (`SchemaManager.cs:2680-2684`), so for such a caller the element reads as never stored and
  its captions are reported as unknown: no replaced and no filled-in notice, while the synchronization still writes
  the callee's captions. Rare for processes, and the reviewer's own confidence was low; the fix itself (R) does not
  depend on it. Revisit if a caller that replaces a schema from another package shows a silent resync.
* **Known limitation, not fixed (second review round, Low).** Whether an element is stored is decided by NAME,
  because the resource keys are: any row under `BaseElements.<element>.`, in any culture. A batch that removes an
  element and adds another under the SAME name, then re-synchronizes it, therefore compares the new element with
  the removed one's stored captions and can announce them as replaced. Telling the two apart needs the element
  UIds the request started with, which the reader does not have; the case needs three operations in one batch
  and changes nothing that is saved.
* **A second toolset save of a callee that is already stale** was measured only in the ticket's shape, a save that
  changed the captions AGAIN: it left the callers one save behind again. Whether a save that changes no caption
  catches them up was not measured. By the model it would (the new design session's snapshot is taken after the
  previous save), but the guidance names only the measured cure, a designer save of the callee.

## 7. Side observations (out of scope, recorded so they are not rediscovered)

* `setElement {"elementUpdate":{"caption":…}}` on a Sub-process element is refused: "specifies no field to change".
  The refused call saved nothing, but it did rebuild the caller's cached instance: the next describe showed fresh
  captions.
* The designer writes a mapped parameter's `DisplayValue` row as `[#Delivery date#]`. The package writes
  `Delivery date`.
* The platform's sync copies the **current culture** only (`ProcessSchemaActivity.cs:173-177`), so a caption
  written server-side exists in the writer's culture only - `en-US` on this stand. Other cultures keep whatever
  they had, and no fix proposed here changes that.

## 7a. Shipped corpus

`C:\Projects\PackageStore` holds 381 sub-process elements. Of 1,661 en-US element parameters compared with the
callee's caption:

* 81% are equal.
* **18% have no caption on the caller**, mostly old elements (2015–2020) that store none.
* 0.5% differ: 7 of these 8 are one caller with deliberately edited captions, `CreatioAISuggestCaseResolution`.

Stale captions are therefore not a shipped-data problem. They are created on stands by package saves. Details and
their consequences for the report are in plan §10.

## 8. Fixtures left on the stand (package `Custom`)

`UsrCapD1Callee/Caller`, `UsrCapD2Callee/Caller`, `UsrCapD3Callee/Caller`, `UsrCapD4Callee`, `UsrCapD4CallerA/B`,
`UsrCapD5Callee/Caller` (created, not used), `UsrCapE1…E4Callee`, `UsrCapE1/E2/E4Caller`, `UsrCapE3CallerA/B`.
Their state after the runs:

* `UsrCapD1Caller` is intentionally reverted to `D1 date A1` (R1).
* `UsrCapD2Callee` was cured by T1.
* `UsrCapD4CallerB` has no `Flag5` caption row (Q5d).

The older `UsrCapM*` fixtures from the ticket's reproduction were not touched.
