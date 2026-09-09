---
description: ProcessLibResolver takes no policy selector, so every surface wired to IProcessModelGenerator silently inherits the active-version caption policy — get-process-signature and run-process included
applies-to:
  - clio/Command/ProcessModel/ProcessLibResolver.cs
  - clio/Command/ProcessModel/ProcessModelGenerator.cs
  - clio/Command/GetProcessSignatureCommand.cs
  - clio/Command/RunProcessCommand.cs
  - clio/Command/GenerateProcessModelCommand.cs
ticket: ENG-94374
date: 2026-09-07
---

**What is true** — `ProcessLibResolver.Resolve` has no policy parameter. Any command that resolves a
process by caption through `IProcessModelGenerator.Generate` therefore gets the active-version policy,
whether or not that surface was considered: today `generate-process-model`, `get-process-signature`
(also reached from `PageUpdateTool`) and `run-process`, plus `describe --process-caption`, which calls
the resolver directly. Four user-facing surfaces, one policy. The same seam also brings a refusal those
surfaces did not have: a lone caption match the view flags explicitly `IsActiveVersion == false`
returns `Conflict` instead of resolving.

**Why it is this way** — the policy exists once precisely so describe and generate-process-model cannot
drift apart, and `Generate` is the only caption-resolution path those commands have. ENG-94374's PRD
listed "will NOT make `get-process-signature` version-aware" as a bounded-scope non-goal; the exclusion
was not achievable by leaving code alone. Honouring it would have meant ADDING a policy selector to the
shared seam — more work than letting the surface inherit, and a second policy to keep true. The
non-goal was withdrawn and the ADR's alternative G moved off Rejected instead.

**What breaks if you ignore it** — wiring a new command to `IProcessModelGenerator` changes that
command's caption behaviour with no edit to the command, its docs, or its help text, and reviewers
reading the diff see nothing. The reverse trap is worse: assuming a documented non-goal keeps a surface
version-blind leaves shipped `[Description]`, `docs/commands/*.md` and `help/en/*.txt` contradicting a
spec that says the opposite, and the next reader cannot tell intended widening from accident. If a
surface genuinely must keep the old resolution, give `Resolve` an explicit policy parameter — do not
assume the seam left it out.
