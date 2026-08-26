---
description: RegisterActiveEnvironmentServices builds CreatioClient/RemoteDataProvider inline and bypasses ApplicationClientFactory, so a new authentication mode must be added in both places or it silently falls back to Supervisor
applies-to:
  - clio/BindingsModule.cs
  - clio/Common/ApplicationClientFactory.cs
ticket: ENG-93208
date: 2026-08-19
---

**What is true** — clio builds Creatio connections in **two** independent places.
`ApplicationClientFactory.CreateClient` serves explicit foreign environments passed at call time
(cross-environment `ApplicationManager`, package install). `BindingsModule.RegisterActiveEnvironmentServices`
plus the `SysSettingsManager` data-provider factory just above it construct `RemoteDataProvider`,
`CreatioClient` and the `CreatioClientAdapter` **inline** for the container's active environment, and
that is the path the per-tenant MCP child container takes. Neither file says the other exists.

**Why it is this way** — the child container injects its connections directly rather than going
through a factory, and the two code paths grew separately. The inline sites end with a
`Login ?? "Supervisor"` / `Password ?? "Supervisor"` bootstrap fallback for the no-credential case.

**What breaks if you ignore it** — adding an authentication mode to the factory alone leaves the
inline sites reading only `Login`/`ClientId`, so credentials they do not understand fall through to
the Supervisor default. That is how ENG-93208 B1 happened: a bearer-token request authenticated as
Supervisor and never used `NoReauthExecutor` — a silent cross-identity privilege escalation with no
error anywhere. Branch on the new credential kind **first** at every inline site and fail closed;
never let a new credential shape reach the Supervisor fallback.
