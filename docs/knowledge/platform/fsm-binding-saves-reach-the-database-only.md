---
description: in file system development mode a package data binding saved through SchemaDataDesignerService reaches the database only — Data/<binding> never appears in the package folder, and pkg-to-file-system / pkg-to-db are whole-package overwrites, so PackageDataBindingWriter only warns and names pkg-to-file-system instead of exporting; the SysModule_<code> re-save in update-app-section (ApplicationSectionLocalization) bypasses that writer and does not warn
applies-to:
  - clio/Command/PackageDataBindingWriter.cs
  - clio/Package/FileDesignModePackages.cs
  - clio/Command/McpServer/Prompts/DataBindingDbPrompt.cs
  - clio/Command/ApplicationSectionLocalization.cs
  - clio/Command/SectionLocalizationPlanner.cs
ticket: ENG-102507
date: 2026-10-07
---

**What is true** — read from the Creatio core source (`creatio-core`, October 2026) and checked on a
disposable FSM stand (eng101676, Creatio 10.2.414, PR #1772): after `sync-schemas` `create-lookup` +
`seed-rows` both `Data/` folders were missing on disk until `pkg-to-file-system` wrote them, with the
same binding UIds `read-data-binding-db` returned.
`SchemaDataDesignerService.svc/SaveSchema` → `SaveSchemaDataCommand.InternalExecute` writes
`SysPackageSchemaData`, its columns and the bound rows to the database and has no file-system branch.
Entity schemas behave differently: they are saved through `FileSystemEnabledSchemaManager`, which also
writes the schema files when FSM is on. That is why, after `sync-schemas` `create-lookup` + `seed-rows` in
FSM, `Schemas/<Lookup>/` and `Resources/<Lookup>.Entity/` appear on disk but `Data/<Lookup>/` and
`Data/Lookup_<Lookup>/` do not (GitHub #1747). The platform has no per-binding export. The only
database-to-disk writer is `AppInstallerService.svc/LoadPackagesToFileSystem` (`pkg-to-file-system`).
It accepts a package-name list (`IList<string> packageNames`, bare JSON body), but clio sends an empty
body, so it always exports every package. It runs with `OverwriteTargetStorageChanges = true`. For the
selected packages that makes the disk match the database: an on-disk item missing from the database is
marked deleted (`PackageStorageComposer.ShouldItemBeMarkedAsDeleted`). Only `ClientUnitSchemaManager` and
`SourceCodeSchemaManager` schemas are skipped. `LoadPackagesToDB` (`pkg-to-db`) is the same overwrite in
the other direction. By that logic, a binding that exists only in the database would be deleted from it
by the next `pkg-to-db`, including the automatic one that `create-user-task` and
`modify-user-task-parameters` run when they write parameter directions. That deletion follows from the code and has not been reproduced
on a stand. It concerns the binding registration (`SysPackageSchemaData`), not the bound rows in the
target table: `pkg-to-db` never installs those rows
([`Command/load-packages-to-db-registers-definitions-not-package-data.md`](../Command/load-packages-to-db-registers-definitions-not-package-data.md)),
which is what the workspace template means by "leaves package data alone".

**Why it is this way** — FSM has the developer edit client modules and C# source on disk. Everything
else is designer-driven and stays database-first until the user runs "download packages to file system".
That is the same button the Configuration section shows after a data binding is saved in its UI.

**What breaks if you ignore it** — calling `LoadPackagesToFileSystem` automatically after a binding
write looks like the obvious fix. It would silently overwrite or delete uncommitted work in the developer's
linked repository: data folders pulled from git and not yet loaded, SQL scripts, resources. And it would
do that as a side effect of a row insert. Without any notice, the opposite failure happens: the binding
command reports success, the commit ships the package without its data, and the next environment gets an
empty lookup. `PackageDataBindingWriter` therefore reads `GetIsFileDesignMode` once per writer instance
after a save or delete. It warns with the folder and the `pkg-to-file-system` step, also when the state
cannot be read, and never fails the write. A failed write would make callers retry a non-idempotent
insert. The warning covers only bindings written through that writer: `create-data-binding-db`,
`upsert-data-binding-row-db`, `remove-data-binding-row-db`, `sync-schemas` and `create-lookup`, and the
branding/feature binder. `ApplicationSectionLocalization.RefreshSectionPackageBinding` re-saves the
`SysModule_<code>` binding by posting SaveSchema itself (called from `SectionLocalizationPlanner`, that is
`update-app-section` after a caption localization), so that binding stays database-only in FSM without any
warning. The same `pkg-to-file-system` step applies there.
