---
description: the AutoTestClioMcp seed app holds mobile and Detail pages the mobile converter rejects by contract, so a Sandbox fixture that enumerates list-pages must filter on the response's sourceType before treating a rejection as a regression
applies-to:
  - clio.mcp.e2e/MobilePageConversionGuideSandboxE2ETests.cs
ticket: "1382"
date: 2026-09-04
---

**What is true** — `list-pages` on the `AutoTestClioMcp` seed application returns pages
`get-mobile-page-conversion-guide` refuses by contract, not by accident: `create-app` generates
`<Entity>_MobileFormPage` and `<Entity>_MobileListPage` alongside the web pages unless
`with-mobile-pages=false`, and the app also carries a Detail schema. The converter rejects an
already-mobile source and any non-`freedom-web` source in `RejectUnsupportedSourceType`, and that is
correct product behaviour. A fixture therefore cannot equate "the seeded page did not convert" with
"the converter regressed". The response carries the detected type in its structured `sourceType`
field **even on failure**, and that field — not the error text, and not the schema name — is the
supported way to tell a by-contract refusal from a runtime error.

**Why it is this way** — `list-pages` filters on `ManagerName = ClientUnitSchemaManager` only. It is
a general MCP tool with other callers, so it must keep returning every client-unit schema; narrowing
it to please one fixture would break page discovery for everyone else. `PageListItem` carries no
schema type, so the type cannot be read from the listing either.

**What breaks if you ignore it** — the seven `MobilePageConversionGuideSandboxE2ETests` tests failed
on every run where they executed at all, for three seeded pages behaving exactly as designed, and the
failure text asserted the opposite ("this is a runtime regression, not missing seed data"). Because
the schema-name heuristic that picked a single page matched `_ListPage` or `_MobileListPage`
depending on the order the platform happened to return, the same defect also made the single-page
test pass about a quarter of the time — a coin flip that reads as flakiness and hides the cause.
