---
description: the docs tier picks which *_LOCAL_FILE override to honour from the documentation path prefix (docs/, mobile-docs/, request-docs/, mobile-request-docs/) - a registry that emits a foreign prefix silently reads the wrong working copy
applies-to:
  - clio/Command/McpServer/Tools/ComponentRegistryDocsPath.cs
  - clio/Command/McpServer/Tools/ComponentRegistryDocsClient.cs
ticket: "#1361"
date: 2026-09-05
---

**What is true** — `ComponentRegistryDocsClient` has no idea which registry flavour a
`references.docs[]` path came from: it is one DI singleton shared by the web, mobile, requests and
mobile-requests catalogs, and `GetDocAsync` receives only `(version, docPath)`. It therefore infers
the owning flavour from the path's documentation namespace via
`ComponentRegistryDocsPath.TryResolveFlavor` — `docs/` → `RegistryFlavor.Web`, `mobile-docs/` →
`Mobile`, `request-docs/` → `Requests`, `mobile-request-docs/` → `MobileRequests` — and consults
that flavour's `*_LOCAL_FILE` environment variable to find the working copy.

**Why it is this way** — threading the flavour through would mean changing
`IComponentRegistryDocsClient`, `ComponentDocumentationLoader` and every call site in
`ComponentInfoTool`, `RequestInfoTool` and `ComponentInfoCommand`, all to re-state a fact the
producer already encodes in the path. The one-flavour-one-namespace convention was verified against
the live CDN payloads (`ComponentRegistry.json` publishes only `docs/`,
`MobileComponentRegistry.json` only `mobile-docs/`, `RequestRegistry.json` only `request-docs/`,
`MobileRequestRegistry.json` only `mobile-request-docs/`) and against the live-snapshot fixtures in
`clio.tests/Command/McpServer/Fixtures/`.

**What breaks if you ignore it** — if the producer ever publishes a cross-namespace reference (say a
`docs/...` path inside `MobileComponentRegistry.json`), the docs tier resolves it against the *web*
override directory. With `CLIO_COMPONENT_REGISTRY_LOCAL_FILE` set and
`CLIO_MOBILE_COMPONENT_REGISTRY_LOCAL_FILE` unset, the mobile recipe is then looked for in the web
working copy and reported as `documentationSource: "none"`; with both set, it is read from the wrong
one. Nothing fails loudly — the response just carries the wrong prose or a warning naming a path in a
directory the developer never edited. If cross-namespace references become real, stop inferring and
pass `RegistryFlavor` explicitly from the two tools, which already know their `schema-type`.
