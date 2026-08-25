---
description: the built-in curated knowledge library is KnowledgeSourceType.GitHubRelease over the clio-knowledge-bundle.zip release asset, so a guidance change reaches clio only when clio-knowledge publishes a RELEASE - an unpublished generation cannot be exercised through supported commands
applies-to:
  - clio/Command/McpServer/Knowledge/CuratedKnowledgeBootstrapService.cs
  - clio/Command/McpServer/Knowledge/KnowledgeSourceManagementService.cs
  - clio/Command/McpServer/Knowledge/KnowledgeBundleContracts.cs
date: 2026-08-19
---

**What is true** — `CuratedKnowledgeSourceDefaults.CreateConfiguration` configures the built-in
`com.creatio.clio` source as `KnowledgeSourceType.GitHubRelease` against the fixed asset
`clio-knowledge-bundle.zip`. Merging a `clio-knowledge` pull request therefore changes nothing for any
clio installation; only a published release does. There is also no supported way to preview an
unpublished generation locally: the built-in source cannot be removed
(`KnowledgeSourceManagementService.cs:191`), a second source claiming the same alias or library id is
refused (`:165`), MCP startup restores the canonical configuration, and
`BuiltInKnowledgeBundleTrustStore.TrustedKeys` pins `com.creatio.clio` to the single key id
`clio-knowledge-2026-08`, so a locally signed bundle does not verify. The only redirect that exists is
`CLIO_KNOWLEDGE_CURATED_API_BASE_URL`, which is accepted for loopback origins only and exists for
hermetic tests.

**Why it is this way** — the library is a product surface delivered to users, so its trust anchor is
compiled in and its delivery channel is an immutable signed artifact rather than a branch anyone can
point at.

**What breaks if you ignore it** — a pull request that depends on new article content is described as
gated on a producer merge and is then merged with the content unreachable, so `get-guidance` answers
from the previously released generation and the shipped instructions point at guides that do not exist
for the user. Plan the release as the gate, and expect to verify against a real published generation
rather than a local build.
