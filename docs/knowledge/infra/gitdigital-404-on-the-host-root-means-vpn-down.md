---
description: gitdigital.creatio.com answering HTTP 404 on the host root means the VPN is down, not that credentials are missing - GitLab also answers 404 for a private repository, so a failing git push cannot tell the two apart
applies-to:
  - clio/Command/McpServer/AGENTS.md
  - clio/Command/McpServer/Tools/ComponentRegistryClient.cs
ticket: ENG-93878
date: 2026-08-19
---

**What is true** — the component/request registries and their recipe markdown are authored in the
GitLab repository `gitdigital.creatio.com/academy/static-files-mcp`, which the academy CDN mirrors on
a five-minute cadence. That host is reachable only over the corporate VPN. The two-second
discriminator is `curl -I https://gitdigital.creatio.com/` on the **host root**: a reachable GitLab
answers a redirect to `/users/sign_in`, while `404` on the root means the request never reached
GitLab at all.

**Why it is this way** — GitLab deliberately answers `404` rather than `401`/`403` for a private
repository so that repository existence does not leak. A blocked request and an unauthorised one
therefore produce the same status on a repository path.

**What breaks if you ignore it** — a `git push`, `git fetch` or `curl` against a repository path
fails with the same `404` in both cases, so the failure reads as a credential problem and sends you
into credential-helper and token configuration that was never broken. Check the host root first.
