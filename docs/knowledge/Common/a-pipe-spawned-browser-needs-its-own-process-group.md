---
description: page-smoke's posix_spawn-ed Chrome must lead its own process group with a clean signal mask, or a macOS GitHub runner wedges
applies-to:
  - clio/Common/BrowserSession/UnixPipeSpawn.cs
  - .github/workflows/build.yml
ticket: ENG-103330
date: 2026-10-14
---

**What is true** — `UnixPipeSpawn` starts the browser with `POSIX_SPAWN_SETPGROUP` (a new process group), an
empty signal mask and SIGPIPE at its default, and ends it with `kill(-pid, SIGKILL)` for the whole group. Puppeteer
and Playwright do the same on Unix (`detached: true`). `System.Diagnostics.Process` resets the mask and the signal
dispositions in its own children, but posix_spawn does neither unless asked.

**Why it is this way** — The first version used none of these attributes. On `macos-latest` the
`Unix File Mode Tests` leg then hung on the real-Chrome `HeadlessPageIntegrationTests`. The runner itself stopped
responding: the 15-minute step timeout never fired, and no log could be fetched after the job was cancelled. The
`posix_spawn` echo test alone passed on that runner, and the same tests passed in seconds on a developer Mac and on
`ubuntu-latest`. Adding all three attributes in one change made the macOS leg pass, so it is not known which one
mattered. The likely cause is a signal sent to the browser's process group, which until then also held the test host
and the runner.

**What breaks if you ignore it** — If a refactor drops the attributes (for example "simplify the spawn back to file
actions only"), the macOS CI leg wedges for its whole job timeout and produces no log. Under the MCP server, the
same signal could also reach clio's own process group.
