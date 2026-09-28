---
description: After create-page the page caption holds the English text in all cultures; after create-entity-schema the other cultures show the parent's caption (es-ES "Objeto base") - a missing culture cannot be detected by absence
applies-to:
  - clio/Command/PageCreateOptions.cs
  - clio/Command/EntitySchemaDesigner/RemoteEntitySchemaCreator.cs
  - spec/adr/adr-page-localization.md
ticket: ENG-90576
date: 2026-09-26
---

**What is true** — measured on Creatio 10.2.254 (stand `eng90576`). `create-page` sends the caption in one culture
(`en-US`); the stored page caption then has 28 `SysLocalizableValue` rows, every one "Lab form page".
`create-entity-schema --title "Lab object"` sends `en-US` only; the schema's `es-ES` caption reads "Objeto base",
`de-DE` "Basisobjekt", `uk-UA` "Базовий об'єкт" — the BaseEntity caption, inherited per culture.

**Why it is this way** — platform save behaviour for a new schema caption (page) and per-culture inheritance of the
parent caption (entity).

**What breaks if you ignore it** — a coverage check that treats "a value exists in es-ES" as "translated" reports a
page title or object title as done while Spanish users see English text or the word "Objeto base". Report a value
equal to the `en-US` one separately (`sameAsDefault`) and translate object titles explicitly in every culture.
