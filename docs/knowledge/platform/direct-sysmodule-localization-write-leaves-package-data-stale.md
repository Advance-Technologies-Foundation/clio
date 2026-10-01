---
description: a direct SysModule localization write (UpdateLocalizationQuery) does not refresh the application package's SysModule_<SectionCode> data binding snapshot; re-save the binding unchanged (GetSchema, GetBoundSchemaData, SaveSchema)
applies-to:
  - clio/Command/ApplicationSectionLocalization.cs
  - clio/Command/SectionLocalizationPlanner.cs
  - clio/Command/ApplicationSectionUpdateCommand.cs
ticket: ENG-90576
date: 2026-09-26
---

**What is true** — measured on Creatio 10.2.254 (stand `eng90576`). An application package carries the section
record as package data `SysModule_<SectionCode>` (`SysPackageSchemaData`, `IsLocked: true`), with one
`SysPackageDataLcz` snapshot per culture. After `UpdateLocalizationQuery` wrote `es-ES`/`de-DE`/`fr-FR` titles,
those snapshots still held `''`. Re-saving the binding unchanged —
`SchemaDataDesignerService.svc/GetSchema {"schemaUId":<binding UId>}` (returns `boundRecordIds: null`),
`GetBoundSchemaData {"uId":<binding UId>}` for the record ids, then `SaveSchema` with the same DTO plus
`boundRecordIds` — answered `success:true` and every culture snapshot then held the stored title. The locked
flag did not block the save.

**Why it is this way** — package data is a snapshot taken when the binding is saved
(`PackageSchemaDataCreator` → `LoadData`); the platform's own section update re-saves it, a direct entity write
does not.

**What breaks if you ignore it** — the translation shows in the environment but is missing from the package, so an
export or install elsewhere ships the old (empty) title. `update-app-section` re-saves the binding after every
localization write and reports a warning if it cannot (ADR D11 step 4).
