---
description: dotnet certificate passwords are restored from the hashed per-user Clio host-environment store after the initial deployment process exits
applies-to:
  - clio/Common/CreatioHostEnvironmentStore.cs
  - clio/Common/CreatioHostService.cs
  - clio/Common/DeploymentStrategies/DotNetDeploymentStrategy.cs
date: 2026-08-31
---

**What is true** — Dotnet deployment removes certificate passwords from `appsettings.json`, writes the
resolved Kestrel environment values to the owner-only host-environment store under `CLIO_HOME`, and
`clio start` loads them when no explicit environment map is supplied. The store key is a SHA-256 hash
of the normalized application directory, so the secret file is outside the deployed web root.

**Why it is this way** — The first background process can receive a password through its environment,
but a later `clio start` is a new process and cannot recover that value from memory. Persisting the
password in application JSON would make it part of the deployed configuration and backups.

**What breaks if you ignore it** — A password-protected PFX works immediately after deployment but
fails on manual restart because Kestrel receives no password. Storing the sidecar in the web root can
also make a deployment-specific secret reachable through static-file handling or application backups.
