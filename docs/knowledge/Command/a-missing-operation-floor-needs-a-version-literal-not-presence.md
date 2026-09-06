---
description: a [RequiresPackage] floor for an operation the older package does not HAVE must be a version literal; presence-only turns the refusal into a 404 that reads as clio being broken
applies-to:
  - clio/Command/ModifyProcessAsNewVersionCommand.cs
  - clio/Command/SetActiveProcessVersionCommand.cs
  - clio.tests/Command/ProcessDesignerRequiresPackageAttributeTests.cs
ticket: ENG-94374
date: 2026-09-06
---

**What is true** — `[RequiresPackage]` on a process-designer options class carries a version literal for two
different reasons, and they are not interchangeable. The older ones (`CreateBusinessProcessOptions`,
`ModifyBusinessProcessOptions`) name a version because an older server MISHANDLES a newer *input form* it
still accepts. `ModifyProcessAsNewVersionOptions` and `SetActiveProcessVersionOptions` name one because the
*operations themselves* do not exist before `CrtProcessBuilder 1.5.0.0`.

**Why it is this way** — the presence-only form exists so the convergence rule (`IBundledPackageConvergence`)
can own "keep the environment current" without a literal restating that policy where it cannot track the
archive. That reasoning holds only while every operation the command calls exists in every package version
the gate would let through. It does not hold for a NEW operation: `ProcessDesignService` is a WCF service, so
an environment on an older package has no such route and answers the POST with a 404 — an HTTP fault, not a
contract error with a message. The gate is the only place that can turn that into "your package is behind".

**What breaks if you ignore it** — with a presence-only requirement the gate passes, the call goes out, and
the caller gets a transport failure carrying an HTML error page. Nothing in it names `CrtProcessBuilder`, so
the agent (or the user) reads it as clio being broken and starts debugging the wrong system; the
`install-process-builder` remediation the hint exists to deliver is never shown. The same trap waits for
every future operation added to a bundled package: **the commit that starts CALLING a new operation is the
commit that must declare its version literal**, and the archive carrying that operation has to ship first —
`BundledArchive_ShouldCarryAtLeastEveryDeclaredRequirement` fails while a declared floor exceeds the bundled
version, which is the intended, temporary red between the two.
