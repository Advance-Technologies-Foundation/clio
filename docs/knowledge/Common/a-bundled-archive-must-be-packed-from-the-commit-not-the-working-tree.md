---
description: the bundled package archive is packed from a git export of the producing commit with core.autocrlf=false and core.eol=lf - packing the working tree makes the SHA-256 provenance pin depend on the operator's line endings, and the clean-tree gate cannot catch that
applies-to:
  - rebundle-process-builder.ps1
  - clio.tests/Common/BundledProcessBuilderPackageTests.cs
  - docs/agent-instructions/bundled-packages.md
ticket: ENG-94374
date: 2026-09-08
---

**What is true** — `clio/CrtProcessBuilder/CrtProcessBuilder.gz` is cut from
`git -c core.autocrlf=false -c core.eol=lf archive --format=zip <producing commit> -- packages/CrtProcessBuilder`,
expanded to a temp directory, with the single tooling-owned `descriptor.json` overlaid from the restamped
working tree. `rebundle-process-builder.ps1` step 3b does this; step 4 packs the export, never
`$PackageRepoPath`. Measured for the 1.6.1.1 cut: 153 entries, 152 byte-identical to
`git show <commit>:<path>`, 0 line-ending-only differences, 0 content differences.

**Why it is this way** — `ExpectedArchiveSha256` is the sole prescribed control on a binary that installs
executable C# onto customer environments, so it has to be reproducible by someone who has only the commit
id. Packing the working tree broke that in a way nothing reported: a file freshly written by a tool is LF in
the tree, and the same file after a clean checkout on Windows (`core.autocrlf=true`, or `* text=auto` in
`.gitattributes`) is CRLF. Same commit, same content, different bytes, different hash. A reviewer following
the documented recipe therefore got a mismatch with no way to tell a tampered archive from a merely
repacked one.

`git archive` alone does NOT fix it, and this is the part that is easy to get wrong: it runs the same
working-tree conversion a checkout does, so with `core.autocrlf=true` it emits CRLF and the hash is still
machine-dependent. The two `-c` flags are what make it hand back blob bytes on every host and every git
configuration. What remains host-dependent is the path separator the container records per entry, so cut and
verify on Windows.

**What breaks if you ignore it** — the pin degrades from provenance to a change detector: it still goes red
when the archive moves, and it establishes nothing about WHERE the bytes came from, which is the one
question it exists to answer. This has already happened twice — nine files at the 1.3.1.1 cut, thirteen at
1.6.1.0 — and both times the archive was clean, correct and unreproducible at the same time. Note especially
that the script's clean-tree gate **cannot** catch it: a tree can be clean, current and CRLF at once, so
there is no dirty state to refuse. That is why the fix is structural rather than another gate. If you change
how the archive is produced, change the recipe in `docs/agent-instructions/bundled-packages.md` and the
remarks on `ExpectedArchiveSha256` in the same commit — those two are what a reviewer follows, and a recipe
that no longer reproduces the pin is the defect this record is about.
