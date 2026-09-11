---
description: convergence never reads the [RequiresPackage] version, so a presence-only requirement with no version literal still refuses an environment behind the bundled version - an empty grep for a floor does not mean the command is unaffected by a rebundle
applies-to:
  - clio/Common/BundledPackageConvergence.cs
  - clio/Common/RequiredPackageChecker.cs
  - clio/Command/DescribeProcessCommand.cs
  - clio/Command/ListUserTasksCommand.cs
  - clio/Command/McpServer/Tools/ProcessDesigner/ValidateProcessGraphTool.cs
ticket: ENG-91853
date: 2026-09-08
---

**What is true** — a `[RequiresPackage]` attribute with **no version argument** still causes a refusal
when the environment is behind the version clio ships. The two gates read different evidence and the
second one never looks at the attribute:

`RequiredPackageChecker` asks convergence *after* the requirement is satisfied
(`RequiredPackageChecker.cs:232-237`), and its own comment says it is asked "only for packages clio
actually ships (**the catalog decides that, so nothing declares it**)".
`BundledPackageConvergence.TryGetConvergenceRefusal` takes only a package name and the installed
version — there is no parameter for the requirement's floor — and returns `false` only when the package
is absent, is not bundled, has an unreadable or suffixed bundled version, or `installedVersion >=
bundledVersion`.

So for the ProcessBuilder package, **all five** gated surfaces refuse a behind environment, not just
the two carrying a literal:

| site | floor | refuses a behind environment |
|---|---|---|
| `CreateBusinessProcessOptions` (CreateBusinessProcessCommand.cs) | `1.6.2.1` | yes |
| `ModifyBusinessProcessOptions` (ModifyBusinessProcessCommand.cs) | `1.6.2.1` | yes |
| `DescribeProcessOptions` (DescribeProcessCommand.cs) | none — `Hint` only | **yes** |
| `ListUserTasksOptions` (ListUserTasksCommand.cs) | none — `Hint` only | **yes** |
| `ValidateProcessGraphArgs` (ValidateProcessGraphTool.cs) | none — `Hint` only | **yes** |

The sites are named by the DECLARING TYPE, not by file and line. Line numbers in this table went stale
twice — the last time when a rationale comment grew above an attribute and moved it nine lines — and a
stale pointer is worse than none here, because the instruction beside it is to read each attribute in
full. A type name survives every edit that does not rename it, and finds the attribute in one grep.
| `GetProcessSignatureCommand.cs:17` | deliberately not gated | no |

**Why it is this way** — by design, and the design is argued in
`spec/adr/adr-bundled-package-version-source-of-truth.md`. The attribute states what the *code* needs;
convergence states what environments should be *brought to*. They can legitimately disagree, and
`BundledPackageConvergence`'s own docblock says it is "deliberately a DIFFERENT rule from
`RequiresPackageAttribute`, **not a flag on it**". Making the floor govern convergence would make the
two facts unsayable separately, which is what the feature originally did.

**What breaks if you ignore it** — you rebundle, check which commands carry a version literal, and
conclude the rest are unaffected. They are not, and the failure is not hypothetical: a rebundle earlier
in ENG-91853 left a reviewer's clio refusing **`describe-business-process`** against a stand still on
the previous version — a command with no floor at all. AGENTS.md records that incident; this record is
why it happened.

The misreading is easy and was made twice in one afternoon on this ticket, in both directions of a
review. A reviewer verified "floors, both commands, 1.6.0.3" with a regex requiring the literal on the
attribute's own line, which could only ever see two of the five sites; on being corrected, both parties
then inferred that the three presence-only sites had "no floor to collide with", and read that as
unaffected. The first error was an incomplete probe, the second a wrong inference from its corrected
output — and only the second one would have reached a user, as advice about which commands a stand
upgrade affects.

Practical consequence when raising a bundled version: the blast radius is **every** site naming that
package in `[RequiresPackage]`, which you find by grepping for the package name and reading each
attribute in full. Grepping for the package name *and a version literal together* answers a different
question, and answers it with an undercount that looks like a clean result.

Related: [[requirespackage-version-floor-survives-convergence-degraded-modes]] — the same two gates from
the other side, where the floor is the only fail-closed refusal because convergence warn-and-allows.
