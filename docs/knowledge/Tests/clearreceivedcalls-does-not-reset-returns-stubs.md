---
description: ClearReceivedCalls in TearDown resets only call history, not configured .Returns stubs - NUnit reuses the fixture instance, so a stub set by one test leaks into the next and silently short-circuits the gate under test
applies-to:
  - clio.tests/Command/CompileConfigurationCommand.Tests.cs
  - clio.tests/Command/CompilePackageCommand.Tests.cs
ticket: ENG-93157
date: 2026-08-19
---

**What is true** — NUnit creates one fixture instance and reuses it for every test in the class, and
the substitutes held in its fields live just as long. `ClearReceivedCalls()` clears the recorded
*calls* on a substitute; it does not remove the `.Returns(...)` configurations. A stub arranged in
one test therefore stays in force for every test that runs after it in the same fixture. Restoring
the production default belongs in `Setup()`, not in `TearDown()` — which is why both compile-command
fixtures re-stub `_interactiveConsole.IsInteractive.Returns(false)` at the top of `Setup()`.

**Why it is this way** — `AGENTS.md` (test style policy, "Clear substitute received calls in teardown
(`ClearReceivedCalls`) to avoid cross-test interference") reads as if the teardown call covers
cross-test interference in full. It covers only half of it: received calls are per-test state,
returned values are not.

**What breaks if you ignore it** — the failure is an order-dependent false negative, not a red test.
A test that stubs a permissive answer (`IsInteractive = true`, `Prompt = false`) leaves a later test
running against a confirmation gate that now short-circuits before reaching the code the test claims
to exercise. It passes, and it keeps passing until someone reorders or renames a test, at which point
an unrelated commit appears to break it. Re-arrange every stubbed member to its production default in
`Setup()`; do not rely on teardown to undo a stub.
