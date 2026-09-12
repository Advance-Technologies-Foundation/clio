---
description: a package version named as the remedy in a caller-facing warning is advice the reader has already satisfied - convergence refuses every environment below the archive clio ships, so nobody who can see the message is below any floor it could quote, and the reachable cause is a capability gap rather than an old number
applies-to:
  - clio/Command/ProcessModel/FlowLabelExpectation.cs
  - clio.tests/Common/BundledProcessBuilderPackageTests.cs
  - clio/Command/McpServer/Tools/ProcessDesigner/DescribeProcessTool.cs
  - clio/Command/McpServer/Prompts/ProcessDesigner/DescribeProcessPrompt.cs
  - clio/Command/ProcessModel/EmailBlockExpectation.cs
  - clio/Command/ProcessModel/ApprovalBlockExpectation.cs
  - clio/Command/ProcessModel/AccessRightsBlockExpectation.cs
  - clio/Common/BundledPackageConvergence.cs
ticket: ENG-91853
date: 2026-09-10
---

**What is true** — a post-write guard must **not** name a package version as the remedy in the text it
hands a caller. Every command that can reach one of these guards carries `[RequiresPackage]` for the
bundled package, and `RequiredPackageChecker` throws a `PackageRequirementException` on a convergence
refusal — which `BundledPackageConvergence` returns for **any** installed version below the one clio
ships, not merely below the declared floor ([[a-presence-only-requirespackage-still-takes-a-convergence-refusal]]).

So a reader who can see the warning at all is normally already at or above the shipped archive, which is
far above any capability floor a message would quote. "Update to X.Y.Z.W" is therefore an instruction
they have satisfied before reading it.

**Two paths do bypass the refusal** and the rule survives both: convergence warn-and-allows when the
bundled archive version cannot be read and when it carries a pre-release suffix
(`BundledPackageConvergence.cs:78-109`, recorded in
[[requirespackage-version-floor-survives-convergence-degraded-modes]]). A genuinely older package is
then reachable through the presence-only describe gate. It changes nothing here, because in exactly
those states `install-process-builder` is itself refused - so a version literal would have been no more
actionable than the qualitative text. Say "normally", not "never".

**Why it is this way** — the two gates answer different questions and run in sequence. The floor says
what the code needs; convergence says clio will not drive a server older than the one it bundles. The
second is strictly stronger, and it runs on the same path, so the first can never be the binding
constraint at the moment a guard fires.

**What breaks if you ignore it** — the message is not merely useless, it is misdirection, and it
misdirects hardest in the one case it fires. The reachable cause is a **capability** gap: a cut from a
branch line that never carried the member, whose number can be arbitrarily high. The reader re-runs
`install-process-builder`, nothing changes, and the text has named neither the cause nor anything they
can act on — while the version-shaped literal reads as precise. This is the trap
`docs/agent-instructions/bundled-packages.md` states as "a numeric floor cannot express a CAPABILITY
once two branches cut numbers".

Say it the way the three sibling guards do — `EmailBlockExpectation` writes "predates the sendEmail
element" and carries no version LITERAL — and point at `clio install-process-builder`. Copy the shape,
not every clause: those siblings also say "check the package version", which is the half that does not
survive this record. Name the cause qualitatively AND leave the version out of the remedy. A version literal is
legitimate in two other places, and the distinction is what the sentence DOES: recording where a
measurement was taken, or when a member first shipped, is provenance and stays. Telling a reader to go
install a number is advice and must not.

The regression guard is an inverse test: `FlowLabelSurfaces_ShouldNameNoPackageVersion` in
`clio.tests/Common/BundledProcessBuilderPackageTests.cs` asserts these surfaces match no four-part
version at all, because a number removed on purpose is otherwise free to come back looking helpful.

**The counter-example is real, not hypothetical, and it is measured.** A reviewer left this as the one
open question on the change: does any archive numbered ABOVE the capability floor actually lack the
member? It does. Counted over the tags in the CrtProcessBuilder repository on 2026-09-10:

```
crtprocessbuilder-1.6.1.2   a115cc75   [DataMember(Name = "label")] declarations: 0
crtprocessbuilder-1.6.1.4   9c86d9b0   [DataMember(Name = "label")] declarations: 3
```

1.6.1.2 is higher than the 1.6.0.8 the member first shipped in, and carries none of it - the 1.6.1.x
line was cut from a branch that did not have the label work. The package's own default branch has 0 as
well. So a reader on 1.6.1.2 satisfies "1.6.0.8 or newer" by arithmetic and still has the field
discarded silently: the qualitative wording is not a stylistic improvement, it is the only wording that
is TRUE. (Tags 1.6.1.0, 1.6.1.1 and 1.6.1.3 do not exist - one was replaced, one was burned by an
aborted rebundle - so 1.6.1.2 is the whole population below 1.6.1.4.)
