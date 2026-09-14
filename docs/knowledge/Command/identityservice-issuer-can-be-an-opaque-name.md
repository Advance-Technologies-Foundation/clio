---
description: Creatio IdentityService can publish creatio.com as its issuer rather than an absolute URL
applies-to:
  - clio/Command/OAuthAppConfiguration/IdentityServerProbe.cs
ticket: clio-1432
date: 2026-09-10
---

**What is true** — The IdentityService bundled with Creatio 10.1.585 publishes `issuer: "creatio.com"` with absolute discovery endpoint URLs. This was observed on the isolated issue-1432 local deployment.

**Why it is this way** — The issuer is a configured token identifier, independent of the host address used to reach IdentityService. Clio checks that it is present; CRM remains responsible for accepting the issued token.

**What breaks if you ignore it** — Requiring the issuer to be an absolute HTTP URL rejects this real deployment before token verification. Do not equate the issuer identifier with the discovery host or implement a second token-validation policy in Clio.
