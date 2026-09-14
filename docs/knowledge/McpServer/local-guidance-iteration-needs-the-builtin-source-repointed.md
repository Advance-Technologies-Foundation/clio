---
description: the creatio-curated knowledge source is BUILT IN - it cannot be removed and it holds library-id com.creatio.clio even while disabled - so a second source for local clio-knowledge iteration cannot be registered, and the only route is repointing that entry in appsettings.json to a git branch plus the knowledge-allow-unsequenced flag
applies-to:
  - clio/Command/McpServer/Knowledge/KnowledgeSourceManagementService.cs
  - clio/Command/McpServer/Knowledge/KnowledgeSourceConfiguration.cs
  - clio/Command/KnowledgeCommands.cs
ticket: ENG-94374
date: 2026-09-03
---

**What is true** — testing an unpublished `clio-knowledge` article locally looks like a job for a
second knowledge source, and `add-knowledge-source` appears to support it. It does not work, for three
reasons that only surface in this order:

1. `--library-id` must be **unique across configured sources**, and disabling a source does not release
   its identity. `disable-knowledge-source --alias creatio-curated` succeeds and changes nothing about
   the collision.
2. `creatio-curated` is a **built-in** source: `remove-knowledge-source --force` refuses it by name and
   tells you to disable it instead. So `com.creatio.clio` is permanently reserved on that machine.
3. The configured library id must equal the `libraryId` inside `bundle-source.json`
   (`KnowledgeSourceManagementService.ShouldRejectCandidate`, ordinal compare), so registering a dev
   source under a different id means editing the manifest on the branch — which makes the branch
   content wrong for its own pull request.

The route that works is to repoint the built-in entry in
`%LOCALAPPDATA%\creatio\clio\appsettings.json` under `knowledge.sources.creatio-curated`: replace
`type: github-release` and its `repository-*` / `asset-name` fields with `type: git`, `location` the
repository HTTPS URL and `branch` the feature branch, then `install-knowledge --source
creatio-curated`. Git sources need no signing key, which is what makes this cheap. Enable
`knowledge-allow-unsequenced` first (`clio experimental --name knowledge-allow-unsequenced --enable`):
`bundle-source.json` carries no `sequence` field — the release build derives it from `libraryVersion` —
and without the flag a Git manifest that omits it will not load, and re-installing edited content under
an unchanged `libraryVersion` is refused.

Two further facts that cost time on their own:

- A **local path or `file://` location is rejected.** `ValidateRemoteUri` accepts HTTPS, or HTTP only on
  loopback. The branch has to be pushed. A loopback HTTP git server would pass validation, but the
  clone is a partial clone (`--filter=blob:none`), so a dumb static file server cannot serve it.
- **A running MCP server does not see a fresh install.** It resolves the active bundle at startup, so
  `get-guidance` in an already-connected session answers `guidance-unavailable` with an empty
  `availableGuides` no matter what `info-knowledge` reports. Verify against a server you start yourself
  (`clio mcp-server` over stdio), not against the session's.

**Why it is this way** — the built-in entry exists so a stock install serves curated guidance with a
pinned key and no operator setup, and identity uniqueness is what stops two sources claiming to publish
the same library. Neither was designed against local iteration; the `knowledge-allow-unsequenced` flag
is the part that was, and `BindingsModule` says so at its registration ("LOCAL DEV TOGGLE ... e.g.
clio-knowledge master").

**What breaks if you ignore it** — the failures are all quiet or misdirecting. `add-knowledge-source`
reports `alias 'x' or library 'com.creatio.clio' is already configured`, which reads as an alias
problem and sends you to rename the alias, forever. Disabling the built-in source looks like it should
free the identity and does not. And an agent that checks its work through the session's own
`get-guidance` concludes the article never installed, when the bundle is present and valid — that
answer is about server age, not about the bundle. Restore the entry afterwards and disable the flag:
it is persistent, it affects every later clio run, and while it is on the content-integrity check that
detects a swapped guidance corpus does not apply to Git sources.
