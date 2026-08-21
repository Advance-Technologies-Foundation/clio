---
description: Command_ShouldHave_DescriptionBlock_InReadmeFile fails on a single bool from ReadmeChecker.IsInReadme, which ANDs four independent documentation checks and never reports which of the four is missing
applies-to:
  - clio.tests/Command/ReadmeChecker.cs
  - clio.tests/Command/BaseCommandTests.cs
ticket: ENG-91314
date: 2026-08-19
---

**What is true** — `ReadmeChecker.IsInReadme` returns one boolean that is the conjunction of four
lookups: a `(docs/commands/<verb>.md)` link inside `clio/Commands.md`, a renderable
`clio/help/en/<verb>.txt`, an existing `clio/docs/commands/<verb>.md`, and a line starting `<verb>:` in
`clio/Wiki/WikiAnchors.txt`. `BaseCommandTests.Command_ShouldHave_DescriptionBlock_InReadmeFile`
asserts that boolean, so a new or renamed verb fails with "expected true" and no indication of which
artifact is absent. The checker also short-circuits to `true` for a `[Verb(Hidden = true)]` options
class, and it builds its renderer with a null feature-toggle service, so feature-toggled-off commands
are still required to have all four.

**Why it is this way** — the checker predates the four-artifact rule; each check was appended to the
same predicate. It reads the files from `AppContext.BaseDirectory/../../../../clio`, so it also fails
when the test binary is run from an unexpected output layout.

**What breaks if you ignore it** — the natural reading of the failure is "the verb is missing from
Commands.md", so a `[Verb]` rename that leaves, say, only `WikiAnchors.txt` stale sends you editing
the wrong file. Check all four before re-running, and remember that the paths are relative to the
build output, not to the repository root.
