# Business process versioning — traps that fail silently

> Jira: [ENG-94374](https://creatio.atlassian.net/browse/ENG-94374). Companion to
> [`business-process-versioning-plan.md`](business-process-versioning-plan.md).
>
> **Read this file before writing any code.** Every item below was found by adversarial verification against the
> platform source, and every one of them **saves green, compiles, renders correctly in the designer and passes a
> response-payload assertion** while being wrong. None is catchable by a unit test in the ProcessBuilder harness.

---

## BLOCKER 1 — `Version` / `IsActiveVersion` set on the instance are never persisted

`Terrasoft.Core/SchemaManager.cs:3029`, verified verbatim:

```csharp
protected virtual void SaveExtraProperties(ISchemaManagerItem<TSchemaManagerSchema> item, Guid sysSchemaId) {
    if (item.ExtraProperties.Count == 0) {
        return;
    }
    foreach (ExtraProperty extraProperty in item.ExtraProperties) { … }
}
```

It iterates **`item.ExtraProperties`**, never `item.Instance`. `ExtraProperties` is populated only by
`AssignSchema` (`SchemaManagerItem.cs:521-529`, `ExtraProperties.ParseObject(source)`), reachable only through
the public `Assign(TSchemaManagerSchema)` at `SchemaManagerItem.cs:649`. The `Instance` setter
(`SchemaManagerItem.cs:130-133`) does **not** call it, and `InternalCreateSchema` (`SchemaManager.cs:2636-2639`)
only assigns `newSchemaManagerItem.Instance = schema`.

**So for a freshly created item `ExtraProperties.Count == 0` and `SaveExtraProperties` returns immediately.**

`GetMaxProcessVersionInPackage` reads `SysSchemaProperty` where `Name = 'Version'`
(`BaseProcessSchemaManager.cs:1285-1292` + the select at `:375-399`), and `GetActiveVersionItem`'s ordering reads
`i.FindPropertyValue(x => x.Version, 0)` off the manager item (`:575-586`).

### How it presents

Version 1 is created and **looks perfect in the metadata body** (the value *is* written into
`SysSchema.MetaData`). The **second** `create-process-version` then computes `max = 0` again, generates the
identical name `<root><Pkg>1`, and is rejected by the handler's own `ProcessExists` pre-check — surfacing as
*"a process named X already exists"* with no hint that the cause is a missing property row.

### Fix

Do **not** rely on the instance write. Either:
* call the public `item.Assign(item.Instance)` (`SchemaManagerItem.cs:649`) immediately before `Save` to refresh
  `ExtraProperties` from the mutated instance, **or**
* set the properties through `item.SetPropertyValue(s => s.Version, n)` **after** the first successful save.

> `SetPropertyValue` **before** the save is a silent no-op: `SaveMetaDataValue`
> (`SchemaManagerItem.cs:478-492`) calls `Manager.GetItemFromMetaData(UId, …)` and returns early when the schema
> is not yet in the database.

### Acceptance check

Read `SysSchemaProperty` for the new schema Id on a stand, **and run `create-process-version` twice
consecutively** — the second must yield `Version = 2`.

---

## BLOCKER 2 — the `GetMetaItems` UId rewrite is incomplete

`Terrasoft.Core/Process/ProcessSchema.cs:1541-1546`, verified verbatim:

```csharp
public override void GetMetaItems(ICollection<IMetaItem> metaItems) {
    base.GetMetaItems(metaItems);
    LaneSets.GetMetaItems(metaItems);
    Artifacts.GetMetaItems(metaItems);
    FlowElements.GetMetaItems(metaItems);
}
```

The base (`Schema.cs:848-853`) adds only `this`, `Methods`, `LocalizableStrings`, `Usings`.
`BaseProcessSchemaItem.GetMetaItems` (`Process/BaseProcessSchemaItem.cs:133-135`) adds only `this`.

**No override anywhere under `Terrasoft.Core/Process` enumerates `Parameters` or `ExecutionContexts`.**
(Verified by grepping every `GetMetaItems` in that folder: only `ProcessBasedSchema`, `ProcessSchema`,
`ProcessSchemaLane`, `ProcessSchemaLaneSet`, `ProcessSchemaSubProcess` override it, none touching those.)

The ticket's own v0 payload contains `NotificationCaption` — a schema-level `ProcessSchemaParameter` carrying
`CreatedInSchemaUId` / `ModifiedInSchemaUId` — which the **client** rewrite *does* change, because
`copySchema` does a blanket replace over the serialized JSON.

Anything the walk misses keeps the source `CreatedInSchemaUId`, and
`ProcessSchemaBaseElement.IsInherited` = `!CreatedInSchemaUId.Equals(ParentMetaSchema.UId)`
(`Process/ProcessSchemaBaseElement.cs:109-120`) therefore reads it as **inherited from the source schema**.

> A typed `GetMetaItems` walk is **not** parity with `copySchema`. Do not claim it is.

### Fix — pick one, and prove it by diff

* **Typed:** additionally enumerate schema `Parameters`, `ExecutionContexts`, and each
  `IProcessParametersMetaInfo.Parameters` on every flow element / lane / activity
  (`ProcessSchemaParametrizedFlowNode.cs:43`, `ProcessSchemaEvent.cs:95`, `ProcessSchemaActivity.cs:113`).
* **Serialized:** do the rewrite on the serialized metadata with a targeted replace of the short keys
  **`A3` / `A4` only** (`MetaItem.cs:81,88`).

**Never** use the platform's blunt whole-UId `metaData.Replace` — it also rewrites `CreatedInOwnerSchemaUId`
(`BL8`, `ProcessSchemaBaseElement.cs:41`), which turns the version into a **copy**.

Proof must be a **field-by-field diff against a designer-created version**, not a unit test.

---

## BLOCKER 3 — the native `SetIsActualVersion` endpoint is unauthenticated for design rights

See [`…-research.md` §3.1](business-process-versioning-research.md). `BaseProcessSchemaManagerService.cs:56-66`
has **zero** rights checks, and `SetActiveVersionItem` (`BaseProcessSchemaManager.cs:1317-1328`) has none either
— unlike `EnableProcess`.

Wrapping that endpoint directly in an MCP tool would let any authenticated caller re-point which process version
the whole environment executes, bypassing `IProcessDesignGuard` entirely.

**Fix:** route activation through a `CrtProcessBuilder` operation so `EnsureCanManageProcessDesign()` runs first.
If the native endpoint is used as a stop-gap anyway, clio must first prove the caller holds
`CanManageProcessDesign` via `KnownRoute.RightsGetCanExecuteOperation = 58`
(`clio/Common/ServiceUrlBuilder.cs:210`) and refuse otherwise — **and record it in the ADR as a deliberate
security decision.**

---

## BLOCKER 4 — `IsInherited` is memoised on first read

`Process/ProcessSchemaBaseElement.cs:106-120` caches into `_isInherited` behind `_isIsInheritedInitialized` on
first access and never re-evaluates.

Immediately after `manager.CreateSchema(name, source, …)` every cloned child still carries the **source**
`CreatedInSchemaUId`. Any read of `IsInherited` between the clone and the rewrite latches `true` permanently.
`InternalCreateSchema` calls `schema.SetDefInheritance()` (`SchemaManager.cs:2747`) and the
`ProcessSchema(ProcessSchema source)` copy ctor (`ProcessSchema.cs:139-184`) rebuilds the element collections —
neither path has been proven free of an `IsInherited` read.

**Fix:** either establish empirically that no read occurs before the rewrite, or avoid the in-memory clone for the
rewrite entirely and go **serialize → rewrite → `ReadMetaData`**, which constructs fresh elements with the
already-corrected `CreatedInSchemaUId`.

---

## Trap 5 — `IsActiveVersion` defaults to `true`

`BaseProcessSchema.cs:401-403` declares `public bool IsActiveVersion { get; set; } = true;` and `:1116` **omits it
from serialization when true**.

A create path that forgets the explicit `= false` ships a version that is **born active and immediately hijacks
every new instance** — and the omission leaves no trace in the payload, because the default is exactly what is
not serialized.

---

## Trap 6 — `GetMaxProcessVersionInPackage` takes `SysSchema.Id`, **not** the UId

`BaseProcessSchemaManager.cs:1285-1292` binds the value to a `@ParentId` parameter compared against
`VwSysSchemaInWorkspace.ParentId`.

Passing the UId **compiles, runs, and silently returns 0** — so the new version is numbered 1 and named
`<root><Pkg>1` again, colliding with the existing v1. The collision surfaces only at save time, *after* the draft
item exists, so rollback must cover it. A one-character mistake with no compile-time signal.

---

## Trap 7 — the caption is regenerated and must be restored on **both** objects

`InternalCreateSchema` overwrites the cloned schema's caption when `fromMetaData == false`
(`SchemaManager.cs:2748-2752`: `SchemaUniqueNameGenerator.CreateCaption(...)`), and the public 5-arg
`CreateSchema` (`:4457-4466`) leaves `fromMetaData` at its `false` default — so the regeneration **always**
happens.

The persisted caption column is written from the **manager item**, not the instance:
`sysSchema.SetColumnValue("Caption", item.Caption)`.

**Fix:** set **both** `item.Caption` and `item.Instance.Caption` from the source, and assert on a stand that the
version's caption in the process library matches the source's.

---

## Trap 8 — `GetItemByUId` / `GetAllVersionItems` throw, they do not return null

`BaseProcessSchemaManager.GetAllVersionItems(Guid)` (`:1375-1378`) opens with `GetItemByUId(schemaUId)`, which
throws `ItemNotFoundException` on a miss. The tolerant sibling is `Manager.FindItemByUId`
(`public virtual TItem`, `Manager.cs:220-223`), which returns `default`.

**Fix:** pre-validate every caller-supplied UId with `FindItemByUId` and return the package's own not-found
message. Wrap the describe version block in its own `try`/`catch` that **degrades to absent** rather than failing
the graph read. A previously working `describe` must never start failing because a version read failed.

---

## Trap 9 — the active-version cache is invalidated on **activate**, not on **create**

`BaseProcessSchemaManager.cs:1345-1358` — `GetActiveVersionItem` short-circuits when `allItems.Count <= 1` and
otherwise memoises per root into `_activeVersionItemsCache`. The cache is cleared by `OnProcessVersionChanged`,
which only `SetActiveVersionItem` raises (`:1324-1326`).

Before the first version exists the family has one member and the short-circuit returns without caching; after a
create the family has two and the freshly computed answer is cached. **A create → describe read-back can
therefore report a stale active version**, which will be misdiagnosed as a rewrite bug.

**Fix:** determine on a stand whether a create can be observed stale; either re-resolve after save or document the
window. Do not build the acceptance test on an unexamined cache.

---

## Trap 10 — `SetActiveVersionItem` is an O(N) schema save that swallows partial failure

`SchemaManagerItem.SetPropertyValue` (`:707-730`) routes to `SaveMetaDataValue` → `Manager.SaveSchema(…,
lockSchemaInSourceControlStorage: true)` **per sibling** on an editable package, and to a `SysSchemaUserProperty`
write on a foreign one — regenerating sources per version inside one DB transaction.

Worse, `BaseProcessSchemaManager.cs:518-519` **catches `SetSchemaPropertyException` for the versions being
DEACTIVATED and only logs it.**

Concrete failure: activate v2 in `Custom` while v1 sits in a delivered package → v1's deactivation is skipped,
v2's activation succeeds, **two versions carry `IsActiveVersion = true`**, and which one runs is then decided by
`PackagePosition`.

**Any activate operation MUST read the active version back and fail when it is not the requested one.**
It must also own its own timeout contract — `clio/Command/McpServer/AGENTS.md` forbids a destructive tool
routing through the 120 s read deadline.

---

## Trap 11 — an older `CrtProcessBuilder` drops an unknown member **in silence**

No contract in the package implements `IExtensibleDataObject`, so a request carrying a member the installed
package does not know is answered **normally** with the member ignored.

Had versioning been expressed as `saveAsNewVersion: true` on `ModifyProcessRequest`, an environment one release
behind would have **edited the current version in place and reported success** — a green log and a wrong process.

**Therefore: the write path must be a new OPERATION NAME, never a new field on an existing request.** An old
package rejects an unknown operation loudly, by name.

---

## Trap 12 — "created" is not "runnable"

`ProcessSchemaManager.Save` only generates sources (`:584`); workspace compilation is a separate explicit
`Publish` that `CrtProcessBuilder` never performs. An interpretable process runs anyway, but a version whose
validation reports `IsInterpretable = false` is created, reported as success, and **cannot execute** until
something else compiles the configuration.

The create response must carry that as a warning, or the caller has no way to know.

---

## Trap 13 — `Enabled` is family state, not per-version state

`BaseProcessSchemaManager.EnableProcess` / `GetIsProcessEnabled` key on the **root** `SysSchema.Id`
(`:1232-1266`), and `VwProcessLib.Enabled` is computed from `SysProcessDisabled` matching **either** the row's Id
**or** its `ParentId`.

Reporting `enabled` on a per-version entry without documenting that it is family state — or ever accepting it as
a per-version input — is a semantic bug that reads correct.

---

## Trap 14 — two authorities disagree on which version is active

| Authority | Ordering |
|---|---|
| `VwProcessSchemaVersion` SQL view | `IsActiveVersionUserProperty` desc, `IsActiveVersionSchemaProperty` desc, **`PackageLevel`** desc, `PackageName` asc, `Version` desc, `SchemaName` |
| `BaseProcessSchemaManager.GetActiveVersionItem` (`:575-586`) | same first two, then **`PackagePosition`** desc, `Version` desc, `Name` asc |

**The runtime consults the manager** (`ProcessEngine.CheckCanRunProcessSchema` → `GetIsActiveVersion`). A
clio-side read of the view can therefore report a version as active that the runtime will not start, on a family
with no explicit flag and versions spread across packages.

**Fix:** document that the read-only answer is the process-library view's; moving the read server-side (Stage B)
removes the divergence.

---

## Trap 15 — concurrent create has no database guard, and bursts take the stand down

No uniqueness constraint on `(SysSchema.ParentId, Version)` or on name-per-package was found in the C# source.
Two concurrent calls both read `max + 1`, compute the same `Version` and the same `Name`; one wins the platform's
name-uniqueness check and the other throws late, after its draft exists.

Independently, per operational experience on this stand family, **a burst of parallel schema writes trips IIS
rapid-fail and downs the .NET Framework app pool.** Any test harness or agent that fans out version operations
can take the environment down rather than merely failing. Run schema-write operations **sequentially**.

---

## Trap 16 — delete-a-version is data-destructive and has no product concept

`RemoveItemByUId` fires `BeforeRemoveProcess` (`:462-477`), which sets **every `SysProcessLog` row for that schema
to Cancelled** and deletes its runtime data, to-dos, persistent store, flow schema and rights.

The product exposes no delete-a-version anywhere: `ProcessVersionsDetail` explicitly disables Add/Edit/Copy/Delete,
and Academy states the version history *"cannot be edited"*.

**Out of scope permanently, not just deferred.** "Manage version history" in this domain means **read and
activate**, never delete.

---

## Trap 17 — rebundle traps, each costing a full configuration build to diagnose

1. `-Version` **must go up** or an environment that already recorded `1.1.0.0` is never offered the update — the
   fix silently does not ship.
2. An install command resolves the bundled archive from the **build output** directory, so a rebundle has no
   effect until clio is rebuilt.
3. `install-process-builder --force` is required whenever the environment records an equal-or-higher version.
4. The archive must stay **source-only** — a stray `Files/Bin` artifact breaks the shipped-inventory check.
5. **Rebundle only from a clean checkout of ProcessBuilder `main`.** A tree carrying just-written files produces a
   SHA nobody can reproduce.

---

## Trap 18 — branch hazard, current as of 2026-08-13

| Repo | Branch | State |
|---|---|---|
| `C:/Projects/clio` | `feature/ENG-91845-sysvariable-connection-docs` | **dirty** — `ModifyBusinessProcessPrompt.cs`, `ModifyBusinessProcessTool.cs`, `DescribeProcessTool.cs`, `spec/sprint-status.yaml` modified; `spec/stories/story-process-element-connections-6.md` untracked. HEAD `9b42e5c23`, unmerged. |
| `C:/Projects/workspace/ProcessBuilder` | `feature/eng-91845-sysvariable-validation` | HEAD `e8ee199` *"hold a SysVariable connection to the platform's own vocabulary"* — **not present on `main`** |

**A rebundle run from this ProcessBuilder checkout would ship that unreleased ENG-91845 commit into clio's
archive.** `DescribeProcessTool.cs` is also exactly the file Stage A edits, so the versioning work must branch
from `origin/master` **after** ENG-91845 lands, or be explicitly rebased onto it.

Sequence against the ENG-91845 rebundle (`spec/sprint-status.yaml`: `story-process-element-connections-4` at
`review`, `-5` at `in-progress`), or one archive ships half of each feature.
