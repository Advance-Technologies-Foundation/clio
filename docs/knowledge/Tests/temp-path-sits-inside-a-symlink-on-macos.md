---
description: a test that feeds Path.GetTempPath() to a reparse-point guard fails on macOS because /var is a symlink, and the obvious fix throws on a Windows drive root
applies-to:
  - clio.tests/Command/IdentityServiceDeployment/IdentityServiceDeploymentServiceTests.cs
  - clio/Command/IdentityServiceDeployment/IdentityServiceDeploymentService.cs
ticket: GH-1417
date: 2026-09-09
---

**What is true** — on macOS `Path.GetTempPath()` returns a path under `/var/folders/…`, and `/var`
is a symlink to `/private/var`. Every path a test builds under the temp directory therefore has a
reparse point among its ancestors. Any production code that refuses such a path — here
`IdentityServiceDeploymentService.EnsurePathHasNoReparsePoints`, which walks every ancestor of the
IdentityService target — rejects the test's path, correctly. The fixture has to resolve the temp
root to its physical path first, the equivalent of `pwd -P`.

**Why it is this way** — the guard exists so a deployment cannot redirect binaries and database
credentials outside the selected target, and it must stay strict. `DirectoryInfo.ResolveLinkTarget`
is the resolution primitive, but a single call is not enough: the link is `/var`, several levels
above the temp directory, not the temp directory itself. The walk has to find the deepest linked
ancestor and re-anchor the remainder of the path on its target, repeating until no ancestor is a
link.

**What breaks if you ignore it** — two failures, each visible on one platform only, so a local run
on the other platform reports success:

- Unresolved: five `ExtractIdentityService_*` tests fail on macOS on every branch, including a clean
  `master`, with `InvalidOperationException: IdentityService target '/var/folders/…' cannot be
  inside or be a filesystem reparse point`. The message points at the guard, so the cause reads as a
  production defect rather than a fixture one.
- Resolved carelessly: the ancestor walk reaches the filesystem root, and on Windows
  `new DirectoryInfo("C:\\").ResolveLinkTarget(returnFinalTarget: true)` raises
  `DirectoryNotFoundException` ("Could not find a part of the path 'C:\'") instead of returning
  `null`. When the walk runs from a static field initializer, that takes down the whole fixture with
  `TypeInitializationException` — every test in it fails, including tests that never touch a temp
  path. Treat any path that cannot be inspected as a link as an ordinary directory.
