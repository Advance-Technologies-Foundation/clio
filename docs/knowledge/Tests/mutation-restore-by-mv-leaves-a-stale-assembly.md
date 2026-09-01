---
description: restoring a mutated source file from a cp/mv backup keeps the backup's older mtime, so MSBuild reports "Build succeeded" without recompiling and the next --no-build run silently tests the MUTATED binary
applies-to:
  - docs/knowledge/Tests/
ticket: ENG-95891
date: 2026-09-01
---

**What is true** — mutation testing (break the production code, confirm the test reddens, restore) is only
sound if the restore actually reaches the binary. Restoring with `cp file file.bak` … `mv file.bak file`
does not reliably do that: `cp` stamps the backup at copy time, which is BEFORE the build of the mutated
source, and `mv` preserves that timestamp. MSBuild then sees a source file older than its output, prints
`Build succeeded`, and compiles nothing. The following `dotnet test --no-build` runs the MUTATED assembly
while every log line says the tree is clean and built.

Restoring with `git checkout -- <file>` does not have the problem: it writes the file fresh, so the
timestamp moves forward and the rebuild happens.

**Why it is this way** — MSBuild's up-to-date check is timestamp-based, not content-based, and "restore a
file to an earlier state" is exactly the operation that moves a timestamp backwards. Nothing in the tool
output distinguishes "compiled" from "skipped as up to date" at normal verbosity.

**What breaks if you ignore it** — the failure is silent and points the wrong way. In ENG-95891 a full
suite reported one failure immediately before a commit; the source on disk was correct and passed 985/985
on a clean rebuild, so the "failure" was the stale mutated binary. The dangerous direction is the mirror
image: mutate, restore by `mv`, re-run, see GREEN, and conclude the mutation was reverted when the binary
still carries it — or worse, conclude a test is insensitive when it was never actually run against the
restored code. Either way the mutation result is fabricated.

Rules that make it safe: restore with `git checkout --` whenever the file is committed; when the tree is
dirty and a backup is unavoidable, force the rebuild (`--no-incremental`, or `touch` the restored file)
and re-run WITHOUT `--no-build`; and always re-run the full suite after a restore rather than trusting
the build line.
