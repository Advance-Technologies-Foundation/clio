---
description: Freedom UI colors records only from the schema's color column (PrimaryColorColumn) and only when its type is Color (18) - a text column named Color colors nothing, and the runtime field that names the color column is not yet observed
applies-to:
  - clio/Command/EntitySchemaDesigner/RemoteEntitySchemaColumnManager.cs
  - clio/Common/EntitySchema/RuntimeEntitySchemaReader.cs
  - clio/Command/SetEntitySchemaPropertiesCommand.cs
ticket: ENG-90714
date: 2026-11-24
---

**What is true** — a lookup value is colored on a Freedom UI page from the schema's **color column**: the
designer's `primaryColorColumn` reference (Object Designer → object settings → "Color"). The platform reads it
only when that column's data value type is `Color` (18). The column NAME decides nothing: on Creatio 10.2.965
`ContactType` colors from a `Color` column of type `Color`, `CaseStatus` has a TEXT column named `Color` and
colors from `StatusColor`, and `ActivityCategory` has only a text `Color` column and no color column at all
(read by the clio-knowledge `lookup-colors` article, 2026-11-24). `set-entity-schema-properties
--primary-color-column` therefore refuses a non-`Color` column before saving.

The merged (runtime) read maps the color column from `primaryColorColumnName`, falling back to
`primaryColorColumnUId` matched against the runtime columns - the same shape the primary-display column uses.
Which of the two `RuntimeEntitySchemaRequest` actually returns has NOT been observed on a stand; the
package-scoped read (designer `primaryColorColumn`) is the authoritative one.

**Why it is this way** — the designer accepts any column object as the color column and saves without
complaint, and the platform's `CreatePrimaryColorExpression` silently skips a non-`Color` column. A text
column holding `#RRGGBB` strings is the obvious-looking mistake, and an out-of-the-box text column literally
named `Color` makes it more likely.

**What breaks if you ignore it** — accepting a text column reports success, publishes the schema, and the
lookup stays uncolored on every page with no error anywhere. Resolving the color column by NAME instead of by
the schema reference picks `CaseStatus.Color` (text) and writes colors that are never shown. If a live stand
shows the runtime payload uses neither field name, merged-mode `primary-color-column-name` stays `null` -
fix the mapping in `RuntimeEntitySchemaReader` and delete the "not yet observed" sentence above.
