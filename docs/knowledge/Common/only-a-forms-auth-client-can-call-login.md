---
description: IApplicationClient.Login() is only usable on a forms-auth CreatioClient - the OAuth (CreateOAuth20Client) and bearer-passthrough constructors carry no username/password, so an explicit Login() throws UnauthorizedAccessException
applies-to:
  - clio/Common/IApplicationClient.cs
  - clio/Common/CreatioClientAdapter.cs
  - clio/Common/ApplicationClientFactory.cs
  - clio/Common/ServerReadinessWaiter.cs
ticket: ENG-94417
date: 2026-08-19
---

**What is true** — clio builds a `CreatioClient` in three shapes: forms auth
(`new CreatioClient(uri, login, password, ...)`), OAuth (`CreatioClient.CreateOAuth20Client(...)`)
and bearer passthrough (`new CreatioClient(uri, accessToken, isNetCore)`). Only the first holds a
username and a password. `CreatioClient.Login()` posts those two fields, so on the other two shapes
they are null and the call throws `UnauthorizedAccessException`. `Login()` also sets no request
timeout, inheriting the library's ~100 s connect default plus a separate response-read timeout, and
`IApplicationClient` exposes no overload that bounds it.

**Why it is this way** — the interface hides the difference: `Login()` is one method on
`IApplicationClient` and nothing in its signature says it is credential-shaped. The fact is only
visible by decompiling `creatio.client`.

**What breaks if you ignore it** — any "authenticate first to prove the server is up" step fails on
every OAuth and token-registered environment while the instance is perfectly healthy, and the caller
sees an auth error instead of a readiness answer. It is also unnecessary: `ExecutePostRequest`
establishes the session lazily and `ReauthExecutor` re-establishes a stale one, so a probe should
just issue the real request. `ServerReadinessWaiter` deliberately does not call `Login()` for exactly
this reason.
