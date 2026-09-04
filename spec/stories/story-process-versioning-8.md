# Story 8: Spike: measure the version-creation mechanics on a stand

**Feature**: process-versioning
**Jira**: ENG-94374
**FR coverage**: FR-07, FR-15
**PRD**: [prd-process-versioning.md](../prd/prd-process-versioning.md)
**ADR**: [adr-process-versioning.md](../adr/adr-process-versioning.md)
**Test plan**: [tp-process-versioning.md](../test-plans/tp-process-versioning.md)
**Repository**: clio
**Status**: in-progress
**Size**: L

---

## As a

developer

## I want

the open version-creation mechanics measured on a live stand before the operation is written

## So that

the create handler is written against measured behaviour instead of inferred behaviour

---

## Acceptance Criteria

- [x] **AC-01** *(run twice on the studio stand; SysSchemaProperty read verbatim)* — Given a draft create implementation run twice against a toolkit-created root, when `SysSchemaProperty` is read, then the findings note records the observed `Version` values and `IsActiveVersion` values verbatim
- [x] **AC-02** — Given a created version, when `SysSchema` is read, then the note records whether its `ParentId` equals the root's `SysSchema.Id`
- [x] **AC-03** — Given one version exists, when `GetMaxProcessVersionInPackage(uc, root.Id, packageUId)` is called, then the note records the returned number for the same package and for a different package
- [x] **AC-04** *(closed by observation, not experiment)* — Given a source process in a non-editable package, when the version is created, then the note names the package it landed in and closes OQ-01
- [x] **AC-05** *(both halves: root creation REFUSED, observed; version path compliant by construction)* — Given a root whose name carries no `Usr` prefix, when the version is saved, then the note records whether `validateNamePrefixes` rejected it
- [x] **AC-06** — Given a version was saved, when the ROOT's `SysSchemaProperty` rows are read, then the note records whether they survived
- [x] **AC-07** — Given the stand, when `UseNewSchemaHierarchyFolding` is read, then the note records its state and closes OQ-02
- [x] **AC-ERR** — Given any measurement contradicts this feature's ADR, when the spike ends, then the ADR is amended in the same PR and the contradiction is named in its Notes section

## Implementation Notes

Output is evidence, not shipped behaviour: a findings note plus, where a fact is non-obvious and silent, a `docs/knowledge/` record. No production code merges from this story.
Method: diff a package-created version against a designer-created version of the same source process, field by field.
Anchors (search BY NAME — the platform tree moves): `SchemaManager.SaveExtraProperties` (~`:3223`), `DesignMode/DBSchemaContentProvider.cs:294-329`, `BaseProcessSchemaManager.GetMaxProcessVersionInPackage:1285`, `Schema.cs:126` + `SchemaManager.cs:2316-2318` (`ExtendParent` renames the schema to its parent's name), `ProcessSchemaBaseElement` `IsInherited` memoisation, `base-process-schema-manager.js:88`/`:140-146`.
ENG-95335 changed package-collision reporting on schema save — measure on the current platform, not against the traps write-up.
Run schema writes SEQUENTIALLY: a parallel burst trips IIS rapid-fail and downs the .NET Framework app pool.
BLOCKER 1 as originally written is already refuted: `Assign(instance)` → `ParseObject` rewrites every extra property on each save.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Integration `[Category("Integration")]` | the measurements above, executed against a dedicated sandbox and recorded | evidence, not a suite — `[Explicit]`, self-ignoring, `[NonParallelizable]` if any harness is written |

Test naming: `MethodName_ShouldBehavior_WhenCondition`

## Definition of Done

- [x] Findings recorded in the ADR Notes and, where silent, as knowledge records
- [x] OQ-01 and OQ-02 closed in the PRD, or restated with what is still unknown
- [x] Code compiles without Roslyn analyzer warnings
- [x] All new tool names and flags are kebab-case
- [x] Unit tests use `[Category("Unit")]` — never `[Category("UnitTests")]`
- [x] No `catch (Exception)` added to clio code paths
- [x] Knowledge records whose `applies-to` names a file this story touches are updated or deleted **in this PR** (`AGENTS.md:456-457`)
- [x] Docs verdict stated explicitly in the PR body, including "no update required" where that is the verdict
- [x] MCP verdict stated in the PR body ("MCP reviewed, no update required" where that applies)
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started: 2026-09-03
- Implementation completed: PARTIAL. The read-only half is done; four criteria need stand writes and
  are blocked on a decision, not on effort.
- Tests passing: not a suite - the evidence is SQL against the reference stand (core 10.1.448.0). The
  regression suites had to stay green while the shipped text was corrected: clio 8300 passed / 0 failed,
  clio-knowledge 125 passed / 0 failed.
- Notes:
  - **AC-ERR fired.** V2 ("the root is version 0") is measurably false as a two-way rule:
    `BulkDuplicatesSearchProcess` carries Version 2 and `OrderApprovalBaseSubprocess` Version 1 with
    `SysSchema.ParentId` NULL, while no parented schema carries 0. The number is stamped, not derived.
    Amended in the ADR Notes, corrected in the guidance article (clio-knowledge `34acdd8`), corrected in
    `DescribeProcessResult.Version`, and recorded as
    `docs/knowledge/platform/process-version-number-is-stamped-not-derived.md`. The discriminator the
    guidance review settled on in round one survives: it was family SIZE, never the number.
  - AC-02: a version's `SysSchema.ParentId` equals the ROOT's `SysSchema.Id`, `ExtendParent` 0 on both,
    so the parent-name rename path is not taken.
  - AC-06: the root's eight `SysSchemaProperty` rows survive intact. Two differ in VALUE -
    `IsActiveVersion` and, unexpectedly, `IsInterpretable` (root False, version True). `Tag` is copied
    verbatim, `CreatedInVersion` is 0.0.0.0 on both.
  - AC-07, **OQ-02 closed**: `UseNewSchemaHierarchyFolding` `DefaultState = 1`, no override. The feature
    tables here are `Feature` / `AdminUnitFeatureState`, not the `SysFeature` pair the name suggests.
  - AC-04 closed by OBSERVATION rather than experiment, and it closes **OQ-01**: 3 of 14 visible
    families are cross-package, so a version does not inherit the root's package and the create handler
    must take the target package as an input.
  - AC-01 half measured: the verbatim values of an already-created version are recorded (root 0/False,
    version 1/True, stored as strings). Running a draft create implementation TWICE needs the writes.
  - Side finding: `VwProcessLib` is not a census. 18 version schemas exist, 14 are visible; for three
    `*BaseSubprocess` families the ROOT is invisible too, so describe correctly answers "no row for
    schema". Every visible family holds exactly one version, numbered 1 and active - so the
    multi-version and two-flagged-active paths stay defensive rather than observed.
  - **Second pass, after the writes were authorised: the mechanics were measured WITHOUT creating a
    version.** The composer ships in the client, so it can be read: `Terrasoft.BaseProcessSchemaManager`
    carries `createNewSchemaVersion`, `getNewSchemaVersion`, `setNewSchemaVersionName` and
    `_getPackageForNewSchemaVersion`, and reading them answered three criteria outright.
  - **AC-03 measured directly and read-only.** The number is `maxVersionInPackage + 1`, and the max comes
    from `GetSchemaVersionInfo {parentSchemaUId, packageUId}` on `ProcessSchemaManagerService.svc` — POST
    with the CSRF header, since a GET answers 405 and a bare POST 403. Results: `InvoiceVisaProcess` +
    its own package → 1; the same root + `UsrAntonTest` → 0; a fresh root + its own package → 0. The max
    is scoped to (root, PACKAGE), so a version created in another package restarts at 1. No server-side
    C# was needed after all.
  - **AC-05 answered, and not with a rejection.** `setNewSchemaVersionName` builds
    `parentSchemaName + packageName.replace(/\W/g,"") + version` and PREPENDS the schema-name prefix when
    the PARENT lacks it. A version of the unprefixed `InvoiceVisaProcess` on this stand would be named
    `UsrInvoiceVisaProcessInvoice1`. Nothing validates it. Written up as
    `docs/knowledge/platform/process-version-name-gets-the-schema-prefix-prepended.md`, and it is why the
    guidance article's V4 was demoted from a law to a default.
  - **AC-04 / OQ-01 refined to the real mechanism.** The source's own package is used when it is editable;
    otherwise the feature `SaveProcessVersionInApplicationPackage` decides (ON here → the design package).
    Trap for the create handler: the non-editable branch leaves the NAME builder on
    `CUSTOM_PACKAGE_NAME`, so a version can be named `...Custom1` while living elsewhere.
  - Two facts that change how stories 9-13 should be written: a new version is composed with
    `isActiveVersion = false` (so create and activate are separate at the PLATFORM level, not just in our
    design), and the family is set flat at composition time — a version of a version still points at the
    root. Both now read from code rather than inferred.
  - ~~The BASE manager `getCanUseProcessVersions()` is a hard `return false`~~ — **CORRECTED in the
    fourth pass.** That is the base/Embedded DEFAULT. The manager business processes actually use,
    `Terrasoft.manager.ProcessSchemaManager`, overrides it to `return true`. The silent-no-op branch is
    real but narrow: embedded (case/DCM) managers and anything inheriting the base, NOT business
    processes. I recorded it too broadly and the network trace caught it.
  - **CORRECTED 2026-09-04, raised by the ticket owner: versioning IS reachable on the local stand.** The
    route is the process CARD page `#CardModuleV2/VwProcessLibPageV2/edit/<id>`, served by the Freedom
    shell, which hosts the versions detail. The owner created `UsrProcess_329f1da` +
    `UsrProcess_329f1daCustom1` there. What the probes below actually established is narrower: the
    process DESIGNER is unreachable by URL on that build. I generalised from that to "no implementation
    is reachable", and the card-page URL was sitting in a studio-stand snapshot I had already taken.
    So the studio detour was not needed for AC-01 — though it did yield the created-inactive fact and the
    UId-to-Id translation, which the UI route would not have exposed.
  - **The open question is now CLOSED by observation** (owner screenshots, 2026-09-04). The designer SAVE
    is a split button — `Save new version (Ctrl+Alt+N)` / `Save current version (Ctrl+Alt+S)` — and
    choosing the former creates the version and then ASKS in a separate prompt: *Set the current version
    of the process "<caption>" actual?* YES / NO. So create and activate ARE separate, the composer
    leaving `isActiveVersion = false` is the real product state rather than an artefact of the raw API
    path, and the owner's active version is simply a YES. The timestamps read correctly in hindsight.
  - **Consequence for the contract, and it answers the question raised when scope was agreed:**
    `ModifyProcessAsNewVersion` must NOT activate, and the agent must ASK rather than call
    `SetActiveProcessVersion` itself. The product does not decide for the user; neither may we. Recorded
    in the PRD as its own section.
  - Also observed: "Save current version" — the in-place overwrite the guidance warns about — sits in the
    SAME menu as the safe option, one item away. That is precisely why the modify precondition had to go
    where the modify instruction lives.
  - The naming formula holds on a second stand through the UI route too: root `UsrProcess_329f1da` +
    package `Custom` + version 1 → `UsrProcess_329f1daCustom1`.
  - Reachability correction is now complete: the DESIGNER is reachable as well, just not by any URL I
    constructed — it is entered from the process card page. Recorded as
    `docs/knowledge/platform/process-designer-is-entered-from-the-process-card-not-a-url.md`, including
    the procedural lesson: when a UI route is needed, follow the product navigation instead of guessing
    module names.
  - **AC-01, third pass: run properly, and it cannot be executed here — dead ends named.** Five routes
    to an implementation that creates a version were tried. The Shell hash `#ProcessSchemaDesigner/<uid>`
    falls back to the app list; `ViewModule.aspx` REDIRECTS to the Shell; `ProcessDesigner.aspx` and
    `ProcessSchemaDesigner.aspx` answer 500; the classic section route and the app-list link
    `Navigation.aspx?schemaName=VwProcessLibSection` both land on the app list. Driving the composer
    directly dead-ends as well: `BaseSchemaManagerItem.updateRequestClassName` is NULL on the base, so
    the save request class ships with the designer module, and `Terrasoft.require` does not resolve those
    modules in this Freedom shell. On this build the only implementation that creates a version is the
    classic designer, and the classic UI is not served.
  - **The substance came from the counter instead, and it is worth more than two hand-made runs.**
    `GetSchemaVersionInfo` was measured for five (root, package) pairs. It counts family MEMBERS and
    IGNORES the root own stamped number: `BulkDuplicatesSearchProcess` carries `Version = 2` and the
    counter answers 0 for it.
  - **A trap no formula predicts.** Because a new version is numbered `max + 1`, a root whose stamped
    number is non-zero gets a version numbered BELOW itself — a first version of
    `BulkDuplicatesSearchProcess` would be 1, beside a root that says 2. Stories 9-13 must never derive
    the next number from the SOURCE schema own property, and nothing may present version numbers as a
    total ordering across a family.
  - So the two-run values are DERIVED, not observed, and recorded as such: run 1 -> `Version = 1`,
    `IsActiveVersion = False`; run 2 -> `Version = 2`, `IsActiveVersion = False`. Every input to that
    derivation is measured — the formula from `getNewSchemaVersion`, the initial flag from
    `createNewSchemaVersion`, the counter from the server. AC-01 stays UNCHECKED rather than claimed,
    because it asks for an observation and this is a derivation.
  - One write WAS made and it earned its keep: `UsrSpike_VersionProbe` in `UsrAntonTest`, created through
    `clio-run create-business-process`. A freshly created process reports `Version = 0`,
    `IsActiveVersion = True`, `CreatedInVersion = 10.1.448.0` (stock content ships `0.0.0.0`),
    `IsInterpretable = True` and an extra `StudioFreeProcessUrl` property. That last one corrects the
    earlier note: `IsInterpretable` differing across the stock family is an authoring difference, not a
    versioning artefact. The process has no versions and can still be deleted if the fixture is not wanted.
  - **Fourth pass, second stand (studio edition, core 10.2.3.0) — the classic designer IS served here,
    at the URL guessing never found: `/0/Nui/ViewModule.aspx?vm=SchemaDesigner#process/<schemaUId>`.** On
    the first stand `ViewModule.aspx` redirects to the Freedom shell, so the designer is absent THERE,
    not in the product. Answering the original question: yes, that stand is classic.
  - Measured there first-hand: a designer-created process gets the auto-generated code
    `UsrProcess_f817a44` (prefix + hex tail — the shape V7 warns about), lands in `Custom` with
    `Version = 0`, `IsActiveVersion = true`, own UId as the family key.
  - **Editing an ACTIVE process and saving OVERWRITES it in place — no version offered, none created.**
    Observed: "Successfully saved", row still `Version = 0`. The guidance article asserts exactly this
    about irreversible in-place edits; now it has evidence.
  - Activation reads before it writes: "Set as actual version" issued `GetActualVersionUId` and
    `GetRunningProcessesCount`, then sent NO `SetIsActualVersion`. The platform gates activation on the
    running-instance count, and the refusal is silent unless the caller inspects the result — input for
    story 13.
  - **The version parent must be COMPUTED, not copied.** An instance `parentSchemaUId` is the INHERITANCE
    parent: mine was `bb4d6607-...`, equal to the manager `defSchemaUId`.
    `getIsSetParentSchemaUId = e && !isEmptyGUID(e) && e !== defSchemaUId`, so the composer ignores it and
    roots the family at the source. A server-side copy of `parentSchemaUId` would root the family at the
    BASE process schema. Verified both ways on the live manager.
  - **Fifth pass: AC-01 RUN AND OBSERVED.** The draft create implementation is three lines against the
    shipped manager: `getNewSchemaVersion({sourceSchema: rootItem.instance, sysPackage: null,
    canEditPackageSchema: false}, newItem => newItem.save({}, cb))`. The obstacle was never the platform —
    it was that `sourceSchema` is the schema INSTANCE, not the manager item, which the item shape hid.
  - Run twice against one root, `SysSchemaProperty` verbatim: root `Version=0 IsActiveVersion=True`;
    `UsrProcess_f817a44Custom1` `Version=1 IsActiveVersion=False`; `UsrProcess_f817a44Custom2`
    `Version=2 IsActiveVersion=False`. Names, numbering, flat family and package all exactly as the
    source predicted.
  - **The ROOT stayed active through both creations.** Creating a version changes nothing about which
    version runs — so create and activate are genuinely independent operations, not a convenience split.
    That is the single most load-bearing input for stories 9-13.
  - Two details the write half must replicate: the save TRANSLATES the identifier (client sets
    `parentSchemaUId` = root UId, persisted `SysSchema.ParentId` = root **Id**, `ExtendParent` false), and
    `CreatedInVersion` is stamped with the platform version (10.2.3.0) rather than the 0.0.0.0 stock
    content carries.
  - **AC-05, both halves, and they differ.** Creating a ROOT without the prefix is REFUSED — observed:
    `createSchemaInstance({name: "SpikeNoPrefix"})` throws `Item name must contain prefix 'Usr'`.
    Creating a VERSION of an unprefixed root is not refused, because that path submits no name to
    validate: it CONSTRUCTS one and prepends the prefix. The guard protects supplied names; the version
    path is compliant by construction. A server-side implementation supplying its own name inherits the
    guard and must prepend the prefix itself.
  - Residue on the studio stand, which the owner said is wiped tomorrow: `UsrProcess_f817a44` plus two
    versions, in `Custom`. Versions are undeletable by V6, which is exactly why this ran there and not on
    the local stand.
