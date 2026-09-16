---
description: the AutoTestClioMcp seed app holds mobile and Detail pages the mobile converter refuses by contract, so a Sandbox fixture that enumerates list-pages must filter on the response sourceType - and because the Detail lands in the same "unknown" bucket a detection regression would, an empty surviving set must fail rather than ignore
applies-to:
  - clio.mcp.e2e/MobilePageConversionGuideSandboxE2ETests.cs
ticket: gh-1382
date: 2026-09-04
---

**What is true** — `list-pages` on the `AutoTestClioMcp` seed application returns pages
`get-mobile-page-conversion-guide` refuses by contract, not by accident: `create-app` generates
`<Entity>_MobileFormPage` and `<Entity>_MobileListPage` alongside the web pages unless
`with-mobile-pages=false`, and the app also carries a Detail schema. `RejectUnsupportedSourceType` refuses
an already-mobile source and any non-`freedom-web` source, and that is correct product behaviour. A
fixture separates such a refusal from a runtime error by the response's structured `sourceType` — the
supported signal, unlike error text or schema names. It is present on the type-gate refusals but NOT on
every failure: a page-read failure reports `sourceType: null`, so only a *populated* non-`freedom-web`
value may filter a page out.

**Why it is this way** — `list-pages` filters on `ManagerName = ClientUnitSchemaManager` only. It is a
general MCP tool with other callers, so narrowing it to please one fixture would break page discovery for
everyone else, and `PageListItem` carries no schema type, so the listing cannot answer the question. Its
`ParentSchemaName` discriminates a mobile template from a web one more cheaply than a conversion call, but
it is a heuristic over template names; asking the converter is the product's own answer and is what the
fixture must ultimately agree with.

**What breaks if you ignore it** — the seven `MobilePageConversionGuideSandboxE2ETests` tests failed on
almost every run where they executed, for three seeded pages behaving exactly as designed, while asserting
the opposite ("this is a runtime regression, not missing seed data"). The trap on the way out is that a
Detail reports `sourceType` `unknown`, which is also what a source-type *detection* regression produces
for every page at once — `DetectSourceType` returns `unknown` whenever the platform's `schemaType` is
absent or unrecognised. Filtering `unknown` is therefore necessary and, on its own, a way to turn a total
regression into seven green skips. That is why an empty surviving candidate set fails: the seed always
carries Freedom UI web pages, so "every page was refused" can only be the product.
