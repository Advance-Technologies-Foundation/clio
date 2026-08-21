---
description: clio reads column default values through EntitySchemaDesignerService.svc/GetSchemaDesignItem, not through OData $metadata - CSDL DefaultValue is a facet on structural properties only, so $metadata cannot carry a lookup default's display value
applies-to:
  - clio/Command/EntitySchemaDesigner/
ticket: ENG-91318
date: 2026-08-19
---

**What is true** — clio reads entity schema design items, column default values among them, through
the designer service (`GetSchemaDesignItem` and its siblings under
`clio/Command/EntitySchemaDesigner/`). No clio OData code requests `$metadata` - grep the tree, there
is not one occurrence in C#. The recurring suggestion to switch defaults onto OData `$metadata`,
because other teams read schema that way, was evaluated and rejected.

**Why it is this way** — in CSDL a `DefaultValue` is a facet on a structural property. A lookup column
is a navigation property, so its default cannot appear there at all, and what does appear for a
primitive is the raw stored value. A lookup default therefore has no display value in `$metadata`, and
a readback built on it cannot answer "which record is this default pointing at" - the question the
agent actually asks. The designer service returns the schema item as the designer sees it, including
the surrounding column metadata.

**What breaks if you ignore it** — replacing the designer read with an OData `$metadata` read looks
cheaper and passes a smoke test against a primitive column, then reports lookup defaults as bare GUIDs
with no way to resolve them, which is the very gap the migration was supposed to close. If you revisit
this, the honest option is enrichment on top of the designer read, not a swap.
