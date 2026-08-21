---
description: get-classic-page-sources writes .clio-migration/ with the same anchor as get-page's .clio-pages but without PageFileWriter.EnsureGitIgnoreEntry, so the extracted sources land unignored in the user's checkout
applies-to:
  - clio/Command/GetClassicPageSourcesCommand.cs
  - clio/Command/PageFileWriter.cs
date: 2026-08-19
---

**What is true** — `GetClassicPageSourcesCommand.ResolveOutputPath` carries the comment "the default is
anchored the way `get-page` anchors `.clio-pages`", and the anchoring really is shared: both go through
`PageOutputDirectoryResolver.ResolveAnchor`. The parity stops there. `get-page` writes through
`PageFileWriter`, whose `EnsureGitIgnoreEntry` drops a `*\n!.gitignore\n` file into the output root the
first time it is used. The classic-sources path creates its directory with a bare
`Directory.CreateDirectory` and writes the manifest, so `.clio-migration/` never gets a `.gitignore`.

**Why it is this way** — the two output roots grew separately: `.clio-pages` needs the ignore file
because it doubles as the page baseline store that `get-page`/`update-page` rewrite on every round-trip,
and the hygiene was added there. `.clio-migration/` was modelled on it for path resolution only, and the
ignore file was left out of scope.

**What breaks if you ignore it** — when the anchor resolves to a workspace root (the normal case for a
developer running the migration flow), every extracted classic schema shows up as untracked noise and is
easy to commit by accident: `git add -A` after a migration run pulls a whole `.clio-migration/` tree,
including the source of Classic schemas from a customer environment, into the repository. The read of the
code that misleads is the comment itself — it invites the conclusion that the hygiene is shared too, and
nothing on the `get-classic-page-sources` side says otherwise.
