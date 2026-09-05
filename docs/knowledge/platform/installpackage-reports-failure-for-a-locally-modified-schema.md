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
the log, which is fetched over a separate channel.

`Package installation finished` proves the run REACHED THE END — it does not prove nothing failed,
and it is not sufficient on its own. Measured on the same stand: an archive carrying both a package
whose schema does not compile (`error CS1519`) and a package with a locally modified schema produces
the skip line, the compiler error, the completion marker AND the same generic message, all in one
run. `push-pkg` installs with `ContinueIfError = true` by default, which is exactly the mode that
produces it. The earlier "a genuinely failed install never reaches the marker" reading came from
pushing a corrupt archive, and that data point is unrepresentative: that failure happens before the
installation starts.

"No error text anywhere in the log" is NOT a usable stricter rule either: the installation log is
shared, and a healthy run carries unrelated failures from other packages (`Error while saving the
metadata of application …` was present in the very run that installed successfully), plus the line
`Errors and (or) warnings occurred while compiling configuration dll`, which the platform prints for
warning-only builds too. Anything that refuses the classification when the log mentions an error
therefore refuses it almost always. What does work is a C# compiler diagnostic of severity `error`
(`error CS####`): emitted only for a build that failed, absent from every healthy run measured,
present in the run that broke the configuration.

The log window itself is not trustworthy either. `GetLogFile` sometimes answers with an HTML
`500 - Internal Server Error` page instead of the log, and that body arrives as log content rather
than as a failure; `ApplicationLogProvider.GetInstallationLog` additionally converts every request
failure into an empty string. Since the "current run's log" is a length subtraction of the
pre-install read from the post-install read, either shape silently turns the environment's whole
shared history into "this run's log".

**What breaks if you ignore it** — trusting `success` alone turns a completed installation into a
non-zero exit code, which is what broke scripting around `push-pkg` (the command printed a bare
`[ERR] - Error`). Classifying on the completion marker ALONE breaks it the other way round, and that
direction is worse: the mixed run above was reported as `Done` with exit `0` while the environment's
configuration no longer compiled — a deployment gate answering "green" on a broken stand, silent to
anything reading only the exit code.

The classification is a literal English-string match on the platform log — `Package installation
finished`, `has been modified locally`, `Packages installation failed`, `error CS` — so a platform
build that rephrases or localizes any of them silently changes the outcome, with no test failure and
no error anywhere: `push-pkg` starts exiting 1 again after a successful install, or stops noticing a
failed compilation. If a report of either reappears, re-read the raw log before touching clio.
