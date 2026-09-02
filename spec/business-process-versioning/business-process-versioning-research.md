# Business process versioning — research findings

> Jira: [ENG-94374](https://creatio.atlassian.net/browse/ENG-94374) — *Business process versioning (create new version / manage history)*
> Split from ENG-91852 (Task 14 of the ProcessDesignService backlog). Parent research: ENG-90883.
> Date of research: 2026-08-13. Every claim below carries a `file:line` citation and was spot-verified against
> the checked-out trees.

## 0. Source roots used

| Root | Role |
|---|---|
| `C:/Projects/PackageStore/CrtProcessDesigner/branches/7.8.0` | The product process-designer package |
| `C:/Projects/Creatio/TSBpm/Src/Lib` | Platform server C# **and** the designer's client JS (`Terrasoft.Nui/Resources/Terrasoft/**`) |
| `C:/Projects/workspace/ProcessBuilder/packages/CrtProcessBuilder/Files/src` | The AI toolkit server package |
| `C:/Projects/clio` | The CLI + MCP server exposing the toolkit to agents |

---

## 1. Where the versioning implementation actually lives

**Not in `CrtProcessDesigner`.** A repo-wide grep of the 7.8.0 branch for
`saveNewSchemaVersion|NewVersion|CreateVersion|ActiveVersion|MakeActual|Custom1` returns only:

* `Schemas/SaveSchemaVersionMessageBox/SaveSchemaVersionMessageBox.js:47-54` — the confirmation dialog. It
  collects two booleans and publishes them; it contains no algorithm.
* `Schemas/InplaceProcessSchemaDesignerViewModel/InplaceProcessSchemaDesignerViewModel.js:250-265` — a
  `saveNewSchemaVersion` override that only injects `config.sysPackage` / `config.canEditPackageSchema` and
  calls the parent.
* The `VwProcessSchemaInfo` / `VwProcessSchemaVersion` SQL views, which compute which row is the active version.
* `Data/AdminUnitFeatureState_SaveProcessVersionInApplicationPackage/**` — a feature record, enabled by
  default for "All employees".

**The algorithm lives in the platform client**, under
`C:/Projects/Creatio/TSBpm/Src/Lib/Terrasoft.Nui/Resources/Terrasoft/`:

* `manager/base-process-schema-manager/base-process-schema-manager.js` — clone, number, name, re-parent.
* `designers/base-process-schema-designer/base-process-schema-designer-view-model.js` — the save / new-version
  orchestration and the confirmations.

`Terrasoft.ProcessSchemaManager.getCanUseProcessVersions()` returns `true`, which is what enables the feature
for business processes at all.

### 1.1 The decisive structural fact

**There is no server API that creates a process version.** A new version is composed entirely **client-side**
and then persisted through the ordinary schema save. The only server-side clone,
`BaseProcessSchemaManager.SaveClonedSchema` (`Terrasoft.Core/Process/BaseProcessSchemaManager.cs:892-918`), is
`protected` and deliberately **resets** versioning (`Version = 0; ParentSchemaUId = DefSchema.UId;
IsActiveVersion = true`) — it implements **Copy**, not **Version**. `CloneSchemaUsingMetaData` (:532-555) is
`private` and does a blunt whole-UId `metaData.Replace`, which also rewrites `CreatedInOwnerSchemaUId`
(meta key `BL8`, `Process/ProcessSchemaBaseElement.cs:41`) — the one field a version must leave pointing at
the original.

Only three versioning REST operations exist, all on `ServiceModel/ProcessSchemaManagerService.svc`:
`GetSchemaVersionInfo`, `SetIsActualVersion`, `GetActualVersionUId`.

---

## 2. The client algorithm, step by step

### 2.1 Clone — `BaseSchemaManager#copySchema`

`Terrasoft.Nui/Resources/Terrasoft/manager/base-schema-manager/base-schema-manager.js:1438-1448`

```js
copySchema: function(sourceSchema) {
    const schemaUId = Terrasoft.generateGUID();
    let config = {};
    sourceSchema.getSerializableObject(config);
    const propertiesToReplace = ["createdInSchemaUId", "modifiedInSchemaUId"];
    config = this.replaceObjectProperty(config, propertiesToReplace, sourceSchema.uId, schemaUId);
    config.uId = schemaUId;
    …
}
```

A **new schema UId** is minted and only `createdInSchemaUId` / `modifiedInSchemaUId` are string-replaced across
the serialized object. `createdInOwnerSchemaUId` and every child element `uId` are deliberately left untouched.
This reproduces the observed delta between the two payloads in the ticket exactly.

### 2.2 Re-parent — `BaseProcessSchemaManager#createNewSchemaVersion`

`…/base-process-schema-manager.js:84-94`

```js
schema.parentSchemaUId = this.getIsSetParentSchemaUId(sourceParentUId) ? sourceParentUId : sourceSchema.uId;
schema.setPropertyValue("isActiveVersion", false);
schema.setPropertyValue("isDelivered", false);
managerItem.parentUId = schema.parentSchemaUId;
```

and `getIsSetParentSchemaUId` (:73-75):

```js
return parentSchemaUId && !Terrasoft.isEmptyGUID(parentSchemaUId) && parentSchemaUId !== this.defSchemaUId;
```

with `defSchemaUId: "bb4d6607-026b-4b27-b640-8f5c77c1e89d"` (`manager/process-schema-manager/process-schema-manager.js:61`).

> **The version family is FLAT, not a chain.** Every version points at the **root**, never at the previous
> version. The ticket's payload shows v1's parent = v0's UId only because v0 *is* the root. v2's parent will
> again be v0, not v1. `GetAllVersionItems` filters `ParentUId == root`, so building history by walking parents
> is wrong.

The platform's base `Process` schema UId `bb4d6607-026b-4b27-b640-8f5c77c1e89d` is treated as **"no parent"**.
That is why v0 in the ticket carries `ParentSchemaUId: bb4d6607-…` yet v1's parent becomes v0's own UId.

### 2.3 Number and name — `getNewSchemaVersion` / `setNewSchemaVersionName`

The client asks the server `GetSchemaVersionInfo(parentSchemaUId, packageUId)`, sets
`version = maxVersionInPackage + 1`, then (`…/base-process-schema-manager.js:33-40`, verified verbatim):

```js
setNewSchemaVersionName: function(schema, versionInfo, packageName) {
    packageName = packageName.replace(/\W/g, "");
    schema.name = versionInfo.parentSchemaName + packageName + schema.version;
    if (this.schemaNamePrefix && this.schemaNamePrefix.length > 0 &&
        !Ext.String.startsWith(versionInfo.parentSchemaName, this.schemaNamePrefix)) {
        schema.name = this.schemaNamePrefix + schema.name;
    }
}
```

> **`Custom` in `UsrProcess_0370312Custom1` is the PACKAGE NAME, not a literal suffix.** A version created in
> package `UsrMyApp` is named `UsrProcess_0370312UsrMyApp1`. Nothing server-side validates the shape; this is a
> pure client convention.

### 2.4 Save and activate are two separate gestures

The new schema is persisted through the ordinary `item.save()`. **Activation is a separate, explicit step**:
after saving, the designer asks *"Set the current version of process \"{0}\" actual?"* and only then calls
`SetIsActualVersion`.

### 2.5 Where the version list UI lives

Not in the designer. It is the **`ProcessVersionsDetail`** grid on the process-library page (`ProcessLibrary`
package), which explicitly **disables Add, Edit, Copy and Delete** and offers only *"Set as actual version"* and
*"Open in designer"*. **There is no delete-a-version affordance anywhere in the product.**

### 2.6 The `SaveProcessVersionInApplicationPackage` feature flag

It decides only **where** the new version lands: enabled (the shipped default) → the design/application package
of the source schema (via `ApplicationPackagesService.svc/GetDesignPackageUId`); disabled → the current/`Custom`
package.

---

## 3. Platform server model

| Member | Location | Meaning |
|---|---|---|
| `Version` | `Terrasoft.Core/Process/BaseProcessSchema.cs:401-403` region | The version number. Persisted as a `SysSchemaProperty` row. |
| `IsActiveVersion` | `BaseProcessSchema.cs:401-403` | **Defaults to `true`** and is omitted from serialization when true (`:1116`). |
| `ParentSchemaUId` | `ProcessSchema` | The **root** of the version family (see 2.2). |
| `CreatedInOwnerSchemaUId` | `Process/ProcessSchemaBaseElement.cs:41` (meta key `BL8`) | The inheritance origin. A version must **not** rewrite it. |
| `IsInherited` | `Process/ProcessSchemaBaseElement.cs:106-120` | Computed as `!CreatedInSchemaUId.Equals(ParentMetaSchema.UId)` and **memoised on first read**. |

Key methods on `BaseProcessSchemaManager`:

| Method | Line | Virtual? |
|---|---|---|
| `SetActiveVersionItem` | 1317 | `public virtual` |
| `GetActiveVersionItem(item)` | 1345 | `public virtual` |
| `GetAllVersionItems(Guid)` | 1375 | `public virtual` |
| `GetIsActiveVersion(Guid)` | 1273 | `public`, **not virtual** |
| `GetMaxProcessVersionInPackage` | 1285 | `public`, **not virtual** |
| `GetActiveVersionItemByUId(Guid)` | 1335 | `public`, **not virtual** |
| `GetRootSchemaUId` | 1056 | **`internal`** — not reachable from a configuration package |

"Active version" is **a computed ordering, not a stored flag**: user property → schema property →
`PackagePosition` desc → `Version` desc → `Name` (`BaseProcessSchemaManager.cs:575-586`). It must be derived as
`item.UId == GetActiveVersionItem(item).UId`, never read off `schema.IsActiveVersion`.

### 3.1 The native activation endpoint has **no authorization check**

`Terrasoft.Core.ServiceModel/BaseProcessSchemaManagerService.cs:56-66`, verified verbatim:

```csharp
public BaseResponse SetIsActualVersion(string schemaUId) {
    var result = new BaseResponse();
    try {
        schemaUId.CheckArgumentNullOrEmpty("schemaUId");
        ISchemaManagerItem<TSchema> schemaItem = Manager.GetItemByUId(new Guid(schemaUId));
        Manager.SetActiveVersionItem(schemaItem);
    } catch (Exception exception) { result.SetDesingTimeException(exception); }
    return result;
}
```

A grep of that entire file for `CanManageProcessDesign|GetCanExecuteOperation|UserType.General|SecurityException`
returns **zero** matches, and `SetActiveVersionItem` itself has no `CheckOperationUserRights`. **Exposing this
endpoint directly through MCP would ship an ungated privileged write.** Activation must be routed through
`CrtProcessBuilder` so `IProcessDesignGuard.EnsureCanManageProcessDesign()` runs first.

---

## 4. What Academy says this is for

Creatio documents the feature as **"Process version control"**, introduced in **7.17.0**.

* **Purpose** — [Process Designer basics](https://academy.creatio.com/docs/8.x/no-code-customization/bpm-tools/business-process-setup/process-designer-basics):
  *"Process version control in Creatio ensures that business process revisions and updates do not disrupt any
  active process instances."*
* **The gesture** — click Save, then choose **"Save new version"** vs **"Save current version"**. Two distinct
  operations.
* **Blast radius** — *"The new version replaces the earlier process versions in every place that uses the process
  schema, for example, sub-processes. However, the active instances of the process continue to work according to
  the version in which they were launched."*
* **In-place save is the risky one** — *"If the process has active instances, they might be stopped when you save
  the changes."* (i.e. plain `modify-business-process` is documented as *more* dangerous than creating a version.)
* **Package placement** — *"If the process package is non-editable, Creatio asks if you want to save the new
  business process version. After you confirm this, Creatio saves the new version to the package specified in the
  Current package system setting."* This is why the ticket's screenshot 2 shows Package `Custom`.
* **History UI** — [View process properties](https://academy.creatio.com/docs/8.x/no-code-customization/bpm-tools/business-process-administration/view-process-properties):
  *"The Process versions tab displays information about process versions. The data cannot be edited and are added
  to the detail automatically, each time a new process version is saved."* Columns: title, date saved, package,
  version number, and the currently-used flag.
* **Activation** — *"Only one of the versions of the same process can be set as actual. Any version can be used as
  a sub-process."* / *"All new instances of this business process will be run using the actual version."*
* **The WHY, verbatim** — [7.17.0 release notes](https://academy.creatio.com/docs/8.x/resources/release-notes/7170-release-notes):
  *"You can now save a modified business process as a new version regardless of whether any of its instances have
  been run. This enables tracking change history details and returning to any of the previous versions if needed."*
  → safe live iteration + change history + rollback.

### 4.1 Two documentation facts that contradict naive assumptions

1. **Version numbering is documented as package-scoped, not process-scoped**:
   *"Creatio numbers the versions within a single package consecutively, i.e., the new process version is greater
   than the last saved version of any process in the package by 1."*
   This conflicts with the per-`(root, package)` `MAX+1` the code computes. **Never compute or assert a version
   number client-side — read back whatever the platform assigned.** Worth validating on a stand with two
   processes in one package.
2. **The `Custom<N>` name suffix is undocumented.** No Academy page mentions it. It is an implementation detail
   of the client, established here from source only — do not treat it as a stable public contract.

### 4.2 Not documented anywhere on Academy

Deleting a version (the history detail is explicitly read-only); `SysProcessLog` behaviour across versions; the
`SaveProcessVersionInApplicationPackage` flag.

> **Dynamic cases are different and must not be generalised from.** The same release note says moving a running
> *case* to a new version **cancels the current instance**. For business processes the docs never describe
> migrating a running instance. A process-versioning tool must not offer it.

---

## 5. Current state of the AI toolkit

**`CrtProcessBuilder` has zero versioning awareness.** A grep of all 85 C# sources under
`packages/CrtProcessBuilder/Files/src` for `Version|IsActiveVersion|ParentSchemaUId|Custom\d` matches only two
unrelated XML-doc comments about `SysPackage.Version` in `PingContracts.cs` and `ProcessDesignService.cs`.

**clio already has most of the read half's raw material.** `clio/CreatioModel/VwProcessLib.cs` already declares
`Version`, `IsActiveVersion`, `IsMaxVersion`, `VersionParentId`, `VersionParentUId`, `SysSchemaId` and `Enabled`
(verified), and `ServerProcessDescriber` already queries that exact view —
`clio/Command/ProcessModel/IProcessDescriber.cs:99`:

```csharp
VwProcessLib row = ctx.Models<VwProcessLib>().FirstOrDefault(p => p.Caption == identity.Caption);
```

### 5.1 A live defect this research surfaced

`ProcessSchemaRepository.LoadForDescribe` resolves by `FindInstanceByName`
(`packages/CrtProcessBuilder/Files/src/cs/Schema/ProcessSchemaRepository.cs:139-144`). Because a versioned
process has **a distinct schema name per version**, `describe-business-process --process-name UsrProcess_0370312`
returns **version 0's graph** even when `UsrProcess_0370312Custom1` is the version the runtime executes. The
runtime redirects to the active version (`ProcessRunner.TryRunScheduledProcess` → `GetActiveVersionItem`);
`describe` does not, and says nothing about it.

**Today an agent reads and explains a process that is not running, with no signal in the response.** This is a
wrong answer being served now, independent of ENG-94374, and fixing it is the strongest argument for shipping the
read half first.

---

## 6. Cross-reference

* Traps that break silently → [`business-process-versioning-traps.md`](business-process-versioning-traps.md)
* The staged plan → [`business-process-versioning-plan.md`](business-process-versioning-plan.md)
* Estimate and defer rationale → [`business-process-versioning-estimate.md`](business-process-versioning-estimate.md)
