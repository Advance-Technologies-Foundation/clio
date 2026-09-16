---
description: rebundle-process-builder.ps1 refreshes three of its four pins and leaves ExpectedArchiveSha256 on the previous value
applies-to:
  - rebundle-process-builder.ps1
  - clio.tests/Common/BundledProcessBuilderPackageTests.cs
  - docs/agent-instructions/bundled-packages.md
ticket: ENG-98559
date: 2026-09-16
---

**What is true** — a successful run of `rebundle-process-builder.ps1` updates `ExpectedArchiveVersion`,
`ExpectedDescriptorModifiedOnUtc` and `ExpectedProducingCommit` in
`clio.tests/Common/BundledProcessBuilderPackageTests.cs`, but NOT `ExpectedArchiveSha256`. Measured on two
consecutive cuts of `CrtProcessBuilder` (1.6.2.12 and 1.6.2.13) on 2026-09-16: the script reported the new
SHA in its own summary, and the constant in the fixture still held the previous one.

**Why it is this way** — not established. The script's documentation says it refreshes all four pins "in the
same run, so 'the pins are stale' stops being a reachable state", and its summary prints the SHA it
computed, so the intent is clearly to write it. Whatever the mechanism, the outcome is the state the pin
exists to prevent.

**What breaks if you ignore it** — `BundledArchive_ShouldMatchThePinnedHash` fails, which is the good case:
the guard catches it. The bad case is a rebundle committed without running the fixture, which is exactly
what that test's own comment warns about — *"refreshing the archive but not its SHA pin leaves a red test at
best and a lie at worst"*: the repository then claims a reviewable, pinned identity for bytes nobody pinned.
Until the script is fixed, set the constant by hand from
`shasum -a 256 clio/CrtProcessBuilder/CrtProcessBuilder.gz` (uppercase) and re-run the fixture before
committing.
