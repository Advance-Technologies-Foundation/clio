---
description: MobileComponentRegistry.live-snapshot.json is a hand-refreshed pin whose ENTRY SET nothing guards, so a converter test that derives its mobile type set from it can silently invert what it proves - an excluded type absent from the snapshot is dropped as unsupported and the exclusion rule under test never runs
applies-to:
  - clio.tests/Command/McpServer/Fixtures/MobileComponentRegistry.live-snapshot.json
  - clio.tests/Command/McpServer/Tools/MobilePageConverter/WebToMobileRealPageRegressionTests.cs
  - clio.tests/Command/McpServer/Tools/MobilePageConverter/WebToMobilePropertyPruneTests.cs
ticket: ENG-95081
date: 2026-08-26
---

**What is true** — `clio.tests/Command/McpServer/Fixtures/MobileComponentRegistry.live-snapshot.json` is a pin
with its own refresh cadence. On 2026-08-26 it carried **35** components while
`https://academy.creatio.com/api/mcp/latest/MobileComponentRegistry.json` served **47**; among the twelve it
lacked were `crt.SearchFilter`, `crt.QuickFilter` and `crt.QuickFilterGroup` — exactly the types the
`excludedComponents` rules target. ENG-96589 refreshed it to the runtime-derived 66-component catalog, but
the mechanism that let it drift is unchanged.

The live catalog is what runs in production: `MobilePageConversionGuideTool` builds its `mobileTypes` set from
`_mobileCatalog.LoadAsync(...)` (cache → CDN → `latest`), never from this fixture. The fixture exists only for
`ComponentRegistrySnapshotTests`, whose guard checks for UNMAPPED FIELDS.

Partial mitigation since ENG-96589: that guard now also asserts a COUNT FLOOR (`> 60`) and the presence of the
`mobileRuntimeVersion` marker, so a wholesale regression to an older, smaller pin fails. A floor is not a
freshness check — the producer adding a 67th component still trips nothing.

**Why it is this way** — the snapshot is refreshed by hand (`curl … > <fixture>`) when someone notices a
producer-side schema change. Nothing refreshes it when the producer merely ADDS components, because adding a
component changes no schema and trips no guard.

**What breaks if you ignore it** — a converter test that derives its mobile type set from this fixture, which
looks like the most faithful thing to do, quietly asserts the opposite of what it reads as asserting. With
`crt.SearchFilter` missing from the type set, the converter drops it as `type 'crt.SearchFilter' not in mobile
registry` BEFORE any `excludedComponents` filter runs, so the test passes green while the exclusion rule it
names is never executed — and would keep passing if that rule were deleted outright. This was observed while
building `WebToMobileRealPageRegressionTests`, which is why that class derives its type set from the page under
test plus an explicit assertion that the banned type resolves, and states the reason inline.

If you need a realistic mobile type set in a test, either declare it explicitly or refresh the fixture first
and confirm the entry count moved; do not assume the pin is current.

A second consumer now depends on this fixture being the RUNTIME-DERIVED generation, not merely current:
`WebToMobilePropertyPruneTests` drives the ENG-96589 prune from it, and the prune's own gate switches itself
off against a catalog with no `mobileRuntimeVersion`. A pin that regressed to the old generation would leave
that whole fixture passing while asserting nothing, which is why it opens with an explicit generation check.
