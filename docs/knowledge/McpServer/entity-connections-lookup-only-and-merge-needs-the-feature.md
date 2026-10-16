---
description: update-entity-connections accepts only lookup columns although the platform stores any column, and it refuses to write when GetConnections does not reflect the EntityConnections add-on (UseConnectionsComponent off) or fails over to the EntityConnection table
applies-to:
  - clio/Command/EntityConnections/
  - clio/Command/McpServer/Tools/EntityConnectionsTools.cs
ticket: ENG-101678
date: 2026-10-16
---

**What is true** — two rules of `update-entity-connections` are not visible in the platform contract.

1. *Lookup-only is a clio decision.* The core `EntityConnections` add-on and its `SaveSchema` validation accept
   any column of the object (they reject only an empty, repeated or foreign `ColumnUId`). clio rejects a non-lookup
   column because the Object designer "Connections" node offers only lookups and the product owner chose to keep
   the two in step.
2. *The diff needs the merged list.* The add-on stores only the difference from the parent layers, so the write is
   computed against the effective list from CrtUIv2 `EntityConnectionsService.GetConnections`. That service applies
   the add-on only when the `UseConnectionsComponent` feature is on; with it off (or with the endpoint missing, when
   clio falls back to the `EntityConnection` table ordered by `Position`) the list carries no add-on items or
   removals. clio then refuses the write, the same way the Object designer makes the node read-only, and
   `get-entity-connections` reports a warning instead of a `featureEnabled` flag — clio has no reader for feature
   values, so the mismatch between the list and the add-on content is what it checks.

**Why it is this way** — the backend accepting any column is the platform's contract, not a product promise; the
UI is the product. And a diff computed from a list that ignores the add-on rewrites the add-on from incomplete data.

**What breaks if you ignore it** — dropping the lookup check lets an agent add a text column that the Freedom UI
`crt.Connections` component and the designer never offer; dropping the refusal turns a reorder on an environment
with the feature off into removals and items that wipe the order other packages stored, and nothing fails until the
feature is turned on.
