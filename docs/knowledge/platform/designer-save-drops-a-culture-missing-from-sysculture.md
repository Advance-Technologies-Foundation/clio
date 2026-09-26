---
description: ClientUnitSchemaDesignerService and EntitySchemaDesignerService SaveSchema silently drop a localizable value whose culture is not a SysCulture row (xx-XX, fi-FI) and still answer success:true; an inactive culture is stored
applies-to:
  - clio/Command/ResourceStringHelper.cs
  - clio/Command/PageUpdateOptions.cs
  - clio/Command/EntitySchemaDesigner/EntitySchemaDesignerSupport.cs
  - clio/Command/EntitySchemaDesigner/CaptionCultureScriptGuard.cs
  - clio/Command/EntitySchemaDesigner/RemoteEntitySchemaColumnManager.cs
  - clio/Command/EntitySchemaDesigner/RemoteEntitySchemaCreator.cs
  - clio/Command/Localization/CultureAvailabilityGuard.cs
  - spec/adr/adr-page-localization.md
ticket: ENG-90576
date: 2026-09-26
---

**What is true** — measured on Creatio 10.2.254 (.NET Framework, stand `eng90576`). A page resource value
`{"cultureName":"fi-FI"}` or `{"cultureName":"xx-XX"}` sent to
`ClientUnitSchemaDesignerService.svc/SaveSchema` returns `{"success":true,"validationErrors":[]}` and is not stored:
the next `GetSchema` and `SysLocalizableValue` have no such row. The same happens for a column caption sent to
`EntitySchemaDesignerService.svc/SaveSchema`. `fi-FI` is a valid .NET culture — it is simply not one of the
environment's 28 `SysCulture` rows. A culture that IS a row but has `Active=false` (`es-ES`, `de-DE` on that stand)
is stored and read back normally. A lower-case name (`de-de`) is stored under the canonical `de-DE`.

**Why it is this way** — the designer maps values to `SysCulture` by name and skips names it cannot map; there is
no validation error for it.

**What breaks if you ignore it** — a `CultureInfo`-based check (the one `CaptionCultureScriptGuard` and
`NormalizeSchemaCaptionLocalizations` use) accepts `fi-FI`, so the translation is reported as saved while nothing
was written. A culture-writing command must check `SysCulture` before the save and read the value back after it
(ADR `adr-page-localization.md`, D4/D5/D9).
