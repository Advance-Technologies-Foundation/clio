---
description: MobileComponentRegistry.live-snapshot.json lags the published mobile catalog (35 entries vs 47), so a converter test that derives its mobile type set from it silently inverts what it proves - an excluded type absent from the snapshot is dropped as unsupported and the exclusion rule under test never runs
applies-to:
  - clio.tests/Command/McpServer/Fixtures/MobileComponentRegistry.live-snapshot.json
  - clio.tests/Command/McpServer/Tools/MobilePageConverter/WebToMobileRealPageRegressionTests.cs
ticket: ENG-95081
date: 2026-08-26
---

**What is true** — `clio.tests/Command/McpServer/Fixtures/MobileComponentRegistry.live-snapshot.json` is a pin
with its own refresh cadence, and on 2026-08-26 it carried **35** components while
`https://academy.creatio.com/api/mcp/latest/MobileComponentRegistry.json` served **47**. Among the twelve it
lacks are `crt.SearchFilter`, `crt.QuickFilter` and `crt.QuickFilterGroup` — exactly the types the
`excludedComponents` rules target.

The live catalog is what runs in production: `MobilePageConversionGuideTool` builds its `mobileTypes` set from
`_mobileCatalog.LoadAsync(...)` (cache → CDN → `latest`), never from this fixture. The fixture exists only for
`ComponentRegistrySnapshotTests`, whose guard checks for UNMAPPED FIELDS — it does not check that the entry set
is current, so the lag produces no failing test.

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
