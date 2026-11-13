---
description: A web service with Digest authentication sends a preemptive Authorization Basic header; a Digest-only API refuses every call
applies-to:
  - clio/Command/WebServiceDesigner/WebServiceDefinitionBuilder.cs
ticket: ENG-105475
date: 2026-11-14
---

**What is true** — `DigestAuthData` derives from `BasicAuthData` (Terrasoft.Services), so
`HttpWebExtensions.AppendAuthData` writes `Authorization: Basic <user:password>` for Digest, before any challenge.
Measured: REST on 2026-11-11 (a Digest endpoint answered 401, the request carried Basic), SOAP 1.2 on 2026-11-14 on
stand `Creatio` (the request carried Basic).

**Why it is this way** — Digest was modelled as a variant of Basic and never got its own challenge and response.

**What breaks if you ignore it** — a Digest-only API refuses every call, which no service setting fixes; and Digest
gives no protection over Basic: on an http base URI the password travels readable. Tell the user before registering
Digest; such an API needs a platform change.
