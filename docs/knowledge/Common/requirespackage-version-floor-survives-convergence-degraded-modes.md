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

**Updated by ENG-95891.** The Create/Modify floor moved from 1.3.1.1 to 1.4.0.3. For a while the floor EQUALLED
the bundled version, and while that holds the two refusals stop being nested: `installed >= floor` already implies
`installed >= bundled`, so `TryGetConvergenceRefusal` cannot fire on that command at all, and the sentence above
about the floor's refusal set being a subset of convergence's is true only in the degenerate sense that the sets are
equal. Agent-facing text must therefore not promise "the convergence message naming both versions" as the refusal a
caller will see — in that state it names one.

That state has ended: the branch cut further archives and the bundled version is well ahead of the floor again, so
the nesting is back and the general rule above applies. Do NOT restate the bundled version here — this record said
"1.4.0.3 is also the version clio bundles" and was 23 patches stale within a day. The bundled version has exactly
one home, `ExpectedArchiveVersion` in `clio.tests/Common/BundledProcessBuilderPackageTests.cs`; what belongs in a
record is the CONDITION (floor equals bundled) and what it does, not the numbers that happen to satisfy it today.
