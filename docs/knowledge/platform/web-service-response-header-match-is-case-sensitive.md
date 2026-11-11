---
description: Web service response header and cookie parameters, and URI placeholders, are matched by Path with case-sensitive comparison
applies-to:
  - clio/Command/WebServiceDesigner/WebServiceDefinitionBuilder.cs
ticket: ENG-105475
date: 2026-11-11
---

**What is true** — `ServiceClientResponse` reads a header or cookie response parameter by its `Path` with an ordinal,
case-sensitive lookup, and `ServiceClientRequest` fills `{placeholder}` in the method URI with a case-sensitive
`Replace` of `"{" + Path + "}"`. The header case was measured on a .NET Framework stand in November 2026; the
placeholder rule is read from the platform source (`Terrasoft.Services/Runtime/ServiceClientRequest.cs`).

**Why it is this way** — the platform compares the stored string as is; HTTP header names are case-insensitive by
the standard, but the platform does not apply that.

**What breaks if you ignore it** — a response parameter `X-Request-Id` reads nothing when the API sends
`x-request-id`, and a method URI `orders/{Id}` with a path parameter whose path is `id` is called with the literal
`{Id}` in the URL. Neither fails at save time. clio validates placeholders with ordinal comparison, and the guide
asks the agent for the exact header spelling.
