---
description: PackageInstallerService.svc/InstallPackage answers success:false with the generic "Packages installation failed" when the only problem in the run was a schema skipped because it was modified locally
applies-to:
  - clio/Package/BasePackageInstaller.cs
  - clio/Package/InstallLogAnalyzer.cs
ticket: GH-1299
date: 2026-09-05
---

**What is true** — `/0/ServiceModel/PackageInstallerService.svc/InstallPackage` returns
`{"errorInfo":{"errorCode":"Exception","message":"Packages installation failed",...},"success":false}`
for a run that installed the package and whose only anomaly was a schema the platform refused to
overwrite. The same run writes `Unable to install Schema "X" into package "Y", because the element has
been modified locally.` and then `Package installation finished` to the installation log. Verified on
Creatio 10.1.725 (.NET Framework) by pulling a package whose schema had been edited on the
environment and pushing the pulled archive back.

**Why it is this way** — the service collects everything reported during the run into one bucket and
answers `success:false` when the bucket is not empty. It does not separate "this element was left
alone on purpose" from "the installation failed", and it does not put the schema name into
`errorInfo`: the generic message is all the caller gets. The only evidence that the run finished is
the log, which is fetched over a separate channel. `Package installation finished` is a usable
discriminator — a genuinely failed install never reaches it (checked by pushing a corrupt archive) —
but "no error text anywhere in the log" is NOT: the installation log is shared, and a healthy run
carries unrelated failures from other packages (`Error while saving the metadata of application …`
was present in the very run that installed successfully). Anything that refuses the classification
when the log mentions an error therefore refuses it almost always.

**What breaks if you ignore it** — trusting `success` alone turns a completed installation into a
non-zero exit code, which is what broke scripting around `push-pkg` (the command printed a bare
`[ERR] - Error`). The classification that fixes it is a literal English-string match on the platform
log — `Package installation finished`, `has been modified locally`, `Packages installation failed` —
so a platform build that rephrases or localizes any of those three silently restores the old
behaviour, with no test failure and no error anywhere: `push-pkg` simply starts exiting 1 again after
a successful install. If a report of that reappears, re-read the raw log before touching clio.
