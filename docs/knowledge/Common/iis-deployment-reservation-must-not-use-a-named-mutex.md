---
description: IisDeploymentPortReservation uses an exclusive machine-level file lease instead of a named Mutex because async deployment can dispose the lease on a different thread
applies-to:
  - clio/Common/IIS/IisDeploymentPortReservation.cs
  - clio/Command/CreatioInstallCommand/CreatioInstallerService.cs
date: 2026-08-21
---

**What is true** — the per-port deployment reservation is an exclusively opened file under the
machine-wide ProgramData directory. The stream stays open from preflight until the IIS deployment
finishes and the operating system releases it if the clio process exits.

**Why it is this way** — a named `Mutex` is thread-affine on Windows. An async deployment can acquire
it on one thread and resume its `finally` block on another, where `ReleaseMutex` throws
`ApplicationException`. A file lease has process-wide ownership semantics and can be disposed from
any continuation thread while still allowing unrelated ports to deploy concurrently.

**What breaks if you ignore it** — replacing the file lease with a named mutex makes successful
deployments fail during cleanup or leaves the port reservation held until process exit, depending on
which continuation thread runs. A process-local lock instead permits two clio processes to mutate
the same target concurrently.
