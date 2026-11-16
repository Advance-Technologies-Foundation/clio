# ADR: create a minimal dynamic case through a CrtProcessBuilder operation and one clio tool

Status: accepted for ENG-105556 on 2026-11-10 (grilled with the task owner). Evidence for every decision:
[`spec/eng-105556-minimal-dcm/eng-105556-minimal-dcm-decisions.md`](../eng-105556-minimal-dcm/eng-105556-minimal-dcm-decisions.md)
(Q1..Q29) and the design file of the same folder; the PRD is
[`prd-eng-105556-minimal-dcm.md`](../prd/prd-eng-105556-minimal-dcm.md).

## Context

A minimal DCM is a `DcmSchema` (an always-interpreted process: no C#, no compile, no restart) whose stages carry
`StageRecordId` = a lookup row Id, plus a `SysDcmSettings` row and a `SysDcmSchemaInSettings` link row that the
7.x case designer (which 8.x still opens) needs, bound to the package, plus a `crt.EntityStageProgressBar` on the
Freedom UI record page. Every core member involved is public on the CI platform build (checked by reflection on
8.3.4: `DcmSchemaManager.CreateSchema` / `SaveSchema` / `RemoveItemByUId` / `GetProcessValidationResult`,
`GetIsActiveVersion(Guid)`, `DcmEntityUtilities.FindEnabledDcmSchemas`, `IPackageSchemaDataCreator`,
`PackageElementUtilities.DeletePackageSchemaData`, the `DBSecurityEngine` checks, `WorkspaceUtilities`).

## Decisions

| # | Decision | Rejected alternatives and why |
|---|---|---|
| Q1 | The case write runs in the CrtProcessBuilder package; clio orchestrates and edits the page | clio driving the designer endpoints (needs the undocumented design-JSON dialect, 4-6 non-atomic writes, and duplicates the package's element handlers for the later steps task); a core API (cannot reach existing environments); a CrtGenAI dependency (breaks the package's empty `DependsOn`, inherits six defects) |
| Q2 | `BuildCase` is the ninth operation of `ProcessDesignService` | a sibling `DcmDesignService` (unmeasured route, escapes the package's mirror test); either way clio's operation pin goes 8 -> 9 and a new pin counts `EnsureCanManageDcm` |
| Q3 | One long-tail tool `create-dynamic-case` with `dry-run`; `update-page` is the retry path for the bar | two tools (the discovery index has room for one line); a mode of `create-business-process` (its contract has 16 B left) |
| Q5 | Gate before any read: General user + `CanManageDcm` + `CanManageSolution` + append on both settings tables | `CanManageDcm` only (fails inside the binder after rows are written); also `CanManageProcessDesign` (stricter than the platform) |
| Q6 | Save route A: object model + `DcmSchemaManager.SaveSchema`, behind an `IDcmSchemaSaver` seam | the designer save now (route B stays the upgrade path for step conditions) |
| Q7 | Write order: settings row -> link row -> bindings + read-back -> schema save LAST -> verify | schema first (its side effects - start-event row, flow schema - are not rolled back by the platform) |
| Q12, Q36 | An equivalent enabled case (same object, column, ordered `StageRecordId` list, link row present) answers `already-present` and writes nothing; the lookup is read and the stages planned before that decision; anything else enabled is `case-exists` | refusing every existing case (no safe retry after a page failure) |
| Q13, Q37 | One settings row with the same stage column and no filter column (`Filters` null, empty or `[{"columnUId":null}]`) is reused; any other existing row is `settings-conflict` | always inserting (duplicates resolved arbitrarily by `GetDcmStageColumnName`) |
| Q16 | clio adds the exactly computed missing package dependencies before the build pass (opt-out flag), removes them again if the build pass does not create the case, refuses on a cycle | refusing with the calls only (one call no longer meets the acceptance wording) |
| Q18 | The page is written with `PageUpdateCommand` append (dry run first, before any write) + the data-source validator | extracting the guards of `PageUpdateTool` (the fragment is a clio constant) |
| Q22 | The package version is claimed above every deployed build; the floor is that version; a 404 or non-envelope reply maps to `process-builder-without-build-case` | next version after main (a stand running an unmerged feature build passes the floor without `BuildCase`) |
| Q27 | Steps inside stages are NOT built now; the later task spikes the host-process + `ReadMetaData` wrap first | generalising the 166 `ProcessSchema`-typed element files now |
| Q28 | No feature toggle; the tool is added to the go-live test | a toggle (hides the tool on every surface, forces `requiredFeatures` in the article) |
| Q29 | Fresh stage UIds, `StageRecordId` = row Id, names `Stage1..N`, colour `#0C8701`, full-mesh connections, `Enabled = true` | GeneratedDcmSaver's shape (record Id as stage UId collides with derived system-element UIds once steps get conditions) |

## Invariants a reviewer can check (the seams the later steps task needs)

1. A stage travels as an object (`{value}`) on the wire and in the contract; `steps` is refused with
   `steps-not-supported`, never ignored.
2. Stage UIds are fresh and never equal `StageRecordId`.
3. Planner, composer and saver are separate types; the save goes through `IDcmSchemaSaver`.
4. The dependency planner takes an open input set.
5. Obligations are reported per artifact (`dcm`, `settings-data` from the server; `page` from clio).
6. The response's `case` block is describe-shaped, so a later `describe-dynamic-case` can return it.
7. Nothing is written before every check passes; the schema save is the last write.

## Consequences

- clio's bundled-package pins move by hand in the rebundle commit: operations 8 -> 9 and a new
  `EnsureCanManageDcm` call-site pin (1); the `EnsureCanManageProcessDesign` pin stays 6.
- Every rebundle makes the gated process tools refuse environments still on the previous archive; the release
  note says so.
- A failed save can leave platform residue (start-event row, flow schema) that `RemoveItemByUId` may not clear;
  the result lists it (`failed-leftover`) with the cleanup order.
- .NET 8 shares the code path but is reported as expected until measured on a .NET 8 stand.
