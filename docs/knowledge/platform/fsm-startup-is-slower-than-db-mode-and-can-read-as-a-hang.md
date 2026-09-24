---
description: an environment's first application start after turn-fsm on has been observed to answer roughly a minute later than a DB-mode start, which without this fact reads as a hang rather than a slow but healthy start
applies-to:
  - clio/Command/TurnFsmCommand.cs
  - clio/Command/RestartCommand.cs
date: 2026-09-21
---

**What is true** — after switching an environment to file system mode and restarting it, the
application has been observed to take roughly a minute longer to start answering requests than the
same environment starting in database mode. `TurnFsmCommand.TryLoginWithRetry` already retries login
for up to 90 seconds and prints "Waiting for application to start after restart..." while it does, but
an operator watching the process directly (outside that retry loop, e.g. tailing a manually restarted
site) has no equivalent signal and can read the extra wait as a stuck or crashed process.

**Why it is this way** — in file system mode the application resolves package content from disk
(including through any symlinked package directories) instead of from pre-loaded database rows,
which pushes work into application start that DB mode does not have to do.

**What breaks if you ignore it** — killing or restarting the process again during this window
compounds the wait instead of shortening it. Give a freshly FSM-switched environment noticeably more
time to answer than a DB-mode environment before treating a non-responsive site as failed.
