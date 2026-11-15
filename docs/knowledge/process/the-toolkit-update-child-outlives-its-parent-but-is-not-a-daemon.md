---
description: the toolkit auto-update child (FireAndForgetAsync, DiscardOutput) is NOT a daemon - it outlives its parent but a caller that reaps the process tree can kill it; verify it from a real terminal, and know that a kill costs one window and self-heals
applies-to:
  - clio/Common/Skills/ToolkitAutoUpdateLauncher.cs
  - clio/Common/ProcessExecutor.cs
ticket: "#1950"
date: 2026-11-15
---

**What is true** — a child started through `IProcessExecutor.FireAndForgetAsync` with
`DiscardOutput = true` keeps running after its parent exits, although the drain reading its redirected
output dies with the parent (.NET ignores a broken console pipe on write). Measured on Windows 11 from a
terminal tab: `clio info --clio` returned in 0.51 s with the toolkit due, and its detached
`clio update-toolkit` child went on to finish a full Codex reinstall in a sandbox `CODEX_HOME`.
`CreateNoWindow = true` gives it no console on Windows, so Ctrl+C and closing the terminal do not
reach it. It is still a plain child, though: no `setsid` on Unix, no job breakaway on Windows. A
terminal hang-up on Unix, or a caller that kills its whole process tree or job, kills it too. The same
check driven from Claude Code's Bash tool did not complete; whether that tool reaps children was not
isolated.

**Why it is this way** — real detachment is per-platform native work (`setsid`/`posix_spawn` attributes,
`CREATE_BREAKAWAY_FROM_JOB`, which a job may forbid). It was not needed to remove the 15 s stall, and a
kill is cheap. `TryScheduleAutoupdate` has already advanced `next-run`, so a killed child loses one
window (60 minutes by default). A Codex reinstall cut off after its legacy cleanup leaves no cache
payload, which reads as "no installed commit", so the next window updates it.

**What breaks if you ignore it** — verifying the background path from an agent's shell can produce a
false negative. The predictable wrong fixes are inheriting the console instead of discarding output,
which prints results into the terminal after the prompt returns (and into the JSON-RPC stream under
`mcp-server`), or running the update in-process again, which is the stall #1950 removed. Prove it from a
terminal tab with work the child must visibly complete. If kills ever turn out to be frequent, move to
real detachment rather than back to in-process.
