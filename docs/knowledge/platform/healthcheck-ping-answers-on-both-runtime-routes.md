---
description: /api/HealthCheck/Ping returns 200 on a .NET Framework Creatio site, so the health probe cannot tell the two runtimes apart
applies-to:
  - clio/Command/EnvironmentRuntimeDetectionService.cs
  - clio/Command/ClassicEnumVocabularyResolver.cs
  - clio.mcp.e2e/Support/Creatio/RuntimeDetectionStubServer.cs
date: 2026-09-09
---

**What is true** — on a .NET Framework Creatio site, `<base>/api/HealthCheck/Ping` (no `0/` segment) answers
`200 OK`, exactly like `<base>/0/api/HealthCheck/Ping`. The health endpoint therefore carries no information
about which runtime is behind the URL. The login pages do:

| URL | .NET Framework answer |
|---|---|
| `/api/HealthCheck/Ping` | `200` |
| `/0/api/HealthCheck/Ping` | `200` |
| `/Login/Login.html` | `404` |
| `/0/Login/NuiLogin.aspx` | `302` to `<base>/Login/NuiLogin.aspx?ReturnUrl=…`, then `200` |

Measured on three .NET Framework stands on 2026-09-09: `ts1-infr-web03:88/sae_m_seeenu_15999009_0909`,
`ts1-infr-web01:88/sae_m_seeenu_15998736_0909`, `a_kravchuk2:88/workenu_15970796_0919`. All three answered
identically.

**Why it is this way** — not investigated. The routing rule that makes the `0/` segment optional for the API
routes was not confirmed; only the responses above were measured. The mirror direction — what a .NET Core stand
answers on the `0/` routes — was NOT measured either: no .NET Core environment was reachable from the machine
this was written on. `RuntimeDetectionStubServer.cs` and the comment in `ClassicEnumVocabularyResolver.cs`
around the login-page split state only what their authors assumed, so neither is evidence. Measuring
`/0/Login/NuiLogin.aspx` and `/0/api/HealthCheck/Ping` on any .NET Core stand closes this, and is worth doing
before anyone leans harder on the health probe.

**What breaks if you ignore it** — rank a lone successful health probe above the login markers, and a cold
.NET Framework site registered without credentials comes out as .NET Core with no error at all: the `0/` health
route times out while the plain one answers. Every later command then builds `/rest/...` URLs without the `0/`
prefix and fails for reasons that look nothing like a registration problem. That is why health sits last in
`ResolveWithoutAuthentication`, after the markers, rather than first.
