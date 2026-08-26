---
description: guidance and reference articles are gated only by their own requiredFeatures list - publishing an article for a resource that carried [FeatureToggle(...)] without copying the feature key silently un-gates it
applies-to:
  - clio/Command/McpServer/Knowledge/KnowledgeGuidanceSource.cs
  - clio/Command/McpServer/Knowledge/KnowledgeReferenceExampleService.cs
  - clio/Command/McpServer/Knowledge/KnowledgeGitRepositoryReader.cs
date: 2026-08-19
---

**What is true** — for a published knowledge article the only feature gate clio applies is the
article's own `requiredFeatures`: `KnowledgeGuidanceSource` treats an article as visible when
`(article.RequiredFeatures ?? []).All(_featureToggleService.IsFeatureEnabled)`, and
`KnowledgeReferenceExampleService` does the same for reference examples. An empty or absent list means
"always visible". Nothing in the reader correlates an article with the `[FeatureToggle(...)]`
attribute that may have gated the C# resource or tool the article was extracted from.

**Why it is this way** — guidance content no longer lives in this repository; it is authored in
`clio-knowledge` and delivered as a signed bundle. The CLI/MCP feature toggle is an attribute on a
type in this assembly, so it cannot travel with the Markdown. The article has to restate the gate as
data, and `KnowledgeGitRepositoryReader` validates the `requiredFeatures` identifiers it finds.

**What breaks if you ignore it** — externalizing a gated guide without copying its feature key makes
an experimental, not-for-public topic reachable through `get-guidance` on every installation, with the
toggle switched off and no error and no log line anywhere. The gate looks intact because the tool it
belongs to is still hidden; only the article leaks.
