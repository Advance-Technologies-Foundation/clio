---
description: MobileComponentRegistry.json and mobile-docs/ are produced from the Flutter runtime by mobile-app-component-registry-generator, not from creatio-ui
applies-to:
  - clio/Command/McpServer/Tools/ComponentRegistryClient.cs
  - clio/Command/McpServer/Tools/ComponentInfoCatalog.cs
  - clio.tests/Command/McpServer/Fixtures/MobileComponentRegistry.live-snapshot.json
ticket: ENG-98482
date: 2026-09-22
---

**What is true** — the two catalog flavours `get-component-info` serves have different producers and
different source languages. `ComponentRegistry.json` + `docs/` + `RequestRegistry.json` +
`request-docs/` come from `crt-monorepo-component-registry-generator`, extracted from the
`creatio-ui` Angular design-time classes. `MobileComponentRegistry.json` + `mobile-docs/` +
`MobileRequestRegistry.json` + `mobile-request-docs/` come from a different repository,
`mobile-app-component-registry-generator`, extracted from the **Flutter** runtime in `mobile-app`.
Both publish into the same `static-files-mcp` version folder under a shared Jenkins lock, so the two
halves of a single `/api/mcp/{version}/` namespace are written by two jobs from two codebases.

**Why it is this way** — the mobile registry used to be a second `build()` call in the web generator
over `creatio-ui`'s `CrtMobileViewElement` design-time classes. ENG-98015 (15 Sep 2026) stopped that:
those Angular classes describe the mobile DESIGNER, while the contract an agent authors against is
executed by the Flutter app, and the two had drifted. The web generator now explicitly excludes the
mobile artefacts from the set it regenerates and deletes.

**What breaks if you ignore it** — a fix authored in `creatio-ui`'s
`*/mobile-design-time/*.component.md` changes nothing any clio caller can see, and nothing reports
that: the file still exists, the PR still merges, the nightly web job still turns green, and
`get-component-info --schema-type mobile` keeps serving whatever the Flutter generator last
published. ENG-98482 spent its first week aimed at that file and at the web generator, and a merged
`creatio-ui` PR would have read as the fix. Read `mobile-docs/<name>.component.md` off the CDN and
compare it with the file you are about to edit before assuming they are the same document — the
Flutter generator does not even reuse the web naming (`indicator_widget.component.md`, not
`mobile-indicator-widget.component.md`).
