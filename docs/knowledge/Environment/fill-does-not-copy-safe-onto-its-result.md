---
description: EnvironmentSettings.Fill never assigns Safe onto the EnvironmentSettings it returns, so the Safe-environment confirmation has to stay inside Fill and cannot move to a downstream boundary
applies-to:
  - clio/Environment/ConfigurationOptions.cs
  - clio/Common/IInteractiveConsole.cs
  - clio.tests/Common/SafeEnvironmentFillTests.cs
ticket: ENG-91234
date: 2026-08-19
---

**What is true** — `EnvironmentSettings.Fill` builds a fresh `EnvironmentSettings` and copies field
by field (`Uri`, `IsNetCore`, credentials, `Maintainer`, `WorkspacePathes`, db-server options). It
does **not** copy `Safe`. The production-site confirmation therefore reads `this.Safe` — the stored
environment — inside `Fill`, and `IInteractiveConsole` is a required `Fill` parameter so every call
site is forced by the compiler to supply one.

**Why it is this way** — historical: `Fill` merges stored settings with command-line
`EnvironmentOptions`, and `Safe` has no command-line counterpart, so it was never added to the merge.
Nothing depends on the omission; nothing announces it either.

**What breaks if you ignore it** — the obvious cleanup is to move the confirmation "up to the
execution boundary" and gate it on `environment.Safe` there. Every resolved `EnvironmentSettings`
carries `Safe == null`, so that check never fires: the production-site prompt disappears for **all**
CLI commands and no test fails. The existing tests cover the throw and the fail-closed console
paths — none of them pins `result.Safe`, so the drop is invisible to the suite. If the confirmation
must move, copy `Safe` in `Fill` first and add a test that asserts it survives.
