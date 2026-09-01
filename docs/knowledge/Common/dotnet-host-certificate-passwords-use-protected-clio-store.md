---
description: dotnet certificate passwords are restored from the hashed per-user Clio host-environment store after the initial deployment process exits
applies-to:
  - clio/Common/CreatioHostEnvironmentStore.cs
  - clio/Common/CreatioHostService.cs
  - clio/Common/DeploymentStrategies/DotNetDeploymentStrategy.cs
  - clio/Command/LinkCoreSrcCommand.cs
date: 2026-09-01
---

**What is true** — Dotnet deployment removes certificate passwords from `appsettings.json`, writes the
resolved Kestrel environment values to the owner-only host-environment store under `CLIO_HOME`, and
`clio start` loads them when no explicit environment map is supplied. The store key is a SHA-256 hash
of the normalized application directory, so the secret file is outside the deployed web root. The store
accepts only the generated `Kestrel__Endpoints__*__Certificate__Password` and
`Kestrel__Certificates__*__Password` names; Unix permissions and Windows ACLs are tightened on both
the directory and file, and the store refuses symbolic-link targets. The macOS terminal launcher
uses the same protected directory and applies the same symbolic-link/reparse-point refusal before
creating its temporary wrapper. When `link-core-src` changes a registered dotnet environment from
the deployed application directory to its core source directory, it migrates the protected map to
the new path before completing the registration.

**Why it is this way** — The first background process can receive a password through its environment,
but a later `clio start` is a new process and cannot recover that value from memory. Persisting the
password in application JSON would make it part of the deployed configuration and backups. The restored
map is passed to a new `dotnet` process, so arbitrary names would also allow a tampered store to inject
startup hooks, runtime roots, or path settings.

**What breaks if you ignore it** — A password-protected PFX works immediately after deployment but
fails on manual restart because Kestrel receives no password. Storing the sidecar in the web root can
also make a deployment-specific secret reachable through static-file handling or application backups.
If the store accepts arbitrary variables or inherits a broad ACL, an attacker who can replace it can
change how the next host process is launched or read the certificate password. A symbolic link would
otherwise redirect the write or read to a different file. If file hardening fails after a write, the
store removes that file instead of leaving an unprotected secret artifact behind. Without the
terminal-launcher path check, a planted directory link could redirect the temporary wrapper outside
the protected Clio state directory.
