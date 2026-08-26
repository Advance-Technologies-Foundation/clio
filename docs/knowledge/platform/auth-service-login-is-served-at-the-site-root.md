---
description: ServiceModel/AuthService.svc/Login is served at the site root with NO 0/ WebAppAlias prefix on .NET Framework too, so it must never become a ServiceUrlBuilder KnownRoute
applies-to:
  - clio/Common/BrowserSession/CreatioAuthClient.cs
  - clio/Common/ServiceUrlBuilder.cs
ticket: ENG-91234
date: 2026-08-19
---

**What is true** — the forms-authentication endpoint lives at
`{Uri}/ServiceModel/AuthService.svc/Login` on **both** .NET Core and .NET Framework environments. It
is the exception to the `0/` WebAppAlias rule that governs every other route: live-confirmed against
a .NET Framework studio instance that `POST {Uri}/0/ServiceModel/AuthService.svc/Login` answers 401
while `POST {Uri}/ServiceModel/AuthService.svc/Login` answers `200 {"Code":0}` with `Set-Cookie`.
`CreatioAuthClient` therefore composes the login URL by hand instead of asking
`ServiceUrlBuilder`.

**Why it is this way** — the `0/` alias fronts the Shell and the data services; authentication is
mounted above it. `ServiceUrlBuilder.Build` unconditionally prepends `0/` when
`IsNetCore == false`, which is correct for every route it currently holds and wrong for this one.

**What breaks if you ignore it** — registering the login path as a `KnownRoute` (the natural
refactor, since every other clio endpoint is one) makes browser-session login fail on every .NET
Framework environment with a bare 401. The failure reads as bad credentials, so the investigation
goes to the environment's login and password instead of to the URL, and .NET Core stands keep working
the whole time.
