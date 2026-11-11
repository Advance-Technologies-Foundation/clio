---
description: a DataService SelectQuery without useLocalization true reads the entity table, which holds the site default culture (PrimaryCulture), and that culture never gets a Lcz row
applies-to:
  - clio/Command/DataBindingDbLocalizer.cs
  - clio/Package/SelectQueryHelper.cs
ticket: ENG-105291
date: 2026-11-10
---

**What is true** — The DataService `SelectQuery` contract carries `useLocalization`, and when the request leaves it
out it is `false` (`Terrasoft.Nui.ServiceModel.DataContract.SelectQuery.UseLocalization` is a plain `bool`, copied
into `EntitySchemaQuery.UseLocalization` in `QueryExtension`). Without it a localizable column is read from the
entity table itself, which holds the site's default culture: the `PrimaryCulture` system setting (All-employees
value; the platform falls back to `en-US` when it is empty, `AppConnection.SetPrimaryCultureInResourceStorage`).
With `useLocalization: true` the column answers in the connected user's culture. Seen live on 10.2.871 (ru-RU
profile, en-US default): `AccountType` read `Клиент` with it and `Customer` without it. `Entity.SaveLocalizationEntity` skips the default culture, so it never has a
`<Table>Lcz` row and `SelectLocalizationQuery` never returns it. `SelectQueryHelper` does not send
`useLocalization`, so every clio select reads the default culture. `UpdateLocalizationQuery` keeps the stored value
of a culture left out of its map, the default one included, but rejects a required column with
`RequiredColumnsEmptyValuesException` when the map lacks the connected user's culture, even a non-default one
(seen live, same stand).

**Why it is this way** — Platform contract; clio only chooses which of the two reads it sends.

**What breaks if you ignore it** — While the connected user's profile culture equals the default culture both
reads agree, so tests on an `en-US` stand pass. With a `ru-RU` profile on an `en-US` site, a "plain select" that
was meant to read the profile culture returns the `en-US` value, and a check that looks for the default culture in
`SelectLocalizationQuery` finds nothing and reports a correct write as "NOT verified".
