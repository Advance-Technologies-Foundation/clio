---
description: rebundle-process-builder.ps1 refuses "-Version X is not higher than the version already in the descriptor" when the package branch committed its own set-pkg-version stamp before the clio cut; bundle that cut by hand from the tagged stamp commit, and the producing commit is then the stamp itself
applies-to:
  - rebundle-process-builder.ps1
  - clio.tests/Common/BundledProcessBuilderPackageTests.cs
ticket: ENG-95985
date: 2026-10-29
---

**What is true** — the script is built for one order: the package checkout sits on the PRE-stamp commit,
the script stamps `descriptor.json` itself (`set-pkg-version`), packs the commit with that file overlaid, and the
stamp is committed afterwards. When the package branch has already committed the stamp and tagged it
(`crtprocessbuilder-<version>` on the stamp commit), the descriptor carries the target version and the script
refuses `-Version <that version>` before writing anything. The 1.6.6.127 cut was made by hand instead:
`git -c core.autocrlf=false -c core.eol=lf archive --format=zip <tag commit> -- packages/CrtProcessBuilder`,
expanded with `Expand-Archive`, no overlay, `clio compress <export> --skip-pdb`, the script's inventory walk run
against the result, and the four provenance pins typed by hand - `ExpectedProducingCommit` is the stamp commit,
and its `descriptor.json` carries the pinned version and `ModifiedOnUtc`.

**Why it is this way** — the script deliberately has no "same version" flag (it would be a permanent hole for a
one-off), and running it with a HIGHER version would restamp the package checkout and ship a number the package
branch never tagged.

**What breaks if you ignore it** — raising `-Version` to get past the refusal produces an archive whose version
and stamp exist in no package commit and differ from the tag the package PR merges, so provenance and the
claimed number both go wrong; checking out the pre-stamp commit to satisfy the script rewrites the stamp with a
new `ModifiedOnUtc`, and the bytes no longer match the tagged cut.
