---
description: A legacy Mobile-wizard settings schema (Mobile<Entity>GridPageSettings<Workplace>) passes PageGetCommand.TryGetPage successfully with schema-type "unknown" and an EMPTY bundle, so source-type detection must not rely on the bundle
applies-to:
  - clio/Command/PageGetOptions.cs
  - clio/Command/PageSchemaBodyParser.cs
  - clio/Command/McpServer/Tools/MobilePageConverter/MobilePageConversionGuideTool.cs
  - clio/Command/McpServer/Tools/MobilePageConverter/Legacy/LegacyMobileSettingsReader.cs
ticket: ENG-95730
date: 2026-09-02
---

**What is true** — A classic Mobile-wizard settings schema stores a JSON *operation array* (`[ { "operation": "insert",
"name": "settings", … } ]`) and has a `ClientUnitSchemaType` that is neither 9 (web) nor 10 (mobile). `TryGetPage`
therefore reports `schema-type: "unknown"`, and because `PageSchemaTypeExtensions.FromBody` treats every body that
does not start with `{` as an AMD module, the marker-based parser finds no sections and yields an **empty** bundle
without throwing. The read SUCCEEDS; `Raw.Body` carries only the editable layer.

**Why it is this way** — `PageGetCommand` was written for Freedom UI pages; the AMD parser is tolerant by design so
a page with a missing section still resolves. Nothing in that path knows the legacy format, and adding chain bodies to
`PageGetResponse` would echo N raw bodies into every `get-page` call.

**What breaks if you ignore it** — Detecting the legacy source from `pageResponse.Bundle` (empty) or from
`Raw.Body` (one layer only) silently misclassifies or under-reads the page. `get-mobile-page-conversion-guide` detects
the legacy source by the `unknown` label plus the schema-name pattern, then reads ALL package layers through
`ILegacyMobileSettingsReader` (designer hierarchy + `IJsonDiffApplier`), never through the page bundle.
