---
description: GetSchemaDesignItem can synthesize an unsaved replacing entity; use the add-on response targetSchemaUId for related-page identity
applies-to:
  - clio/Command/RelatedPages/RelatedPageAddonService.cs
  - clio/Command/AddonSchemaDesigner/AddonSchemaDesignerDtos.cs
ticket: GH-1302
date: 2026-09-12
---

**What is true** — When an entity is inherited into the requested package, Creatio's
`EntitySchemaDesignerService.GetSchemaDesignItem` may return a new unsaved replacing schema
on every read. Its `uId` is not a persisted entity identity. The add-on service resolves the
parent and returns the base/root target in `schema.targetSchemaUId`. Verified with BulkEmail
in Custom on Creatio 10.0.0.858 (.NET Framework, PostgreSQL), against persisted SysSchema rows.

**Why it is this way** — This is the designer's normal preparation of an editable schema,
not an entity-identity lookup. `AddonSchemaDesignerService.GetAddonInfo` resolves the supplied
target or parent through the entity manager and canonicalizes the target by name. Keep that
request path: replacing it with a flat SysSchema name query is ambiguous across replacing rows.

**What breaks if you ignore it** — Reads and successful writes report different invented
entity IDs even while their bindings persist correctly. Read the existing add-on extension
data without rewriting it; validate the target before saving so a malformed response cannot
turn an already committed write into an apparent failure.
