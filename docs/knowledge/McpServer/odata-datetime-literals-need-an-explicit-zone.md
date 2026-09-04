---
description: Creatio OData v4 mishandles a date-time literal with no UTC designator or offset - build-dependent between an opaque rejection and a silent DateTime.MinValue write
applies-to:
  - clio/Command/McpServer/Tools/ODataDateTimeGuard.cs
  - clio/Command/McpServer/Tools/ODataUpdateTool.cs
  - clio/Command/McpServer/Tools/ODataCreateTool.cs
ticket: GH-1369
date: 2026-09-04
---

**What is true** — Creatio publishes EVERY temporal column as `Edm.DateTimeOffset` (verified on
Creatio 10.1.725 / .NET Framework: 3933 occurrences in `odata/$metadata`, `Activity.StartDate`,
`Activity.DueDate` and `Contact.BirthDate` among them). There is no `Edm.Date`, so a date column and a
timestamp column are indistinguishable from the metadata. What the service does with a literal that
carries neither `Z` nor an offset depends on the platform build and is never safe:

- `"2024-01-01T04:00:00.000"` on Creatio 10.1.725 — the whole request fails with the opaque body
  `The request is invalid.`, on both PATCH (`odata-update`) and POST (`odata-create`).
- the same value on the build reported in GitHub issue #1369 — the request SUCCEEDS, answers
  `success:true`, and the column ends up as `0001-01-01T00:00:00Z` (`DateTime.MinValue`).
- `"2024-01-01T04:00:00.000Z"` — written correctly on both.
- `"2024-01-01"` (date only, no time) — accepted, and interpreted in the SERVER's local zone:
  writing it to `Activity.StartDate` on a UTC+02:00 stand read back as `2023-12-31T22:00:00Z`.
- `odata-read` filters are not affected the same way: a `StartDate eq <literal>` filter fails with a
  server error whether the literal carries `Z` or not, so it never silently returns the wrong rows.

**Why it is this way** — the OData v4 literal form of `Edm.DateTimeOffset` requires a zone, so a
zone-less string is simply not a valid value; each platform build then decides on its own whether to
fault or to bind it to `default(DateTime)`.

**What breaks if you ignore it** — a batch of writes reports `success:true` with the date columns
quietly zeroed, which nothing surfaces unless every write is read back and diffed field by field.
`ODataDateTimeGuard` therefore refuses the zone-less date-time shape before the request leaves clio,
gated on the declared Edm type so a text column can still hold a date-shaped string. The date-only
form is deliberately NOT refused: because date columns are `Edm.DateTimeOffset` too, refusing it would
break every ordinary `BirthDate`-style write for what is a zone shift, not a loss.
