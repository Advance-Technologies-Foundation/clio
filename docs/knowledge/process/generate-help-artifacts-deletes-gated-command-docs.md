---
description: never run clio __generate-help-artifacts against the repo - HelpArtifactExporter deletes clio/docs/commands/*.md for every feature-toggled command (ring.md, the identity docs), which are hand-maintained
applies-to:
  - clio/HelpSystem/HelpArtifactExporter.cs
  - clio/HelpSystem/CommandHelpRenderer.cs
  - clio/docs/commands/ring.md
date: 2026-08-19
---

**What is true** — the hidden verb `clio __generate-help-artifacts` (`clio/Program.cs`, dispatched
before parsing) regenerates `clio/help/en/*`, `clio/Commands.md`, `clio/Wiki/WikiAnchors.txt` and
`clio/docs/commands/*.md`. It runs the exporter with an "export baseline" feature service in which
every `[FeatureToggle]` command counts as **off**, `IsCommandAdvertised` therefore returns false for
them, their names never enter the preserved-name set, and `CleanLegacyMarkdownDocs` **deletes** every
`*.md` in `clio/docs/commands` that is not in that set (only `sync-pages` and `sync-schemas` are
whitelisted extras). `clio/docs/commands/ring.md` and the other gated commands' docs are committed by
hand and are removed on every run.

**Why it is this way** — the export baseline is deliberately deterministic so generated artifacts do
not depend on whoever's local `appsettings.json` flags, and a gated-off command must not be
advertised. The repository nonetheless keeps documentation for gated commands, because they are
shipped code that people need to read about.

**What breaks if you ignore it** — one invocation rewrites well over a hundred files and deletes the
gated-command docs; the deletion looks like a legitimate part of a large regeneration diff and is
easy to commit. Treat the files under `clio/docs/commands` as hand-maintained: edit the one file you
need and do not run the generator over the tree.
