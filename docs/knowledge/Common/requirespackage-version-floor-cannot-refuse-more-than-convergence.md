---
description: a version literal on [RequiresPackage] for a bundled package can never refuse an environment that BundledPackageConvergence does not already refuse - the floor is capped at the bundled version, so its refusal set is a subset
applies-to:
  - clio/Common/RequiresPackageAttribute.cs
  - clio/Common/BundledPackageConvergence.cs
  - clio.tests/Common/BundledProcessBuilderPackageTests.cs
ticket: ENG-92706
date: 2026-08-19
---

**What is true** — for a package clio itself bundles, adding a version floor F to a
`[RequiresPackage]` declaration buys no refusal that is not already available.
`BundledProcessBuilderPackageTests.BundledArchive_ShouldCarryAtLeastEveryDeclaredRequirement`
constrains every declared literal to F <= B, where B is the version read out of the shipped archive
through the catalog. The floor refuses exactly when installed < F; convergence refuses when
installed < B (`BundledPackageConvergence.cs:110`). F <= B therefore makes the floor's refusal set a
subset of convergence's. A floor buys better diagnostics on stands nobody reinstalls on, never
delivery.

**Why it is this way** — clio must never demand of an environment a version its own distribution
cannot supply, otherwise the gate refuses and then hands the caller an installer that cannot satisfy
the refusal. That invariant is what caps F at B, and it is the reason the two rules cannot be made
independent.

**What breaks if you ignore it** — a reviewer will periodically ask for a version floor to fix a
missing refusal, and both the accept and the reject can be argued wrongly. Do not reject it by
citing `spec/adr/adr-bundled-package-version-source-of-truth.md` (that ADR rejects specific
*derivations* of a floor, and explicitly permits a hand-typed literal added in the commit that
creates the need) nor by calling the presence-only test untouchable (a test whose stated premise has
changed is updatable). Reject it, if at all, on the subset arithmetic above - and do not accept it
believing it closes a delivery gap, because it cannot.
