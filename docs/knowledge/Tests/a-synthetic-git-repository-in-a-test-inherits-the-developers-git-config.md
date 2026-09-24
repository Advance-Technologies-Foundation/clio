---
description: a test that git-inits a temp repository inherits the machine's git config, and on Windows core.autocrlf=true makes `git add -A` fill the stderr pipe, so a sequential ReadToEnd on the two pipes deadlocks forever
applies-to:
  - clio.tests/McpE2eSelectionCoverageTests.cs
ticket: GH-1605
date: 2026-09-18
---

**What is true** — three separate things bite a test that drives `git` in a temporary repository, and on
macOS none of them show.

1. A repository created by `git init` reads the machine's SYSTEM and GLOBAL git config. The Git for
   Windows installer writes `core.autocrlf=true` into `C:\Program Files\Git\etc\gitconfig`, so on a
   default Windows box every LF-written file makes `git add -A` print
   `warning: in the working copy of '<path>', LF will be replaced by CRLF the next time Git touches it`.
   Measured here: 7791 bytes of stderr for a 60-file synthetic tree.

2. Reading a child process's two redirected pipes one after the other —
   `StandardOutput.ReadToEnd()` then `StandardError.ReadToEnd()` — deadlocks as soon as the child writes
   more than the pipe buffer (~4 KB) to the stream that is not being read. The child blocks writing,
   the parent blocks reading, and `WaitForExit()` is never reached. Combined with (1), `git add -A` on
   Windows reaches that volume unaided.

3. Git writes its loose objects and pack files read-only. `Directory.Delete(recursive: true)` over them
   throws `UnauthorizedAccessException` on Windows, which is **not** an `IOException`, so a
   `catch (IOException)` in `Dispose` does not hold it.

**Why it is this way** — (1) is the Git for Windows installer default, not something the repository or
the test chooses; (2) is the documented `ProcessStartInfo` redirection hazard, and it is invisible until
a command happens to be talkative; (3) is git protecting content-addressed objects from being edited in
place.

**What breaks if you ignore it** — the three compound into a failure that reports the wrong cause. On
this machine, before the fix, `dotnet test --filter ~McpE2eSelectionCoverageTests.Registration` never
returned: `git.exe add -A` sat at 0.05 s of CPU for as long as it was left alive, so on a Windows agent
the job burns its timeout instead of reporting a failure. When the wedged `git` was finally killed,
`Git()` threw — and `Dispose` then threw `UnauthorizedAccessException` while the `using` was unwinding,
**replacing** that exception. The only thing in the report was "Access to the path
'107ef01ffe8466140538efc7d2a94c50bde4ad' is denied", which names neither git nor the deadlock.

This is not a local-configuration curiosity. The hosted `Unit Test Shard (unit-4)` lane - the final
shard, which runs every fixture the shard manifest does not assign elsewhere - wedged the same way on
GitHub's `windows-latest`: it sat in `Run unit test shard` for over three and a half hours on this
pull request while the other three shards reported success in four minutes. `gh pr checks` printed
that job as `pending`, which reads as "still queued", not as "wedged", so the pull request looked
green while a required lane was hung.


What makes it safe: drain the pipes concurrently (`StandardError.ReadToEndAsync()` started before
`StandardOutput.ReadToEnd()`); neutralise the inherited configuration rather than assuming it
(`GIT_CONFIG_NOSYSTEM=1`, `GIT_CONFIG_GLOBAL` pointing at a path that does not exist, plus an explicit
`git config core.autocrlf false` in the repository); and clear `FileAttributes.ReadOnly` across the tree
before deleting it, with a `catch (Exception)` so cleanup can never mask the test's own failure.
