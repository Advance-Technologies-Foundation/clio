---
description: the runtime-derived MobileComponentRegistry is published only under /latest/; every versioned path still serves the web-derived catalog, which is why the converter's property prune is gated on mobileRuntimeVersion AND a version floor
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
| `/api/mcp/latest/` | 66 | present (`release`, `commit`) | introspected from the Flutter runtime |
| `/api/mcp/10.0.0/` | 46 | absent | derived from the Angular web components |
| `/api/mcp/8.3.4/` | 45 | absent | same |
| `/api/mcp/8.3.3/` | 42 | absent | same |
| `/api/mcp/8.3.0/` | **3** | absent | same |
| `/api/mcp/8.3.5/`, `/api/mcp/9.0.0/` | — | — | 404 → the client falls back to `latest` |

Measured 2026-09-17. The runtime-derived catalog is published **only** under `latest`.

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

Hence the gate is two conditions, not one, and both are load-bearing:

1. the loaded payload carries the top-level `mobileRuntimeVersion` marker, and
2. the ENVIRONMENT's resolved platform version is `latest` or strictly above `10.0.0`.

Condition 2 exists because condition 1 is not sufficient: a stand on **8.3.5** has no published versioned
mobile registry, so `ComponentRegistryClient` Tier 3 serves it `latest` — the marker IS present, but that
stand runs an older mobile runtime than the catalog describes, and pruning against a newer runtime removes
properties the stand actually supports. Condition 1 exists because condition 2 is not sufficient either:
were a version above the floor ever published in the old generation, the version test alone would admit it.

When versioned runtime-derived files start being published, neither condition needs changing — the floor
only ever admits versions that did not exist while the old generation was current.
