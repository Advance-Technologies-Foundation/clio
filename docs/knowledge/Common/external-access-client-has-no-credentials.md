---
description: an external-access client is built with NO login and password on purpose — creatio.client skips authentication when the cookie container holds .ASPXAUTH
applies-to:
  - clio/Common/ApplicationClientFactory.cs
  - clio/BindingsModule.cs
ticket: none
date: 2026-09-17
---

**What is true** — the client created for an external-access session is constructed as
`new CreatioClient(uri, userName: null, userPassword: null, useUntrustedSsl: false, isNetCore)` and
then receives the exchanged session through `ImportSessionCookies`. The empty credentials are the
mechanism, not an oversight: `CreatioAuthenticationHandler.EnsureAuthenticatedAsync` returns early
when `HasSession()` is true, and `HasSession()` without `ICredentials` means "the cookie container
holds `.ASPXAUTH`" — the authentication cookie name on both hosts
(`Terrasoft.Web.Common/AuthConsts.cs`). The same container also supplies the `BPMCSRF` header on
every later POST.

`NoReauthExecutor` is wired for the same reason: nothing in clio can mint a replacement token, so an
ended session must fail with that stated, not trigger a login attempt.

The session is cached per (environment, grant) in the browser-session store, and it is reused only
after a real request confirms the site still accepts it. The cookie's own expiry is deliberately not
used as the test: a grant can be revoked, or the server session dropped, while the cookie still looks
live — measured on a live site, the token lasts 120 seconds while the session it buys lasts the
site's session timeout, which is the whole reason the cache exists.

**Why it is this way** — `creatio.client` exposes no cookie-only constructor. The credential-less
overload plus `ImportSessionCookies` is the only public path to a pre-authenticated client.

**What breaks if you ignore it** — filling in a placeholder login and password to "make the
constructor look complete" re-arms the forms-login path: as soon as the session ends, the client
posts those placeholders to `AuthService.svc/Login`. In `BindingsModule.BuildCreatioClient` that
placeholder is literally `Supervisor`/`Supervisor` from the existing fallback, so the command would
keep running as a **different identity on a customer site** with nothing reported. The same applies
to `BuildRemoteDataProvider`, which has no cookie constructor at all and therefore throws instead of
falling through to that fallback.
