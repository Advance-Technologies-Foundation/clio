---
description: on a Creatio web farm (K8s Control Plane) the gateway sends a request to the design node only when it carries StickySession, which Creatio issues on the first authenticated request after login - so every installation request, from the first upload chunk on, must go through one session that already holds the cookie
applies-to:
  - clio/Package/BasePackageInstaller.cs
  - clio/Package/DesignNodeSessionGuard.cs
  - clio/Package/IApplicationLogProvider.cs
  - clio/WebApplication/Application.cs
  - clio/Command/InstallGateCommand.cs
ticket: ENG-102757
date: 2026-10-12
---

**What is true** — the Control Plane gateway's only rule is `req.cook(StickySession) -m found` → design
node, everything else round-robin over the common nodes. Creatio appends `StickySession` (empty value)
when the server session STARTS - `SessionEventListener.OnSessionStart` on .NET, `Global.asax
Session_Start` on .NET Framework, both gated on `WebFarmEnabled && UseStickySessionCookie` and on
`CanManageSolution`/`CanManageMobileApplication` - so the login response never carries it.
`PackageInstallation` temp files and the installation log are per node. `creatio.client` sends no
request between login and the first real one on .NET (its post-login `0/ping` is .NET Framework only
and starts no session there either). Installer services are read-only session state, so concurrent log
polling in the same session is not serialized behind `InstallPackage`. `creatio.client` 2.1.0 recovers
an expired forms session inside the same `CookieContainer` and expires only `.ASPXAUTH`, `CRT_CSRF` and
`BPMCSRF`, so the re-login and every later request keep `StickySession`; bearer and OAuth
client-credentials requests use that container too.

**Why it is this way** — the routing is designed around a browser, which has made many requests before
it installs anything.

**What breaks if you ignore it** — a fresh client or login for any installation step, or any payload
sent before a session-starting request, puts that request on a common node: a multi-chunk archive is
split, the install fails with `InvalidGZipArchiveException`/`DirectoryNotFoundException`, the log is
read from the wrong node and the restart hits a common node. Single-node stands stay green; only
`WebFarmPackageInstallationTests` or a real Control Plane runtime show it.
