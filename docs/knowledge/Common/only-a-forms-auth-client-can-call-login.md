---
description: IApplicationClient.Login() and LoginAsync() are only usable on a forms-auth CreatioClient - the OAuth and bearer-passthrough constructors carry no username/password, so an explicit login throws UnauthorizedAccessException
applies-to:
  - clio/Common/IApplicationClient.cs
  - clio/Common/CreatioClientAdapter.cs
  - clio/Common/ApplicationClientFactory.cs
  - clio/Common/ServerReadinessWaiter.cs
ticket: ENG-94417
date: 2026-08-31
---

**What is true** — clio builds a `CreatioClient` in three shapes: forms auth
(`new CreatioClient(uri, login, password, ...)`), OAuth (`CreatioClient.CreateOAuth20Client(...)`)
and bearer passthrough (`new CreatioClient(uri, accessToken, isNetCore)`). Only the first holds a
username and a password. `CreatioClient.Login()` and `LoginAsync()` post those credentials plus
`TimeZoneOffset`, so on the other two shapes the credentials are null and the call throws
`UnauthorizedAccessException`. The synchronous `Login()` retains the library's own timeout behavior;
the asynchronous `IApplicationClient.LoginAsync()` overload accepts an explicit request timeout.

**Why it is this way** — the interface hides the difference: both login methods are available on
`IApplicationClient` and nothing in their signatures says they require the forms-auth client shape.
The fact is only visible in the `creatio.client` source.

**What breaks if you ignore it** — any "authenticate first to prove the server is up" step fails on
every OAuth and token-registered environment while the instance is perfectly healthy, and the caller
sees an auth error instead of a readiness answer. It is also unnecessary: `ExecutePostRequest`
establishes the session lazily and `ReauthExecutor` re-establishes a stale one, so a probe should
just issue the real request. `ServerReadinessWaiter` deliberately does not call `Login()` for exactly
this reason.
