---
description: commands that already degrade when a cliogate endpoint is missing (set-fsm-config and the file-design-mode family) are deliberately NOT decorated with [RequiresPackage] - adding the gate turns a working fallback into a hard refusal
applies-to:
  - clio/Command/SetFsmConfigCommand.cs
  - clio/Command/PackageCommand/DownloadPackageCommand.cs
  - clio/Command/FeatureCommand.cs
  - clio/Common/RequiredPackageChecker.cs
date: 2026-08-19
---

**What is true** — a sweep for cliogate callers finds commands that reach a cliogate route yet carry no
`[RequiresPackage]`. That is a decision, not an omission. `SetFsmConfigCommand` calls
`_fileDesignModePackages.SetFileDesignMode` and, when `remoteResult.EndpointAvailable` is false, logs
that the server's cliogate is older than this clio and falls through to actionable manual instructions.
The same holds for the rest of the file-design-mode family and for `get-info`, which query
`IClioGateway` imperatively instead.

**Why it is this way** — `[RequiresPackage]` is a hard-fail gate: it throws
`PackageRequirementException` before the command body runs. It is correct only where the remote call is
unavoidable. Where a fallback exists, the gate would delete it. When the requirement depends on a flag,
decorate the bool option that selects the remote path instead — `DownloadPackageCommand.Async`
(`pull-pkg --async`) and `FeatureOptions.UseFeatureWebService` are the two live examples; the flag-off
path uses core services and stays package-free.

**What breaks if you ignore it** — gating a gracefully-degrading command is a user-facing regression
that no test catches, because the fallback path has no cliogate-absent coverage: `set-fsm-config` starts
refusing to run on environments where it previously printed working manual steps. Before adding a gate,
confirm the default (flag-off) path issues no cliogate request, and confirm the command does not already
handle the endpoint being absent.
