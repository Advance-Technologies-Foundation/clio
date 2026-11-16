---
description: a secret a host gives clio for an agent's call (manage-user password-file) is a NAME resolved in <clio home>/secrets by ClioSecretFileReader, never a value, a path or a process variable; the reader refuses links, files other users can read, empty/oversized/multi-line/non-UTF-8 files, and never repeats an invalid name
applies-to:
  - clio/Common/ClioSecretFileReader.cs
  - clio/Command/Administration/ManageUserCommand.cs
  - clio/Command/McpServer/Tools/ManageUserTool.cs
date: 2026-11-14
---

**What is true** — `manage-user` `create` and `password` take the new password through `password-file`: the name of a
file in `<clio home>/secrets/` (`ClioRuntimePaths.Home`, which honors `CLIO_HOME`). `IClioSecretFileReader.Read`
resolves it. It refuses a name that is not a plain file name without repeating it, a linked folder or file, a file
others can read (macOS/Linux), and a missing, empty, oversized (over 4096 bytes), multi-line or non-UTF-8 file. It
drops a trailing line break and a byte order mark and keeps other whitespace. `password-env` still works; naming
both is refused.

**Why it is this way** — an MCP host runs clio as a long-lived process, and on macOS other processes of the same
user can read its environment with `ps eww` (measured 14 Nov 2026 on macOS with SIP on). An agent's shell is such a
process, so a host that keeps the password from the agent cannot use `password-env`. A file in clio's home has the
protection clio's settings have, which already hold the environment's sign-in, and a name cannot leave the folder.

**What breaks if you ignore it** — taking a path instead of a name lets a call point clio at any file the person can
read. Echoing an invalid name echoes a password pasted by mistake. Following a link lets a swapped file redirect the
read. A new secret-taking argument should call `IClioSecretFileReader`, not invent a second rule, so a host learns
one. Design: `spec/adr/adr-manage-user-password-file.md`.
