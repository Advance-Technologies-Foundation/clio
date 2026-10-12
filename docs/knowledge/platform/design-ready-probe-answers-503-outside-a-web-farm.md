---
description: the anonymous /api/Probes/DesignReady answers 200 {"isDesignNode":true} only on a design node and 503 on every other node type (Common, None) with its JSON body, while HAProxy answers 503 with its own page when the design pod is down or restarting - so it is consulted only after the session received StickySession, only the gateway's 503 is waited out for a bounded time, and only the probe's own confirmation counts
applies-to:
  - clio/Package/DesignNodeSessionGuard.cs
ticket: ENG-102757
date: 2026-10-12
---

**What is true** — `ProbesController.DesignReady` (`[AllowAnonymous]`, `api/Probes` and `0/api/Probes` on
.NET) returns 200 with `isDesignNode: true` only when `WebFarmSettingsProvider.GetCurrentNodeType()` is
`Design`; `Common` and `None` (a single node) both answer 503 with `isDesignNode: false`. Through the
Control Plane gateway, HAProxy also answers 503 itself - with its own HTML page, not the probe's JSON -
while the design backend is down. After `RestartApp` (measured on a local Control Plane runtime, Creatio
10.0.0.942) the design pod keeps answering for 15-20 seconds, then the gateway has no design server for one
to two minutes. Whether the .NET Framework controller answers under the `0/` prefix `ServiceUrlBuilder`
adds there was not measured.

**Why it is this way** — the probe serves the Control Plane readiness checks, which only ask nodes of a
design/common topology. `StickySession` is issued only with `UseStickySessionCookie` explicitly on
(off by default), which is that same topology.

**What breaks if you ignore it** — consulting the probe without `StickySession` refuses every
single-node installation, and trusting any 200 lets a gateway catch-all page pass as the design node.
Returning from an installation as soon as `RestartApp` answers lets the next installation of a pipeline
(`update-gate` followed by `push-pkg`, several `--id` values, an environment manifest) pass the probe on the
still-answering design pod and lose its `InstallPackage` to the gateway's 503 page (seen on the stand).
Waiting on every 503 would hold a misrouted session for the whole budget, because a common node answers
503 too; letting a 404 through would install with the routing unchecked whenever a gateway does not forward
`/api/*`. `DesignNodeSessionGuard` asks only after the cookie was issued, accepts only a 200 whose body says
`isDesignNode: true`, waits up to five minutes only on 502/503/504 without the probe's body (or a probe with
no answer once waiting), refuses everything else before upload, a 404 included, and after a restart waits
until the design pod has gone down and confirms again. If the .NET Framework controller is not served under
`0/`, a sticky .NET Framework farm is therefore refused rather than installed unchecked.
