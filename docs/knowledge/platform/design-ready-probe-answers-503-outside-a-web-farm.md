---
description: the anonymous /api/Probes/DesignReady answers 200 {"isDesignNode":true} only on a design node and 503 on every other node type (Common, None) - so it is consulted only after the session received StickySession, and only its own confirmation counts
applies-to:
  - clio/Package/DesignNodeSessionGuard.cs
ticket: ENG-102757
date: 2026-10-09
---

**What is true** — `ProbesController.DesignReady` (`[AllowAnonymous]`, `api/Probes` and `0/api/Probes` on
.NET) returns 200 with `isDesignNode: true` only when `WebFarmSettingsProvider.GetCurrentNodeType()` is
`Design`; `Common` and `None` (a single node) both answer 503 with `isDesignNode: false`. Through the
Control Plane gateway, HAProxy also answers 503 itself while the design backend is down. Whether the
.NET Framework controller answers under the `0/` prefix `ServiceUrlBuilder` adds there was not measured.

**Why it is this way** — the probe serves the Control Plane readiness checks, which only ask nodes of a
design/common topology. `StickySession` is issued only with `UseStickySessionCookie` explicitly on
(off by default), which is that same topology.

**What breaks if you ignore it** — consulting the probe without `StickySession` refuses every
single-node installation, and trusting any 200 lets a gateway catch-all page pass as the design node.
`DesignNodeSessionGuard` asks only after the cookie was issued, accepts only a 200 whose body says
`isDesignNode: true`, refuses everything else before upload (only the design node may change), and
lets only a missing probe (404) through with a warning.
