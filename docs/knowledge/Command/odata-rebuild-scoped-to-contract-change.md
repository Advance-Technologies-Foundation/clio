---
description: OData entities are rebuilt only when a mutation changes the published OData contract (name/EDM type/nullability/navigation properties) - required and every other designer-only flag does not, and nothing but this request path ever rebuilds it
applies-to:
  - clio/Command/EntitySchemaDesigner/ODataContractImpact.cs
  - clio/Command/EntitySchemaDesigner/EntitySchemaPublisher.cs
  - clio/Command/EntitySchemaDesigner/RemoteEntitySchemaColumnManager.cs
ticket: 1278
date: 2026-08-30
---

**What is true** — the OData `$metadata` document Creatio publishes for an entity carries only four
things per property: the property name, its EDM type, its nullability, and navigation properties
(for lookups/references). Measured on a live stand by publishing a mutation with the rebuild
suppressed and diffing `$metadata` before and after: caption, description, default value, mask,
usage type, and the `required` flag are all absent from the document, so a mutation that only
touches one of those leaves `$metadata` byte-for-byte identical. `required` in particular does not
map onto the EDM `Nullable` attribute — the two are tracked separately, so setting `required` is not
a nullability change from OData's point of view. Only adding/removing a column, renaming a column
(`--new-name`, which is literally the property name the document publishes), changing a column's
type, or changing a column's reference schema alters the document, and only those mutations request
the rebuild (`ODataContractImpact.Changed`); everything else takes the `Unchanged` branch and skips
it. `WorkspaceBuilder.BuildOData` — the method that actually recompiles the OData entities assembly —
has exactly one caller in the whole platform: the server-side background task that
`WorkspaceExplorerService.RunODataBuild` queues, which is what clio asks for from
`EntitySchemaPublisher`. No other save, publish, or compile path ever triggers it.

**Why it is this way** — the rebuild is a 90-120s server-side compilation that also holds
`conf\_MetaInfo.json` open for its whole duration, so a publish that starts inside that window fails
on a sharing violation. Requesting it after every mutation, including ones that cannot change what
gets published, buys nothing (the document is identical) while paying the full cost and colliding
with the very next publish.

**What breaks if you ignore it** — treating `required`, or any other designer-only property, as
contract-changing reintroduces a rebuild the diffed `$metadata` proves is a no-op: the caller waits
90-120s and collides with adjacent publishes for zero observable difference in what OData serves. In
the other direction, assuming some other code path (a plain `SaveSchema`, a `compile-creatio` run, an
unrelated command) will pick up the slack and rebuild OData for a genuine contract change is also
wrong — `BuildOData` has the one caller named above, so skip the rebuild request here and the new
column silently never becomes reachable over OData until something explicitly calls
`RunODataBuild` again.
