---
description: PackageVersion.CompareSuffix ranks an EMPTY suffix BELOW a non-empty one, so 1.0.0.0 < 1.0.0.0-rc - the inverse of SemVer - and every > / >= / CompareTo on PackageVersion inherits it
applies-to:
  - clio/Package/PackageVersion.cs
ticket: ENG-94385
date: 2026-08-19
---

**What is true** — `PackageVersion.CompareSuffix` returns `-1` when *this* version's suffix is
empty and the other's is not, and `+1` in the mirror case. Since `CompareTo` falls through to it
whenever the four-part `Version` parts are equal, `1.0.1.0 > 1.0.1.0-rc` is **false** and
`1.0.1.0-rc > 1.0.1.0` is **true**. SemVer says the opposite: a pre-release ranks below its GA.
Nothing at the declaration site says so; only two consumers document it
(`clio/Command/InstallProcessBuilderCommand.cs`, `clio/Common/BundledPackageConvergence.cs`), and a
new consumer that just writes `>` will not see either.

**Why it is this way** — the suffix is free text, not a SemVer pre-release tag, and this operator
already backs the cliogate `[RequiresPackage]` gate. Correcting the ordering here would change what
that gate accepts on every environment, so the ordering was left alone and the affected consumers
compare narrowly on their own instead.

**What breaks if you ignore it** — a "is the environment behind?" or "would this downgrade?" check
built on `>` inverts on the exact pair where it matters. Installing GA over an `-rc` reads as a
downgrade and gets refused; installing an `-rc` over GA reads as an upgrade and is allowed. Both
failures are silent — the numbers match, so the log shows two identical-looking versions and a
verdict that cannot be explained from the message. If you need SemVer ordering, implement it in your
own comparison; do not change `CompareSuffix`.
