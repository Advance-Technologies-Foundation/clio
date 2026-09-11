---
description: SaveSettingsUnlocked branches on `fileSystem is FileSystem`, so every MockFileSystem test writes through a StreamWriter path production never executes - a regression in the real FileStream/File.Replace branch is invisible to the mock suite
applies-to:
  - clio/Environment/ConfigurationOptions.cs
  - clio.tests/Command/SettingsRepositoryConcurrencyTests.cs
date: 2026-09-08
---

**What is true** — `SaveSettingsUnlocked` computes `bool isRealFileSystem = fileSystem is FileSystem`
and hands it to `WriteSettingsTempFile` and `CommitSettingsFile`, which then take **materially
different** paths:

| | real `FileSystem` | `MockFileSystem` |
|---|---|---|
| temp write | `FileStream` with `FileMode.CreateNew` / `FileShare.None`, `UnixCreateMode`, `SetAccessControl`, `Flush(flushToDisk: true)` | `File.CreateText` + `Flush()` |
| publish over an existing file | `File.Replace` | `File.Move(overwrite: true)` |
| permission copy in `GetSettingsFileSecurity` | runs when the destination already exists | early-returns `(null, null)` |

So a test that injects a `MockFileSystem` — which is nearly every settings test in `clio.tests` —
exercises the second column only. Nothing in the type system or the test names says so.

**Why it is this way** — the two branches are not stylistic. `File.Replace` is the only publish a
Windows reader holding the file open can be made to tolerate (`File.Move(overwrite: true)` is refused
even by `FileShare.ReadWrite | FileShare.Delete`), and `MockFileSystem` has no Windows sharing
semantics to respect, no ACLs and no `Flush(flushToDisk:)`. Modelling them in the double would mean
reimplementing the platform, so the branch is real and deliberate.

**What breaks if you ignore it** — a regression in the real branch passes the whole mock suite
silently. Concretely: this is the fact that let a fixture named for the atomic write stay green after
the real branch was reverted to truncate-in-place, because its assertions were post-conditions of a
completed write. When changing anything under `isRealFileSystem == true`, pin it in
`SettingsRepositoryRealFileSystemPublishTests` (`[Category("Integration")]` — it does real disk I/O)
and prove the new test bites by applying the reverse mutation and watching it fail. The mock suite
agreeing is not evidence.
