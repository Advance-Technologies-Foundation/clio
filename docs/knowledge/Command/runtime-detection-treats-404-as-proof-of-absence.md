---
description: runtime auto-detection must separate an HTTP 404 (marker absent) from a transport failure (no information) or a cold Creatio site defeats it
applies-to:
  - clio/Command/EnvironmentRuntimeDetectionService.cs
date: 2026-09-09
---

**What is true** — `EnvironmentRuntimeDetectionService` decides between .NET Core and .NET Framework from two
login-page markers, and the two ways a marker probe can fail are not equivalent:

- `404 Not Found` / `410 Gone` — the site answered and the page does not exist. This *proves* that runtime is
  not the one behind the URL.
- a timeout, a reset connection, a DNS failure — nothing was learned.

So when exactly one marker answers 404 and the other never answered, the runtime is the other one.
`ProbeAttempt` carries `StatusCode` for this reason; do not collapse it back into the message string.

Only 404 and 410 count as absence. `401` and `403` mean the page exists and is gated, and a proxy that returns
404 for everything leaves both markers absent, which correctly resolves to nothing and throws.

**Why it is this way** — a cold Creatio site (first request after an application-pool start) drops connections
and blows past the 10 s probe timeout on the routes that have to warm up. Measured on
`ts1-infr-web03:88/sae_m_seeenu_15999009_0909`: `/0/api/HealthCheck/Ping` did not answer within 20 s on the
first request and returned 200 in 60 ms a minute later.

**What breaks if you ignore it** — the original code counted only successes, so a run against a cold
.NET Framework site saw `Login/Login.html => 404` (conclusive) and `0/Login/NuiLogin.aspx => An existing
connection was forcibly closed` (inconclusive), counted "zero successes", and aborted `reg-web-app` with
"Unable to auto-detect the Creatio runtime", forcing the user to pass `--IsNetCore false` by hand — even
though the 404 alone already answered the question.
