---
description: /api/HealthCheck/Ping returns 200 on a .NET Framework Creatio site, so the health probe cannot tell the two runtimes apart
applies-to:
  - clio/Command/EnvironmentRuntimeDetectionService.cs
  - clio.tests/Command/EnvironmentRuntimeDetectionServiceTests.cs
date: 2026-09-09
---

**What is true** — on a .NET Framework Creatio site, `<base>/api/HealthCheck/Ping` (no `0/` segment) answers
`200 OK`, exactly like `<base>/0/api/HealthCheck/Ping`. Verified on
`http://ts1-infr-web03:88/sae_m_seeenu_15999009_0909` (Creatio on .NET Framework) on 2026-09-09: both routes
returned 200. The health endpoint therefore carries no information about which runtime is behind the URL.

The login-page markers do discriminate on that same site:

| URL | Answer |
|---|---|
| `<base>/Login/Login.html` | `404 Not Found` |
| `<base>/0/Login/NuiLogin.aspx` | `302` to `<base>/Login/NuiLogin.aspx?ReturnUrl=…`, then `200` |

Measured on three .NET Framework stands on 2026-09-09 — `ts1-infr-web03:88/sae_m_seeenu_15999009_0909`,
`ts1-infr-web01:88/sae_m_seeenu_15998736_0909` and `a_kravchuk2:88/workenu_15970796_0919`. All three answered
200 on both health routes, 404 on `/Login/Login.html` and 200 on `/0/Login/NuiLogin.aspx`.

**Why it is this way** — not investigated. The routing rule that makes the `0/` segment optional for the API
routes was not confirmed; only the responses above were measured. The mirror direction (what a .NET Core stand
answers on the `0/` routes) was NOT measured either — no .NET Core environment was reachable from this
machine; `clio.mcp.e2e/Support/Creatio/RuntimeDetectionStubServer.cs` only states what the authors assumed.

**What breaks if you ignore it** — `EnvironmentRuntimeDetectionService.ResolveWithoutAuthentication` used to
return `netCoreProbe.HealthProbe.Succeeded` whenever exactly one health probe succeeded. On a cold .NET
Framework site the `0/` health probe times out while the plain one answers, so `reg-web-app <name> -u <url>`
without credentials registered the environment as .NET Core with no error at all — every later command then
built `/rest/...` URLs without the `0/` prefix and failed for reasons that look unrelated to registration.
Health is now only a last-resort tiebreaker, after the login markers.
