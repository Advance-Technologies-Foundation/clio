---
description: every ApplicationSection update (update-app-section, even icon-only) deletes the section title and description in all non-default cultures (SysModule localization rows), RND-34912 workaround ClearSysModuleLocalization
applies-to:
  - clio/Command/ApplicationSectionUpdateCommand.cs
  - clio/Command/ApplicationSectionLocalization.cs
  - clio/Command/SectionLocalizationPlanner.cs
  - spec/adr/adr-page-localization.md
ticket: ENG-90576
date: 2026-09-26
---

**What is true** — measured on Creatio 10.2.254 (.NET Framework, stand `eng90576`). A section title is
`SysModule.Caption` (`ApplicationSection.Id` = `SysModule.Id`); `en-US` is in `SysModule`, every other culture is a
row of the `SysModule` localization table (read it with DataService `SelectLocalizationQuery`, filter `Record`). An
`UpdateQuery` on the `ApplicationSection` virtual entity — which is what `update-app-section` sends, also for an
`icon-background`-only change — deleted the `es-ES` and `de-DE` rows (caption AND description). Source:
`Terrasoft.Core.Applications/Content/AppSectionManager.cs` `UpdateSection` calls `ClearSysModuleLocalization`
("TODO Remove this workaround when RND-34912 will be completed") before it saves `SysModule`.

A per-culture write that does NOT lose data is DataService `UpdateLocalizationQuery` on `SysModule` with a
parameter `{"dataValueType":19,"value":"{\"es-ES\":\"…\"}"}`: it merges per culture. A `Caption` map without the
connected user's culture fails with `Title field must be filled in`; a culture that is not a `SysCulture` row is
dropped with `success:true`.

**Why it is this way** — a platform workaround; the `ApplicationSection` path only knows the current culture.

**What breaks if you ignore it** — any section metadata change silently deletes every translation of the section
title and description. `update-app-section` therefore snapshots the rows before the update and writes them back
after it (ADR D11); a new caller of the `ApplicationSection` update path must do the same, and a translation must
never be written through that path.
