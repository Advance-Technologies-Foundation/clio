---
description: on IsNetCore environments ping-app goes through CreatioClient.ExecuteGetRequest, which forces a real CreatioClient.Login through the lazy AuthCookie - so ping-app validates credentials, not just liveness, and shares the login codepath with UploadFile
applies-to:
  - clio/Command/PingCommand.cs
  - clio.mcp.e2e/Support/Configuration/ClioCliCommandRunner.cs
  - Directory.Packages.props
ticket: ENG-90640
date: 2026-08-19
---

**What is true** — `PingAppCommand.Execute` branches on `EnvironmentSettings.IsNetCore`: a .NET Core
stand is probed with `ApplicationClient.ExecuteGetRequest`, a .NET Framework stand falls through to
`RemoteCommand.Execute`. Inside the `creatio.client` package (pinned at `1.0.40` in
`Directory.Packages.props`), `ExecuteGetRequest` resolves the lazy `AuthCookie`, which calls
`InitAuthCookie` and therefore `CreatioClient.Login` — the same login path `UploadFile` uses. So on
a .NET Core environment a successful `ping-app` proves the configured credentials work, and a
failing one distinguishes an authentication rejection from a connect or warm-up failure. This is
what makes it usable as a readiness gate before an upload-based operation (see
`ClioCliCommandRunner.WaitForLoginReadinessAsync`).

**Why it is this way** — the login behaviour lives in an external nuget, not in this repository; it
was re-verified from the `creatio.client` 1.0.40 source. Nothing in `PingCommand.cs` states it, and
the `/ping` endpoint name suggests an unauthenticated liveness check.

**What breaks if you ignore it** — two symmetric mistakes. Treating `ping-app` as a plain liveness
probe makes you read a 401 as "site is down" and hunt for a deployment problem. Conversely, reusing
it as a credential check on a **.NET Framework** stand is invalid: that branch does not go through
`ExecuteGetRequest`, so the conclusion does not transfer. Bumping `creatio.client` invalidates the
decompiled half of this record — re-check it rather than assuming.
