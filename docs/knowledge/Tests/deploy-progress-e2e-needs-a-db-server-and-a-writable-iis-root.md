---
description: DeployUninstallProgressTests captures zero notifications/progress stage events unless the request carries dbServerName and the fixture owns a writable iis-clio-root-path - deploy-creatio returns before StageEventEmitter.Begin on a clean CI agent
applies-to:
  - clio.mcp.e2e/DeployUninstallProgressTests.cs
  - clio/Command/CreatioInstallCommand/InstallerCommand.cs
  - clio/Command/CreatioInstallCommand/CreatioInstallerService.cs
date: 2026-08-19
---

**What is true** — the typed stage-event progress E2E has two environmental preconditions that have
nothing to do with progress plumbing. `InstallerCommand.Execute` returns `1` when the resolved
Kubernetes client is `FakeKubernetes` and `DbServerName` is empty, before
`CreatioInstallerService.Execute` runs at all; and inside `CreatioInstallerService.Execute` an IIS
deployment creates `iis-clio-root-path` (line ~1343) well before `_stageEventEmitter.Begin` (line
~1457). The fixture therefore passes an intentionally unused `dbServerName` and points a
fixture-scoped `CLIO_HOME` at a temporary `iis-clio-root-path`.

**Why it is this way** — a clean CI agent has no kubectl config and no persisted clio defaults, and
its identity cannot write the default `C:\inetpub\wwwroot\clio`. Both failures land upstream of the
manifest, so no `notifications/progress` event has been raised yet when the call ends. The corrupt
archive still fails at unzip, so nothing is deployed either way.

**What breaks if you ignore it** — the test fails as "zero progress events captured" or as a
wait timeout, which reads as broken progress forwarding or a too-short wait. Waiting longer can never
help: there is nothing left to emit. Developer machines with persisted defaults and a writable IIS
root pass, so the failure looks CI-only and gets misfiled as flakiness.
