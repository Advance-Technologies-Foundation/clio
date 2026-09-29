---
description: ClientUnitSchemaDesignerService GetSchema returns the parent template's translated title in every culture a page stores no title for ("Página en blanco"), so a non-English page title is not proof of a translation
applies-to:
  - clio/Command/LocalizePageCommand.cs
ticket: ENG-90576
date: 2026-09-29
---

**What is true** — measured on Creatio 10.2.312, .NET Framework, MSSQL (stand `sae_m_seeenu_16104034_1001`).
`ClientUnitSchemaDesignerService/GetSchema` (`useFullHierarchy:false`) returns the page's `caption` array filled in
from its parent schema: in each culture the page stores no title for, the value is the parent template's title in
that culture. A page created with `create-page --template BlankPageTemplate` (title stored in `en-US` only) showed
"Página en blanco", "Page blanche", "Leere Maske"… in 27 of 28 cultures; a `create-app` list page showed the
`ListPageV3Template` title in 10 of 29 cultures (the other cultures held the English page title). The same response
carries the parent inline as `schema.parent`, with its own `caption` array, so the case is detectable without an
extra request.

**Why it is this way** — the designer service resolves a localizable value through the schema hierarchy; the page's
own rows are only the cultures it overrides.

**What breaks if you ignore it** — comparing a culture's title with `en-US` reports such a page as translated, and an
agent skips its title. `localize-page` therefore reports `coverage.captionInherited` (the value equals the parent's
in that culture while the page's `en-US` title differs from the parent's). A later write of one culture also sends
these inherited values back in the full caption list.
