---
description: A version number picked before merging collides - three repositories did it on one ticket; and two artifacts at one number are a defect only when their content differs, which the number cannot tell you
applies-to:
  - docs/agent-instructions/bundled-packages.md
  - clio/Common/BundledPackageCatalog.cs
  - clio.tests/Common/BundledProcessBuilderPackageTests.cs
ticket: ENG-91853
date: 2026-09-08
---

**What is true** — **merge first, then choose the version.** Choosing first guarantees a collision,
because the number is picked against a base you are about to replace.

ENG-91853 hit this three times, in three repositories, and the cause was identical each time:

| | branch chose | master had reached | outcome |
|---|---|---|---|
| CrtProcessBuilder | 1.6.0.1 | 1.6.0.1 | two archives, one number |
| CrtProcessBuilder | 1.6.0.2 | 1.6.0.2 | again, next cycle |
| clio-knowledge | 1.13.98 (untouched) | 1.13.98 | two libraries, one number |

Nobody saw it coming the second time or the third.

**Detection is a separate problem from prevention, and needs the other half: compare CONTENT, not
version numbers.** Two artifacts at one number are only a defect when they differ. Same number plus
same content is a duplicate stamp and harmless; same number plus different content is the failure. The
field alone gives you a suspicion; the trees give you the answer:

```
branch  "libraryVersion": "1.13.98"   guidance tree 7a6d2867…
master  "libraryVersion": "1.13.98"   guidance tree 94ca3ef6…   <- different, so it is real
```

Re-read the base's version at **push** time, not at commit time. "master was still at .98 as of the
commit" is exactly the condition that was true in all three collisions, right up until it was not.

**Why it is this way** — a version is chosen at the moment of building an artifact, and a branch that
takes hours to land is a branch whose base moves underneath the choice. Nothing in either repository's
tooling reads the base's number: `rebundle-process-builder.ps1` takes `-Version` from the caller, and
the knowledge manifest's `libraryVersion` is edited by hand. Both trust the human to have looked, and
both were used by someone who had looked — earlier.

**What breaks if you ignore it** — the failure is silent in the direction that matters. For a bundled
package, clio compares the shipped version against the version an environment recorded, so an archive
reusing a number reaches NEW installs only: everybody already carrying it is never offered the update.
For the guidance library, clio **rejects** a library whose content changed under a reused sequence, and
a rejected update keeps serving the OLD generation. In both cases the change merges, passes CI, and is
invisible to every existing consumer. Nothing goes red.

On this ticket the knowledge collision was caught by a reviewer checking something else — no test, no
gate, and neither session was looking at that field. See
[[three-defences-and-no-oracle-is-an-unpinned-rule]] for why that is the expected way to find one.
