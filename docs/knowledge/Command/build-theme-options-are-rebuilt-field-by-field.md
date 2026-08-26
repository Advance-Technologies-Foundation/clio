---
description: BuildThemeCommand.TryNormalizeOptions constructs a fresh BuildThemeOptions field by field, so a new build-theme option that is not copied there is silently discarded before the build sees it
applies-to:
  - clio/Command/Theming/BuildThemeCommand.cs
ticket: ENG-93985
date: 2026-08-19
---

**What is true** — every `build-theme` entry point routes through
`BuildThemeCommand.TryNormalizeOptions`, which does not mutate the incoming `BuildThemeOptions`: it
builds a **new** instance and assigns each field explicitly. Adding an option to `BuildThemeOptions`
and wiring it to the CLI and the MCP tool is therefore not enough - it has to be copied inside
`TryNormalizeOptions` as well.

**Why it is this way** — normalization is where the caption-derived CSS class name and the font-family
canonicalization happen, and rebuilding the record keeps that seam free of in-place mutation of a
caller-owned object. The cost is that the copy list is an enumeration nothing checks.

**What breaks if you ignore it** — the option parses, binds and reaches the command, and then
evaporates. The command exits 0 and writes a theme that ignores it, with no warning: there is no error
path at all, because as far as the builder is concerned the value was never supplied. This has already
cost one debugging cycle. When adding an option, also decide whether an ignored-input advisory belongs
in `CollectWarnings` next to `FontWeightsWithoutFamily`, which exists precisely because a silently
dropped input reads to the caller as a clio defect.
