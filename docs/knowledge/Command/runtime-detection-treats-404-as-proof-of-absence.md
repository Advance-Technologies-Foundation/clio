---
description: runtime auto-detection reads a login-marker 404 as proof of absence, which only holds because the probe client does not follow redirects and the other marker gave no answer at all
applies-to:
  - clio/Command/EnvironmentRuntimeDetectionService.cs
  - clio/BindingsModule.cs
date: 2026-09-09
---

**What is true** — three constraints hold this rule together, and none of them is visible from the call site.

1. `ProbeAttempt` carries `HttpStatusCode?` so the decision can tell an answer apart from silence. Folding the
   status back into the message string (the shape before this record existed) is the rejected alternative:
   the resolver then sees only "failed" and cannot use the strongest evidence it already holds.
2. The `404`/`410` rule fires **only when the other marker produced no HTTP response at all**. Any other status
   on the other side — `401`, `403`, `500`, a gateway error — leaves the pair undecided on purpose. A wrong base
   URL produces `404` on one path and a WAF `403` on the other just as readily as a real Creatio site does, so
   the narrow form is what keeps the rule from naming a runtime for a host that is not Creatio at all.
3. The probe client (`EnvironmentRuntimeDetectionService.HttpClientName`, registered in `BindingsModule`) has
   `AllowAutoRedirect = false`, and a `3xx` counts as the route being served. This is load-bearing. A .NET
   Framework site answers `/0/Login/NuiLogin.aspx` with a `302` to the same page off the site root; with
   redirects followed, the recorded status belongs to the redirect target rather than to the probed URL.

The `404` also does not mean quite what its name suggests on the framework side: it says this deployment does
not serve the `/0` login form, which is close to but not identical with "not .NET Framework".

**Why it is this way** — a cold Creatio site (first request after an application-pool start) drops connections
and outlasts the probe timeout on the routes that still have to warm up. Measured on
`ts1-infr-web03:88/sae_m_seeenu_15999009_0909`: `/0/api/HealthCheck/Ping` did not answer within 20 s on the
first request and returned 200 in 60 ms a minute later. So on a cold site the evidence is genuinely one-sided,
and a resolver that only counts successes throws away the half it did receive.

**What breaks if you ignore it** — score every failed marker probe the same and a cold .NET Framework site
aborts registration with "Unable to auto-detect the Creatio runtime", because the conclusive `404` on
`Login.html` is filed next to a reset connection on `NuiLogin.aspx`. Widen the rule past "the other side never
answered" and a mistyped base URL registers as a runtime instead of failing. Turn redirect-following back on
and a `302` whose target happens to answer `404` convicts the wrong runtime silently — `BuildProbeSummary`
would print that `404` beside a marker URL that never returns one, so nobody can reproduce it by hand.

`clio.mcp.e2e/RegWebAppToolE2ETests.cs` exercises all three constraints against a stub that can drop a socket
and redirect; `clio.mcp.e2e/Support/Creatio/RuntimeDetectionStubServer.cs` carries the marker modes.
