---
description: SharedIisApplicationPoolException from IisApplicationPoolResolver is a correct Assert.Ignore, not a test gap - production uninstall skips delete-apppool-profile whenever the pool survives, so do not relax the fail-closed resolver to get CI coverage
applies-to:
  - clio.mcp.e2e/Support/Configuration/IisApplicationPoolResolver.cs
  - clio.mcp.e2e/UninstallCreatioWarningE2ETests.cs
  - clio/Common/CreatioUninstaller.cs
date: 2026-08-19
---

**What is true** — when the resolved sandbox application pool carries more than one IIS application
assignment, `IisApplicationPoolResolver.Resolve` throws `SharedIisApplicationPoolException` and
`UninstallCreatioWarningE2ETests` reports itself ignored. That is the correct outcome, not missing
coverage: in production `CreatioUninstaller` only attempts profile cleanup after
`_iisScanner.TryDeleteAppPoolIfUnused` actually removed the pool, and otherwise records
`AppPoolProfileCleanupStatus.NotApplicable` and skips the `delete-apppool-profile` stage.

**Why it is this way** — a pool shared with another application must be preserved, so its
virtual-account profile is never deleted. The locked-profile warning contract only exists on the
path where the pool is deleted, which requires an exclusive disposable pool. The resolver is
deliberately fail-closed because the alternative is a destructive uninstall against a pool something
else is using.

**What breaks if you ignore it** — the predictable wrong move is to loosen the assignment check so
the suite stops reporting an ignore on a shared TeamCity pool. The test then exercises the
`NotApplicable` branch while asserting the warning branch, so it either fails for the wrong reason or
passes vacuously, and the relaxed resolver has removed the only guard that keeps a destructive
uninstall off a pool with live foreign applications.
