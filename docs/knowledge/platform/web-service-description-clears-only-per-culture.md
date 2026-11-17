---
description: Saving a web service schema with an empty description dictionary keeps the stored description; an empty value per culture clears it
applies-to:
  - clio/Command/WebServiceDesigner/WebServiceDesigner.cs
ticket: ENG-105475
date: 2026-11-16
---

**What is true** — `ServiceDataContract` treats a localizable value per culture: a `description` sent as `{}`
leaves the text already stored, while `{"en-US": ""}` (every culture the schema has, each with an empty value)
clears it. Measured on a .NET Framework stand in November 2026 through `modify-web-service setDescription ""`.

**Why it is this way** — the platform saves localizable values culture by culture and skips cultures the contract
does not mention, so "no cultures" means "no change", not "empty".

**What breaks if you ignore it** — a "clear the description" call answers success and describe still shows the
old text, with no error anywhere. `setDescription ""` therefore sends every existing culture with an empty value.
