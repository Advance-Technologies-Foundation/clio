---
description: CLIO_REQUEST_REGISTRY_LOCAL_FILE and the other *_LOCAL_FILE overrides serve only registry JSON - to iterate on request/component recipe markdown point CLIO_COMPONENT_REGISTRY_CDN_BASE_URL at a local HTTP server instead
applies-to:
  - clio/Command/McpServer/Tools/ComponentRegistryDocsClient.cs
  - clio/Command/McpServer/Tools/ComponentRegistryClient.cs
  - clio.tests/Command/McpServer/RequestRegistrySnapshotTests.cs
ticket: ENG-93878
date: 2026-08-19
---

**What is true** — the four `*_LOCAL_FILE` developer overrides listed in
`clio/Command/McpServer/AGENTS.md` are implemented in `ComponentRegistryClient` only.
`ComponentRegistryDocsClient` has no local-file tier at all: it knows only the shared
`CLIO_COMPONENT_REGISTRY_CDN_BASE_URL` base plus its disk cache. To serve changed recipe markdown
(`request-docs/*.request.md`, `docs/*.component.md`) from a working copy, point that base URL at a
local server over the `static-files-mcp` checkout root - clio builds `{base}{version}/{path}`, and
the repository already has the matching `latest/` layout, so registry JSON *and* the recipes both
come from the working copy.

**Why it is this way** — the overrides were added per registry flavour, each one a single JSON
document read before cache and CDN. Documentation is a fan-out of N paths discovered inside the
payload, so the same single-file idiom does not fit, and no equivalent was built.

**What breaks if you ignore it** — with only a `*_LOCAL_FILE` override set, a `get-request-info`
round-trip against an unpublished recipe returns `documentation: null`, which is indistinguishable
from a wrong `references.docs[]` path or a broken docs client. Worse, nothing on the clio side can
catch a wrong recipe later: `RequestRegistrySnapshotTests` pins the registry JSON schema, never the
prose, so a factually false recipe ships silently and is only visible by fetching it.
