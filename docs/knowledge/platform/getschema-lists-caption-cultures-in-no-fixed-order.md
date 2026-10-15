---
description: Designer-service GetSchema returns the schema caption as ~28 culture entries in SysCulture enumeration order with no ORDER BY, so captions[0] is an arbitrary culture (ru-RU, id-ID) rather than en-US or the user's culture
applies-to:
  - clio/Command/SchemaDesignerHelper.cs
ticket: ENG-103380
date: 2026-10-14
---

**What is true** — `ClientUnitSchemaDesignerService/GetSchema` and `SourceCodeSchemaDesignerService/GetSchema` return
`caption` as one `{cultureName, value}` entry per culture that has a value. The entries follow the order in which the
platform enumerates `SysCulture`, and that query has no `ORDER BY` (core: `BaseSchemaToDtoMapper.cs`, `SysCulture.cs`),
so the order depends on the database and on the cache. On Creatio 10.2.521 (clio 8.1.0.140, Supervisor in en-US) QA
saw `ru-RU` first on one call and `id-ID` first on a later call to the same page (ENG-102120, comment 551494). The
`id-ID` value was the page template's title: cultures the page stores no title for carry the parent's title — see
`getschema-fills-missing-page-title-cultures-from-the-parent-template.md`.

**Why it is this way** — platform behaviour of the designer DTO mapper; clio cannot ask for one culture.

**What breaks if you ignore it** — reading `captions[0]` hands an agent a caption in a random language; in ENG-102120
the agent translated page titles into Italian from the Russian text. `SchemaDesignerHelper.ExtractCaption` therefore
picks the profile culture, then `en-US`, then the first non-empty entry. Even then a caption in a culture the schema
does not store is the parent's text, so it is not a reliable title in that culture.
