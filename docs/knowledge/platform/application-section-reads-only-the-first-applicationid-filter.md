---
description: ApplicationSection SelectQuery honours only the first ApplicationId filter; an OR group returns one application's sections, an unfiltered query returns none
applies-to:
  - clio/Command/FindAppCommand.cs
  - clio/Package/SelectQueryHelper.cs
ticket: ENG-102120
date: 2026-10-05
---

**What is true** — `ApplicationSection` is a virtual schema served by `ApplicationSectionQueryExecutor`
(package CrtBase). It does not evaluate the filter tree: for each of `Id`, `ApplicationId` and
`PackageId` it takes the **first** filter on that column and uses that filter's right expressions as
the value list (`GetSpecificColumnValuesAtFilterExpressions` → `.First(...).RightExpressions`). The
group's logical operation is ignored, and a query with no such filter returns no rows at all.
Measured on Creatio 10.2.373 with 12 installed applications: an OR group of 12 `ApplicationId`
equality filters returned 1 section (the first application's); one IN filter (`filterType 4`) with
12 `rightExpressions` returned all 8, the same as 12 single-filter queries summed.

**Why it is this way** — the executor builds its rows from `SysModule` per requested identifier
instead of translating the query into SQL, so it only understands "these identifiers".

**What breaks if you ignore it** — the query succeeds (`success: true`) and quietly drops rows.
`find-app` sent an OR group and reported `"sections": []` for every application but the first; an
agent asked to translate a whole application concluded it had no sections (ENG-102120). Batch
lookups on this schema must use `SelectQueryHelper.BuildSelectQueryWithInFilter`, never
`BuildSelectQueryWithOrFilter`, and must not rely on an unfiltered query to list all sections.
