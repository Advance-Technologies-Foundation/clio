---
description: MobileComponentRegistry.json exists in two generations and only the runtime-derived one is a valid statement of mobile support, so the converter's property prune is gated on the CONTENT of the payload it loaded (the inherited baseInputs surface) and not on the stand's platform version - which is what lets a regenerated versioned file switch pruning on for that version with no clio release
applies-to:
  - clio/Command/McpServer/Tools/MobilePageConverter/WebToMobilePropertyPrune.cs
  - clio/Command/McpServer/Tools/MobilePageConverter/MobilePageConversionGuideTool.cs
  - clio.tests/Command/McpServer/Tools/MobilePageConverter/WebToMobilePropertyPruneTests.cs
  - clio.tests/Command/McpServer/Tools/MobilePageConverter/MobilePageConversionGuidePruneWiringTests.cs
ticket: ENG-96589
date: 2026-09-22
---

**What is true** — `MobileComponentRegistry.json` exists in two generations, and only one of them is a valid
statement of what the Creatio Mobile runtime supports.

| Path | components | generation |
|---|--:|---|
| `/api/mcp/latest/` | ~65 | introspected from the Flutter runtime |
| `/api/mcp/10.0.0/` | 46 | derived from the Angular web components |
| `/api/mcp/8.3.4/` | 45 | same |
| `/api/mcp/8.3.3/` | 42 | same |
| `/api/mcp/8.3.0/` | **3** | same |
| `/api/mcp/8.3.5/`, `/api/mcp/9.0.0/` | — | 404 → the client falls back to `latest` |

Measured 2026-09-17/18. **The versioned files are being regenerated from the mobile runtime**, so those
counts are a snapshot of a migration in progress, not a stable fact — re-measure before relying on a row.

What tells the generations apart is the inherited surface, which is disjoint apart from `name`/`type`:

| generation | `references.baseInputs` |
|---|---|
| web-derived | `classes, id, loading, name, shape, styles, tabIndex, type` |
| runtime-derived | `_filterOptions, adaptive, bindTo, flexConfig, key, layoutConfig, name, type, visible` |

`layoutConfig` is the Flutter layout model itself, not a coincidence of naming, and no web-derived payload
has ever carried it. Sniffing for the mere PRESENCE of `references` or `baseInputs` discriminates nothing —
every old payload ships that block too. Neither does the `mobileRuntimeVersion` marker; see
[the marker record](mobile-runtime-version-marker-is-not-dependable.md).

**Why it is this way** — the mobile catalog moved producers: it used to be generated from `creatio-ui`'s
mobile design-time (Angular), and is now generated from `flutter_creatio`. The two generations do not
differ merely in coverage — they describe different things. The old `crt.Feed` declares only
`primaryColumnValue`; the old `crt.GridContainer` declares `rows`, which the mobile runtime does not
implement at all.

**What breaks if you ignore it** — the property prune (ENG-96589) treats registry membership as proof that
mobile supports a property. Run it against a web-derived payload and it inverts into a page-destroying
pass: against `10.0.0` it strips `crt.Feed.entitySchemaName`; against `8.3.0` — three components — it
strips essentially every property of every element on the page. Nothing errors; `validate-page` and
`update-page --dry-run` both pass, and the loss appears only when a human opens the page on a device.

The gate is therefore one condition, and it reads the PAYLOAD: the loaded registry must carry the
runtime-derived inherited surface (`layoutConfig` **and** `visible` in `references.baseInputs`,
case-insensitively). Nothing about the stand's platform version is consulted.

That is a deliberate reversal of the first design, which also required the environment's version to be
positively known and above a `10.0.0` floor. The floor protected old stands by refusing to prune them; a
regenerated per-version registry does better, because it describes the runtime that version actually runs,
so an old stand is pruned CORRECTLY instead of merely spared. Reading content rather than a version number
is what makes the transition need no clio release: a path still serving the old generation fails the check
and the prune stays off for it, and it switches itself on the moment the regenerated file is published.

Two things the check cannot do, both of which live with the registry producer:

- A regenerated versioned file that is a COPY of `latest` rather than a description of that version's own
  runtime passes every check clio can make, and silently prunes an old stand against a newer runtime.
- A stand whose version has no published registry at all (`8.3.5`, `9.0.0`) still falls back to `latest`
  and is measured against the newest runtime. The response reports this as `resolvedFrom:
  environment-superset` with a `versionWarning`; it is not silent, but it is not refused either.
