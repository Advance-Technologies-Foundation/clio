---
description: the runtime-derived MobileComponentRegistry is published only under /latest/ while every versioned path still serves the web-derived catalog, which is why the converter's property prune is gated on a version floor plus the inherited baseInputs surface
applies-to:
  - clio/Command/McpServer/Tools/MobilePageConverter/WebToMobilePropertyPrune.cs
  - clio/Command/McpServer/Tools/MobilePageConverter/MobilePageConversionGuideTool.cs
  - clio.tests/Command/McpServer/Tools/MobilePageConverter/WebToMobilePropertyPruneTests.cs
  - clio.tests/Command/McpServer/Tools/MobilePageConverter/MobilePageConversionGuidePruneWiringTests.cs
ticket: ENG-96589
date: 2026-09-21
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

Measured 2026-09-17/18. `MobilePageConversionGuideTool` resolves the version against the TARGET ENVIRONMENT
and asks for exactly that version, so a real stand on 8.3.4 or 10.0.0 receives the **web-derived** catalog.

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
mobile design-time (Angular), and is now generated from `flutter_creatio`. The versioned files predate that
move and have not been regenerated. The two generations do not differ merely in coverage — they describe
different things. The old `crt.Feed` declares only `primaryColumnValue`; the old `crt.GridContainer`
declares `rows`, which the mobile runtime does not implement at all.

**What breaks if you ignore it** — the property prune (ENG-96589) treats registry membership as proof that
mobile supports a property. Run it against a versioned path and it inverts into a page-destroying pass:
against `10.0.0` it strips `crt.Feed.entitySchemaName`; against `8.3.0` — three components — it strips
essentially every property of every element on the page. Nothing errors; `validate-page` and
`update-page --dry-run` both pass, and the loss appears only when a human opens the page on a device.

Hence a three-part gate, each part covering a case the others do not:

1. the ENVIRONMENT's platform version is **positively known** — `PlatformVersionResolver` returns the
   literal `"latest"` for every failure class, so that string alone says nothing about the stand;
2. that version normalises to a 3-part semver strictly above `10.0.0`. The literal `latest` is REFUSED
   here: it names a catalog, not a stand, and an explicit `version` argument bypasses the probe — so
   accepting it would prune an 8.3.5 box against a runtime it does not run;
3. the loaded payload carries the runtime-derived inherited surface (`layoutConfig` + `visible`).

Condition 2 exists because a stand on **8.3.5** has no published versioned registry and is served `latest`
anyway. Condition 3 exists because condition 2 is not sufficient: were a version above the floor ever
published in the old generation, the version test alone would admit it. Normalising in condition 2 is not
cosmetic — a real core version is 4-part (`10.0.0.934`), and `System.Version` compares its Revision against
the floor's implicit `-1`, so a raw comparison reads it as ABOVE `10.0.0`.

When versioned runtime-derived files start being published, no condition needs changing — the floor only
ever admits versions that did not exist while the old generation was current.
