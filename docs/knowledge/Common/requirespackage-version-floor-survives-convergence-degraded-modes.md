---
description: a [RequiresPackage] version floor for a bundled package is a subset of convergence ONLY while convergence is healthy - convergence warn-and-allows on an unreadable archive or a suffixed bundled version, and in those states the floor is the only fail-closed refusal
applies-to:
  - clio/Common/RequiresPackageAttribute.cs
  - clio/Common/BundledPackageConvergence.cs
  - clio/Command/ModifyBusinessProcessCommand.cs
  - clio/Command/CreateBusinessProcessCommand.cs
  - clio.tests/Common/BundledProcessBuilderPackageTests.cs
ticket: ENG-91846
date: 2026-08-28
---

**What is true** — the two gates refuse from different evidence. A `[RequiresPackage]` floor F
compares the literal against the INSTALLED version only: `installed < F` refuses with no dependency
on the shipped archive. Convergence refuses `installed < B`, but B is read out of the bundled
archive at call time, and `BundledPackageConvergence.TryGetConvergenceRefusal` deliberately
WARN-AND-ALLOWS in two states: the archive version cannot be read (`BundledPackageConvergence.cs:80`),
and the bundled version carries a pre-release suffix (`:90`). While convergence is healthy,
`BundledProcessBuilderPackageTests.BundledArchive_ShouldCarryAtLeastEveryDeclaredRequirement`
constrains F <= B, so the floor's refusal set is a subset of convergence's and the floor buys only
better diagnostics. In the two degraded states convergence refuses NOTHING, and the floor is the
only gate that still fails closed. ENG-91846 added the 1.3.1.1 floor to Create/Modify for exactly
this reason: the performer block is silently discarded by an older server, so its gate must not
share convergence's failure modes.

**Why it is this way** — clio must never demand of an environment a version its own distribution
cannot supply, which caps F at B while B is readable; but convergence's own cap is the reason it
cannot fail closed when B is unreadable — refusing then would take every gated call down on a
broken distribution, so it warns instead, and that mercy is precisely the hole a hand-typed floor
does not have.

**What breaks if you ignore it** — arguing a reviewer's floor request away "because the refusal set
is a subset of convergence's" ships a gate that silently stops gating whenever the distribution's
archive is unreadable or mis-stamped - the exact moments a stale environment is most likely. The
opposite error is accepting a floor as a REPLACEMENT for convergence: the literal is static and
names the feature threshold, while convergence tracks every rebundle; both are needed. The ADR
(`spec/adr/adr-bundled-package-version-source-of-truth.md`) permits a hand-typed literal added in
the commit that creates the need - it forbids only deriving the literal from the archive.
