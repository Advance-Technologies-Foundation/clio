---
description: the runtime-derived MobileComponentRegistry is published only under /latest/, every versioned path still serves the web-derived catalog, and the mobileRuntimeVersion provenance marker is NOT dependable - which is why the converter's property prune is gated on a version floor plus the inherited baseInputs surface
applies-to:
  - clio/Command/McpServer/Tools/MobilePageConverter/WebToMobilePropertyPrune.cs
  - clio/Command/McpServer/Tools/MobilePageConverter/MobilePageConversionGuideTool.cs
  - clio/Command/McpServer/Tools/ComponentInfoTool.cs
  - clio/Command/McpServer/Tools/ComponentInfoCatalog.cs
ticket: ENG-96589
date: 2026-09-17
---

**What is true** — `MobileComponentRegistry.json` exists in two generations, and only one of them is a valid
statement of what the Creatio Mobile runtime supports.

| Path | components | `mobileRuntimeVersion` | generation |
|---|--:|---|---|
| `/api/mcp/latest/` | ~65 | **unreliable** (see below) | introspected from the Flutter runtime |
| `/api/mcp/10.0.0/` | 46 | absent | derived from the Angular web components |
| `/api/mcp/8.3.4/` | 45 | absent | same |
| `/api/mcp/8.3.3/` | 42 | absent | same |
| `/api/mcp/8.3.0/` | **3** | absent | same |
| `/api/mcp/8.3.5/`, `/api/mcp/9.0.0/` | — | — | 404 → the client falls back to `latest` |

Measured 2026-09-17/18. The runtime-derived catalog is published **only** under `latest`.

**The `mobileRuntimeVersion` marker cannot be used to tell the generations apart.** It appeared on `latest`
carrying `{release: main, commit: d7a0c3bb...}`, and was gone again when the producer republished the file
at 14:15 GMT the same day — while the CONTENT stayed runtime-derived: identical `references.baseInputs`,
byte-identical component entries, only `crt.DataGrid` (and the 7 type definitions that became unreachable
without it) deliberately removed. A provenance field that survives two hours is not a contract, and a
feature gated on it switches itself off without anything failing.

What DOES tell them apart is the inherited surface, because the two sets are disjoint apart from
`name`/`type`:

| generation | `references.baseInputs` |
|---|---|
| web-derived | `classes, id, loading, name, shape, styles, tabIndex, type` |
| runtime-derived | `_filterOptions, adaptive, bindTo, flexConfig, key, layoutConfig, name, type, visible` |

`layoutConfig` is the Flutter layout model itself, not a coincidence of naming, and no web-derived payload
has ever carried it.

`MobilePageConversionGuideTool` resolves the version against the TARGET ENVIRONMENT and asks the registry
client for exactly that version, so a real stand on 8.3.4 or 10.0.0 receives the **web-derived** catalog.

**Why it is this way** — the mobile catalog moved producers. It used to be generated from
`creatio-ui`'s mobile design-time (Angular); it is now generated from `flutter_creatio`. The versioned
files predate that move and have not been regenerated. The two generations are not merely different in
coverage — they describe different things. The old `crt.Feed` declares only `primaryColumnValue`; the old
`crt.GridContainer` declares `rows`, which the mobile runtime does not implement at all.

There is also no structural tell other than the marker: every old payload ships a `references.baseInputs`
block too, just filled with web attributes (`classes`, `shape`, `tabIndex`). Sniffing for the presence of
`references` or `baseInputs` does NOT discriminate the generations.

**What breaks if you ignore it** — the converter's property prune (ENG-96589) treats registry membership as
proof that mobile supports a property. Run it against a versioned path and it inverts into a page-destroying
pass: against `10.0.0` it strips `crt.Feed.entitySchemaName`; against `8.3.0` — three components — it strips
essentially every property of every element on the page. Nothing errors. `validate-page` and
`update-page --dry-run` both pass, and the loss appears only when a human opens the page on a device.

Hence the gate is three conditions, and each covers a case the others do not:

1. the ENVIRONMENT's platform version is **positively known** — `PlatformVersionResolver` returns the
   literal `"latest"` for every failure class, so that string on its own says nothing about the stand;
2. that version is `latest` or, after normalising to 3 parts, strictly above `10.0.0`;
3. the loaded payload carries the runtime-derived inherited surface (`layoutConfig` + `visible` in
   `references.baseInputs`).

Condition 2 exists because a stand on **8.3.5** has no published versioned mobile registry, so
`ComponentRegistryClient` Tier 3 serves it `latest` — a catalog describing a runtime NEWER than the one that
stand runs. Condition 3 exists because condition 2 is not sufficient either: were a version above the floor
ever published in the old generation, the version test alone would admit it.

Normalising in condition 2 is not cosmetic: a real Creatio core version is 4-part (`10.0.0.934`), and
`System.Version` compares its Revision against the floor's implicit `-1`, so a raw comparison reads it as
ABOVE `10.0.0` and prunes the very generation the floor is named after.

When versioned runtime-derived files start being published, no condition needs changing — the floor only
ever admits versions that did not exist while the old generation was current.
